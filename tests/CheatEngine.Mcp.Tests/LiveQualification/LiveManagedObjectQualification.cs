using System.Globalization;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text.Json.Nodes;

using CheatEngine.Mcp.Core.Contract;

namespace CheatEngine.Mcp.Tests.LiveQualification;

/// <summary>Bounded live coverage for the owned target's pinned managed object.</summary>
[SupportedOSPlatform("windows")]
internal static class LiveManagedObjectQualification
{
	private const int PageSize = 32;
	private const int MaximumPages = 4;
	private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(10);

	internal static async Task VerifyAsync(LiveSandboxSession sandbox, LiveMcpInstanceClient instance, string managedAssemblyPath)
	{
		ManagedObjectProbeFacts probe = RequireProbe(sandbox.HostA);
		ManagedAssemblyExpectation assembly = ReadManagedAssemblyExpectation(managedAssemblyPath);
		(string Name, int Value)[] fields = [("Marker", probe.Marker), ("Mirror", probe.Mirror), ("Toggle", probe.Toggle)];
		string structure = "McpLiveManagedObject_" + sandbox.HostA.TargetProcessId.ToString(CultureInfo.InvariantCulture) + "_" + Guid.NewGuid().ToString("N");
		HashSet<string>? jobsBeforeStart = null;
		HashSet<string> ownedJobIds = new(StringComparer.Ordinal);
		bool startAttempted = false;
		bool structureFillAttempted = false;
		Exception? failure = null;
		List<Exception> cleanup = [];
		try
		{
			(JsonNode module, JsonNode type) = await FindProbeTypeAsync(instance, assembly, probe.TypeName);
			string moduleHandle = Text(module, "handle");
			string typeToken = Text(type, "token");
			JsonNode layout = await CallAsync(instance, CheatEngineToolNames.DotNetGetType,
				new Dictionary<string, object?> { ["moduleHandle"] = moduleHandle, ["typeToken"] = typeToken, ["maximumFields"] = 32 });
			Assert.Equal((moduleHandle, typeToken, probe.TypeName), (Text(layout, "moduleHandle"), Text(layout, "typeToken"), Text(layout, "name")));
			AssertExactInstanceLayout(layout, fields);

			JsonNode actual = await CallAsync(instance, CheatEngineToolNames.DotNetGetObject,
				new Dictionary<string, object?> { ["address"] = probe.Address, ["maximumFields"] = 32 });
			Assert.Equal((probe.Address, probe.TypeName), (NormalizeAddress(Text(actual, "address")), Text(actual, "typeName")));
			AssertExactObject(actual, layout, fields);
			foreach ((string name, int value) in fields)
			{
				JsonNode raw = await CallAsync(instance, CheatEngineToolNames.MemoryRead, new Dictionary<string, object?>
				{
					["address"] = Add(probe.Address, SingleField(layout, name)["offset"]!.GetValue<long>()),
					["valueType"] = "int32"
				});
				Assert.Equal(value.ToString(CultureInfo.InvariantCulture), Text(raw, "value"));
			}
			Receipt(sandbox, CheatEngineToolNames.DotNetGetObject, actual);

			jobsBeforeStart = await SnapshotJobIdsAsync(instance);
			startAttempted = true;
			JsonNode started = await CallAsync(instance, CheatEngineToolNames.DotNetStartInstanceSearch, new Dictionary<string, object?>
			{
				["moduleHandle"] = moduleHandle,
				["typeToken"] = typeToken,
				["maximumResults"] = 64,
				["lifetimeSeconds"] = 30
			});
			string jobId = Text(started, "jobId");
			ownedJobIds.Add(jobId);
			Assert.Equal((moduleHandle, typeToken, 64), (Text(started, "moduleHandle"), Text(started, "typeToken"), started["maximumResults"]!.GetValue<int>()));
			Receipt(sandbox, CheatEngineToolNames.DotNetStartInstanceSearch, started);
			JsonNode page = await WaitForCompletedPageAsync(instance, jobId);
			Assert.Equal("completed", Text(page["job"]!, "state"));
			Assert.Equal(0L, page["dropped"]!.GetValue<long>());
			Assert.Contains(page["instances"]!.AsArray(), item => string.Equals(NormalizeAddress(Text(item!, "address")), probe.Address, StringComparison.OrdinalIgnoreCase));
			Receipt(sandbox, CheatEngineToolNames.DotNetPollInstanceSearch, page);

			await AssertStructureAbsentAsync(instance, structure);
			structureFillAttempted = true;
			JsonNode filled = await CallAsync(instance, CheatEngineToolNames.StructureFillFromDotNet, new Dictionary<string, object?>
			{
				["name"] = structure,
				["address"] = probe.Address,
				["rename"] = false,
				["createIfMissing"] = true
			});
			JsonNode definition = await CallAsync(instance, CheatEngineToolNames.StructureGet,
				new Dictionary<string, object?> { ["name"] = structure, ["offset"] = 0, ["limit"] = 32, ["format"] = "detailed" });
			AssertExactStructureLayout(definition, filled, layout, structure, fields);
			Receipt(sandbox, CheatEngineToolNames.StructureFillFromDotNet, filled);
		}
		catch (Exception exception) { failure = exception; throw; }
		finally
		{
			if (startAttempted)
			{
				try
				{
					string[] discovered = [.. await DiscoverOwnedJobIdsAsync(instance, jobsBeforeStart)];
					foreach (string jobId in discovered)
					{
						ownedJobIds.Add(jobId);
					}

					if (discovered.Length > 1)
					{
						cleanup.Add(new InvalidOperationException($"The one owned start created {discovered.Length} dotnetinstances jobs."));
					}
				}
				catch (Exception exception) { cleanup.Add(exception); }
			}
			foreach (string jobId in ownedJobIds)
			{
				await CleanupAsync(cleanup, instance, (client, token) => StopAndAssertJobAbsentAsync(client, jobId, token));
			}

			if (ownedJobIds.Count != 0)
			{
				sandbox.Record("managed_object_job_cleanup", new
				{
					jobIds = ownedJobIds.Order().ToArray()
				});
			}

			if (structureFillAttempted)
			{
				await CleanupAsync(cleanup, instance, (client, _) => DeleteOrAssertStructureAbsentAsync(client, structure));
			}

			if (cleanup.Count == 0 && ownedJobIds.Count != 0 && structureFillAttempted)
			{
				sandbox.Record("live_managed_object_cleanup", new
				{
					jobIds = ownedJobIds.Order().ToArray(),
					jobsAbsent = true,
					structure,
					structureAbsent = true
				});
			}
			LiveLifecycleQualification.ThrowWithCleanup(failure, cleanup);
		}
	}

	private static ManagedObjectProbeFacts RequireProbe(LiveSandboxHost host)
	{
		string address = NormalizeAddress(host.ManagedObjectAddress);
		if (string.IsNullOrWhiteSpace(host.ManagedObjectType) || !ulong.TryParse(address, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out ulong bits)
			|| bits == 0 || host.ManagedObjectMarker is not int marker || host.ManagedObjectMirror is not int mirror || host.ManagedObjectToggle is not int toggle)
		{
			throw new InvalidOperationException("The CoreCLR live target did not publish a complete managed-object probe manifest.");
		}
		return new ManagedObjectProbeFacts(address, host.ManagedObjectType, marker, mirror, toggle);
	}

	private static async Task<(JsonNode Module, JsonNode Type)> FindProbeTypeAsync(LiveMcpInstanceClient instance, ManagedAssemblyExpectation expected, string typeName)
	{
		using CancellationTokenSource deadline = new(Deadline);
		LiveMcpInstanceClient bounded = instance.WithCancellation(deadline.Token);
		int domainOffset = 0;
		for (int domainPage = 0; domainPage < MaximumPages; domainPage++)
		{
			JsonNode domains = await CallAsync(bounded, CheatEngineToolNames.DotNetListDomains, new Dictionary<string, object?> { ["offset"] = domainOffset, ["limit"] = PageSize });
			foreach (JsonNode? domain in domains["domains"]!.AsArray())
			{
				JsonNode? module = await FindOwnedModuleAsync(bounded, Text(domain!, "handle"), expected, deadline.Token);
				if (module is null)
				{
					continue;
				}

				JsonNode types = await CallAsync(bounded, CheatEngineToolNames.DotNetListTypes, new Dictionary<string, object?>
				{
					["moduleHandle"] = Text(module, "handle"),
					["offset"] = 0,
					["limit"] = PageSize,
					["nameContains"] = typeName
				});
				if (types["nextOffset"] is not null)
				{
					throw new InvalidOperationException("The owned type filter exceeded the one-page qualification cap.");
				}

				JsonNode type = Assert.Single(types["types"]!.AsArray(), item => item is not null && Text(item, "name") == typeName)!;
				Assert.Equal(Text(module, "handle"), Text(types, "moduleHandle"));
				return (module, type);
			}
			if (domains["nextOffset"] is not { } next)
			{
				break;
			}

			domainOffset = next.GetValue<int>();
		}
		throw new InvalidOperationException($"The collector did not expose owned module '{expected.FullPath}' (SHA-256 {expected.Sha256}) within {MaximumPages} bounded domain pages.");
	}

	private static async Task<JsonNode?> FindOwnedModuleAsync(LiveMcpInstanceClient instance, string domainHandle, ManagedAssemblyExpectation expected, CancellationToken token)
	{
		int offset = 0;
		for (int page = 0; page < MaximumPages; page++)
		{
			token.ThrowIfCancellationRequested();
			JsonNode modules = await CallAsync(instance, CheatEngineToolNames.DotNetListModules, new Dictionary<string, object?> { ["domainHandle"] = domainHandle, ["offset"] = offset, ["limit"] = PageSize });
			JsonNode? module = modules["modules"]!.AsArray().SingleOrDefault(item => item is not null && IsExpectedOwnedModule(item, expected));
			if (module is not null)
			{
				return module;
			}

			if (modules["nextOffset"] is not { } next)
			{
				return null;
			}

			offset = next.GetValue<int>();
		}
		throw new InvalidOperationException($"The collector truncated module discovery for domain '{domainHandle}' at {MaximumPages} pages.");
	}

	private static async Task<JsonNode> WaitForCompletedPageAsync(LiveMcpInstanceClient instance, string jobId)
	{
		using CancellationTokenSource deadline = new(Deadline);
		LiveMcpInstanceClient bounded = instance.WithCancellation(deadline.Token);
		while (true)
		{
			deadline.Token.ThrowIfCancellationRequested();
			JsonNode page = await CallAsync(bounded, CheatEngineToolNames.DotNetPollInstanceSearch, new Dictionary<string, object?> { ["jobId"] = jobId, ["afterSequence"] = 0L, ["limit"] = 64 });
			string state = Text(page["job"]!, "state");
			if (state == "completed")
			{
				return page;
			}

			Assert.True(state is "running" or "stopping", $"Unexpected instance-search job state '{state}'.");
			await Task.Delay(50, deadline.Token);
		}
	}

	private static void AssertExactInstanceLayout(JsonNode layout, (string Name, int Value)[] fields)
	{
		JsonArray all = layout["fields"]!.AsArray();
		Assert.Equal(fields.Length, all.Count);
		JsonNode[] actual = all.Where(field => field is not null && !field["isStatic"]!.GetValue<bool>()).Cast<JsonNode>().ToArray();
		Assert.Equal(fields.Length, actual.Length);
		Assert.Equal(fields.Select(field => field.Name).Order(), actual.Select(field => Text(field, "name")).Order());
		Assert.All(actual, field => Assert.Equal(8L, field["elementType"]!.GetValue<long>()));
	}

	private static void AssertExactObject(JsonNode actual, JsonNode layout, (string Name, int Value)[] fields)
	{
		JsonArray objects = actual["fields"]!.AsArray();
		Assert.Equal(fields.Select(field => field.Name).Order(), objects.Select(field => Text(field!, "name")).Order());
		foreach ((string name, int value) in fields)
		{
			JsonNode objectField = Assert.Single(objects, field => Text(field!, "name") == name)!;
			JsonNode layoutField = SingleField(layout, name);
			Assert.Equal((value.ToString(CultureInfo.InvariantCulture), 8L, layoutField["offset"]!.GetValue<long>()), (Text(objectField, "value"), objectField["elementType"]!.GetValue<long>(), objectField["offset"]!.GetValue<long>()));
		}
	}

	private static void AssertExactStructureLayout(JsonNode definition, JsonNode filled, JsonNode layout, string structure, (string Name, int Value)[] fields)
	{
		int expectedSize = fields.Max(field => checked((int) SingleField(layout, field.Name)["offset"]!.GetValue<long>() + 4));
		Assert.Equal((structure, expectedSize, fields.Length, fields.Length), (Text(definition, "name"), definition["size"]!.GetValue<int>(), definition["elementCount"]!.GetValue<int>(), definition["total"]!.GetValue<int>()));
		Assert.Null(definition["nextOffset"]);
		JsonArray elements = definition["elements"]!.AsArray();
		Assert.Equal(fields.Length, elements.Count);
		Assert.Equal(fields.Select(field => field.Name).Order(), elements.Select(element => Text(element!, "name")).Order());
		foreach ((string name, _) in fields)
		{
			JsonNode element = Assert.Single(elements, item => Text(item!, "name") == name)!;
			Assert.Equal("int32", Text(element, "valueType"));
			Assert.Equal(4, element["byteSize"]!.GetValue<int>());
			Assert.Equal(SingleField(layout, name)["offset"]!.GetValue<long>(), ParseHexOffset(Text(element, "offset")));
		}
		Assert.Equal((structure, expectedSize, fields.Length), (Text(filled, "name"), filled["size"]!.GetValue<int>(), filled["elementCount"]!.GetValue<int>()));
	}

	private static JsonNode SingleField(JsonNode layout, string name) => Assert.Single(layout["fields"]!.AsArray(), field => field is not null && !field["isStatic"]!.GetValue<bool>() && Text(field, "name") == name)!;
	private static async Task<HashSet<string>> SnapshotJobIdsAsync(LiveMcpInstanceClient instance) => [.. (await CallAsync(instance, CheatEngineToolNames.RuntimeListJobs))["jobs"]!.AsArray().Select(job => Text(job!, "jobId"))];
	private static async Task<IEnumerable<string>> DiscoverOwnedJobIdsAsync(LiveMcpInstanceClient instance, HashSet<string>? before)
	{
		if (before is null)
		{
			throw new InvalidOperationException("The instance-search job snapshot was not captured before start.");
		}

		using CancellationTokenSource deadline = new(Deadline);
		JsonNode jobs = await CallAsync(instance.WithCancellation(deadline.Token), CheatEngineToolNames.RuntimeListJobs);
		return [.. jobs["jobs"]!.AsArray().Where(job => job is not null && !before.Contains(Text(job, "jobId")) && Text(job, "kind") == "dotnetinstances").Select(job => Text(job!, "jobId"))];
	}

	private static async Task StopAndAssertJobAbsentAsync(LiveMcpInstanceClient instance, string jobId, CancellationToken token)
	{
		bool releasedOrAbsent = false;
		for (int attempt = 0; attempt < 5; attempt++)
		{
			LiveMcpToolResult result = await instance.CallToolRawAsync(CheatEngineToolNames.RuntimeStopJob, new Dictionary<string, object?> { ["jobId"] = jobId });
			if (!result.IsError)
			{
				Assert.Equal(jobId, Text(Payload(result), "jobId"));
				Assert.True(Payload(result)["released"]!.GetValue<bool>());
				releasedOrAbsent = true;
				break;
			}
			JsonNode error = Payload(result)["error"]!;
			if (Text(error, "kind") == "not_found")
			{
				releasedOrAbsent = true;
				break;
			}
			if (Text(error, "kind") != "partial_effect" || !error["retryable"]!.GetValue<bool>())
			{
				throw new InvalidOperationException("Managed-object job cleanup failed: " + error.ToJsonString());
			}

			await Task.Delay(100, token);
		}
		if (!releasedOrAbsent)
		{
			throw new TimeoutException("Managed-object job cleanup remained pending after five retryable stop responses.");
		}

		LiveMcpToolResult absent = await instance.CallToolRawAsync(CheatEngineToolNames.DotNetPollInstanceSearch, new Dictionary<string, object?> { ["jobId"] = jobId, ["afterSequence"] = 0L, ["limit"] = 1 });
		Assert.True(absent.IsError);
		Assert.Equal("not_found", Text(Payload(absent)["error"], "kind"));
	}

	private static async Task AssertStructureAbsentAsync(LiveMcpInstanceClient instance, string name)
	{
		LiveMcpToolResult result = await instance.CallToolRawAsync(CheatEngineToolNames.StructureGet, new Dictionary<string, object?> { ["name"] = name, ["offset"] = 0, ["limit"] = 1 });
		Assert.True(result.IsError);
		Assert.Equal("not_found", Text(Payload(result)["error"], "kind"));
	}
	private static async Task DeleteOrAssertStructureAbsentAsync(LiveMcpInstanceClient instance, string name)
	{
		LiveMcpToolResult deleted = await instance.CallToolRawAsync(CheatEngineToolNames.StructureDelete, new Dictionary<string, object?> { ["name"] = name });
		if (deleted.IsError)
		{
			Assert.Equal("not_found", Text(Payload(deleted)["error"], "kind"));
		}
		else
		{
			Assert.Equal(name, Text(Payload(deleted), "name"));
		}

		await AssertStructureAbsentAsync(instance, name);
	}
	private static async Task CleanupAsync(List<Exception> failures, LiveMcpInstanceClient instance, Func<LiveMcpInstanceClient, CancellationToken, Task> action)
	{
		using CancellationTokenSource deadline = new(Deadline);
		try
		{
			await action(instance.WithCancellation(deadline.Token), deadline.Token);
		}
		catch (Exception exception) { failures.Add(exception); }
	}
	private static ManagedAssemblyExpectation ReadManagedAssemblyExpectation(string path)
	{
		string fullPath = Path.GetFullPath(path);
		using FileStream file = File.OpenRead(fullPath);
		return new ManagedAssemblyExpectation(fullPath, Path.GetFileName(fullPath), Convert.ToHexString(SHA256.HashData(file)));
	}
	private static bool IsExpectedOwnedModule(JsonNode module, ManagedAssemblyExpectation expected)
	{
		string observed = Path.GetFullPath(Text(module, "name"));
		if (!string.Equals(Path.GetFileName(observed), expected.FileName, StringComparison.OrdinalIgnoreCase) || !string.Equals(observed, expected.FullPath, StringComparison.OrdinalIgnoreCase) || !File.Exists(observed))
		{
			return false;
		}

		using FileStream file = File.OpenRead(observed);
		return string.Equals(Convert.ToHexString(SHA256.HashData(file)), expected.Sha256, StringComparison.Ordinal);
	}
	private static string NormalizeAddress(string address) => address.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? address[2..] : address;
	private static string Add(string address, long offset) => (ulong.Parse(NormalizeAddress(address), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture) + checked((ulong) offset)).ToString("X", CultureInfo.InvariantCulture);
	private static long ParseHexOffset(string text) => text.StartsWith('-') ? -long.Parse(text[1..], NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture) : long.Parse(text.TrimStart('+'), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture);
	private static void Receipt(LiveSandboxSession sandbox, string tool, JsonNode result) => sandbox.Record("all_tools_" + tool, result.DeepClone());
	private static string Text(JsonNode? node, string property) => node?[property]?.GetValue<string>() ?? throw new InvalidDataException($"Missing string '{property}'.");
	private static JsonNode Payload(LiveMcpToolResult result) => result.Payload ?? throw new InvalidDataException("Expected tool payload.");
	private static async Task<JsonNode> CallAsync(LiveMcpInstanceClient instance, string tool, IReadOnlyDictionary<string, object?>? arguments = null) => await instance.CallToolAsync(tool, arguments) ?? throw new InvalidDataException($"Tool '{tool}' returned no payload.");
	private sealed record ManagedAssemblyExpectation(string FullPath, string FileName, string Sha256);
	private sealed record ManagedObjectProbeFacts(string Address, string TypeName, int Marker, int Mirror, int Toggle);
}
