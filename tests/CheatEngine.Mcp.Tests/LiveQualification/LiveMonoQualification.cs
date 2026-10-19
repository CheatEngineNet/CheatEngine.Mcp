using System.Diagnostics;
using System.Globalization;
using System.Runtime.Versioning;
using System.Text.Json.Nodes;

using CheatEngine.Mcp.Core.Contract;

namespace CheatEngine.Mcp.Tests.LiveQualification;

/// <summary>Bounded evidence for the Mono collector against one owned x64 Mono process.</summary>
[SupportedOSPlatform("windows")]
internal static class LiveMonoQualification
{
	private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(15);
	private static readonly string[] FieldNames = ["Counter", "Magic", "Speed", "StaticMagic", "StaticRoot"];

	internal static async Task VerifyAsync(LiveSandboxSession sandbox, LiveMcpInstanceClient instanceA, string repositoryRoot)
	{
		ArgumentNullException.ThrowIfNull(sandbox);
		ArgumentNullException.ThrowIfNull(instanceA);
		using CancellationTokenSource overall = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
		overall.CancelAfter(TimeSpan.FromSeconds(90));
		LiveMcpInstanceClient instance = instanceA.WithCancellation(overall.Token);
		await using LiveMonoTarget target = await LiveMonoTarget.StartAsync(sandbox, repositoryRoot, overall.Token);
		Exception? failure = null;
		List<Exception> cleanup = [];
		string? jobId = null;
		bool attached = false;
		bool searchAttempted = false;
		HashSet<string>? jobsBefore = null;
		HashSet<string> ownedJobs = new(StringComparer.Ordinal);
		bool collectorDetached = false;
		JsonNode? staticInvoke = null;
		JsonNode? instanceInvoke = null;
		try
		{
			JsonNode selected = await CallAsync(instance, CheatEngineToolNames.ProcessAttach, new Dictionary<string, object?> { ["process"] = target.ProcessId.ToString(CultureInfo.InvariantCulture) });
			Assert.Equal(target.ProcessId, selected["processId"]!.GetValue<int>());
			JsonNode current = await CallAsync(instance, CheatEngineToolNames.ProcessGetCurrent);
			Assert.True(current["isOpen"]!.GetValue<bool>());
			Assert.Equal(target.ProcessId, current["processId"]!.GetValue<int>());
			JsonNode runtime = await CallAsync(instance, CheatEngineToolNames.RuntimeGetInfo);
			JsonNode gates = runtime["gates"] ?? throw new InvalidDataException("Mono runtime gates are missing.");
			Assert.True(gates["targetCodeExecution"]!.GetValue<bool>());
			Assert.False(gates["unsafeLua"]!.GetValue<bool>());
			Assert.False(gates["autoAssembler"]!.GetValue<bool>());
			Assert.False(gates["kernelAccess"]!.GetValue<bool>());

			attached = true;
			JsonNode attach = await CallAsync(instance, CheatEngineToolNames.MonoAttach);
			Assert.True(attach["attached"]!.GetValue<bool>());
			Assert.False(attach["il2Cpp"]!.GetValue<bool>());
			Assert.True(attach["collectorVersion"]!.GetValue<long>() > 0);
			foreach (string effect in new[] { "collector_injected", "mono_error_ok_patched", "cheat_engine_hooks_installed", "uses_mono_table_option" })
			{
				Assert.Contains(effect, attach["hostEffects"]!.AsArray().Select(item => item!.GetValue<string>()));
			}
			Receipt(sandbox, CheatEngineToolNames.MonoAttach, attach);
			JsonNode status = await CallAsync(instance, CheatEngineToolNames.MonoGetStatus);
			Assert.True(status["attached"]!.GetValue<bool>());
			Assert.False(status["il2Cpp"]!.GetValue<bool>());
			Assert.Equal(attach["collectorVersion"]!.GetValue<long>(), status["collectorVersion"]!.GetValue<long>());
			JsonArray domains = status["domains"]?.AsArray() ?? throw new InvalidDataException("Mono status has no domain.");
			Assert.NotEmpty(domains);
			Assert.InRange(domains.Count, 1, 16);
			Assert.All(domains, domain => Assert.True(ulong.TryParse(domain!.GetValue<string>(), NumberStyles.None, CultureInfo.InvariantCulture, out ulong value) && value > 0));
			Receipt(sandbox, CheatEngineToolNames.MonoGetStatus, status);

			JsonNode assemblies = await CallAsync(instance, CheatEngineToolNames.MonoListAssemblies, new Dictionary<string, object?> { ["offset"] = 0, ["limit"] = 1000 });
			AssertCompletePage(assemblies, "assemblies", 1000);
			JsonNode assembly = assemblies["assemblies"]!.AsArray().Single(item => string.Equals(Text(item!, "name"), Path.GetFileName(target.Facts.AssemblyPath), StringComparison.OrdinalIgnoreCase))!;
			string image = Text(assembly, "imageHandle");
			Receipt(sandbox, CheatEngineToolNames.MonoListAssemblies, assemblies);
			JsonNode classes = await CallAsync(instance, CheatEngineToolNames.MonoListClasses, new Dictionary<string, object?> { ["imageHandle"] = image, ["offset"] = 0, ["limit"] = 32, ["nameContains"] = target.Facts.ClassName });
			AssertCompletePage(classes, "classes", 32);
			Assert.Equal(image, Text(classes, "imageHandle"));
			JsonNode listedClass = Assert.Single(classes["classes"]!.AsArray())!;
			Assert.Equal((target.Facts.ClassName, target.Facts.Namespace), (Text(listedClass, "name"), Text(listedClass, "namespace")));
			Receipt(sandbox, CheatEngineToolNames.MonoListClasses, classes);
			JsonNode type = await CallAsync(instance, CheatEngineToolNames.MonoFindClass, new Dictionary<string, object?> { ["namespace"] = target.Facts.Namespace, ["className"] = target.Facts.ClassName });
			string classHandle = Text(type, "handle");
			Assert.Equal(Text(listedClass, "handle"), classHandle);
			Assert.Equal(target.Facts.ClassName, Text(type, "name"));
			Assert.Equal(target.Facts.Namespace, Text(type, "namespace"));
			Receipt(sandbox, CheatEngineToolNames.MonoFindClass, type);

			JsonNode fields = await CallAsync(instance, CheatEngineToolNames.MonoListFields, new Dictionary<string, object?> { ["classHandle"] = classHandle, ["maximumFields"] = 32 });
			JsonArray fieldList = fields["fields"]!.AsArray();
			Assert.Equal(classHandle, Text(fields, "classHandle"));
			Assert.Equal(5, fieldList.Count);
			Assert.Equal(FieldNames, fieldList.Select(field => Text(field!, "name")).Order(StringComparer.Ordinal));
			JsonNode magic = Field(fieldList, "Magic");
			JsonNode counter = Field(fieldList, "Counter");
			JsonNode speed = Field(fieldList, "Speed");
			JsonNode staticMagic = Field(fieldList, "StaticMagic");
			Assert.False(magic["isStatic"]!.GetValue<bool>());
			Assert.True(staticMagic["isStatic"]!.GetValue<bool>());
			Assert.Equal("System.Int32", Text(magic, "typeName"));
			Assert.Equal("System.Int64", Text(counter, "typeName"));
			Assert.Equal("System.Double", Text(speed, "typeName"));
			Assert.Equal((16L, 24L, 32L), (magic["offset"]!.GetValue<long>(), counter["offset"]!.GetValue<long>(), speed["offset"]!.GetValue<long>()));
			Assert.False(counter["isStatic"]!.GetValue<bool>());
			Assert.False(speed["isStatic"]!.GetValue<bool>());
			Assert.Equal("System.Int32", Text(staticMagic, "typeName"));
			Assert.InRange(staticMagic["offset"]!.GetValue<long>(), 0, 64);
			Assert.Equal(0, staticMagic["offset"]!.GetValue<long>() % 4);
			Receipt(sandbox, CheatEngineToolNames.MonoListFields, fields);
			JsonNode staticAddress = await CallAsync(instance, CheatEngineToolNames.MonoGetStaticFieldAddress, new Dictionary<string, object?> { ["classHandle"] = classHandle, ["domainHandle"] = domains[0]!.GetValue<string>() });
			Assert.Equal((classHandle, domains[0]!.GetValue<string>()), (Text(staticAddress, "classHandle"), Text(staticAddress, "domainHandle")));
			Assert.True(ParseHex(Text(staticAddress, "address")) > 0);
			await AssertMemoryAsync(instance, Add(target.Facts.ObjectPointer, 16), "int32", "314159");
			await AssertMemoryAsync(instance, Add(target.Facts.ObjectPointer, 24), "int64", "7000");
			await AssertMemoryAsync(instance, Add(target.Facts.ObjectPointer, 32), "double", "1.25");
			await AssertMemoryAsync(instance, Add(Text(staticAddress, "address"), staticMagic["offset"]!.GetValue<long>()), "int32", "424242");
			Receipt(sandbox, CheatEngineToolNames.MonoGetStaticFieldAddress, staticAddress);

			JsonNode methods = await CallAsync(instance, CheatEngineToolNames.MonoListMethods, new Dictionary<string, object?> { ["classHandle"] = classHandle, ["offset"] = 0, ["limit"] = 32 });
			AssertCompletePage(methods, "methods", 32);
			Assert.Equal(classHandle, Text(methods, "classHandle"));
			JsonNode listedStatic = Field(methods["methods"]!.AsArray(), "StaticAdd42");
			JsonNode listedInstance = Field(methods["methods"]!.AsArray(), "InstanceBumpCounter");
			AssertMethod(listedStatic, "StaticAdd42", true, "value", "System.Int32");
			AssertMethod(listedInstance, "InstanceBumpCounter", false, "delta", "System.Int64");
			Receipt(sandbox, CheatEngineToolNames.MonoListMethods, methods);
			JsonNode staticMethod = await CallAsync(instance, CheatEngineToolNames.MonoFindMethod, new Dictionary<string, object?> { ["classHandle"] = classHandle, ["methodName"] = "StaticAdd42" });
			JsonNode instanceMethod = await CallAsync(instance, CheatEngineToolNames.MonoFindMethod, new Dictionary<string, object?> { ["classHandle"] = classHandle, ["methodName"] = "InstanceBumpCounter" });
			Assert.True(staticMethod["isStatic"]!.GetValue<bool>());
			Assert.False(instanceMethod["isStatic"]!.GetValue<bool>());
			AssertMethod(staticMethod, "StaticAdd42", true, "value", "System.Int32");
			AssertMethod(instanceMethod, "InstanceBumpCounter", false, "delta", "System.Int64");
			Assert.Equal((Text(listedStatic, "handle"), Text(listedInstance, "handle")), (Text(staticMethod, "handle"), Text(instanceMethod, "handle")));
			Receipt(sandbox, CheatEngineToolNames.MonoFindMethod, new JsonObject { ["static"] = staticMethod.DeepClone(), ["instance"] = instanceMethod.DeepClone() });
			JsonNode compiled = await CallAsync(instance, CheatEngineToolNames.MonoCompileMethod, new Dictionary<string, object?> { ["methodHandle"] = Text(staticMethod, "handle") });
			Assert.Equal(Text(staticMethod, "handle"), Text(compiled, "methodHandle"));
			Assert.True(ulong.TryParse(Normalize(Text(compiled, "nativeAddress")), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out ulong nativeAddress) && nativeAddress != 0);
			Receipt(sandbox, CheatEngineToolNames.MonoCompileMethod, compiled);
			staticInvoke = await CallAsync(instance, CheatEngineToolNames.MonoInvokeMethod, new Dictionary<string, object?> { ["methodHandle"] = Text(staticMethod, "handle"), ["arguments"] = new[] { new { type = 2, value = "0" } } });
			Assert.True(staticInvoke["invoked"]!.GetValue<bool>());
			Assert.Null(staticInvoke["exception"]);
			Assert.Equal("42", Text(staticInvoke, "returnValue"));
			instanceInvoke = await CallAsync(instance, CheatEngineToolNames.MonoInvokeMethod, new Dictionary<string, object?> { ["methodHandle"] = Text(instanceMethod, "handle"), ["instanceAddress"] = target.Facts.ObjectPointer, ["arguments"] = new[] { new { type = 2, value = target.Facts.ExpectedBumpArgument.ToString(CultureInfo.InvariantCulture) } } });
			Assert.True(instanceInvoke["invoked"]!.GetValue<bool>());
			Assert.Null(instanceInvoke["exception"]);
			Assert.Equal(target.Facts.ExpectedCounterAfterInvoke.ToString(CultureInfo.InvariantCulture), Text(instanceInvoke, "returnValue"));

			JsonNode obj = await CallAsync(instance, CheatEngineToolNames.MonoGetObject, new Dictionary<string, object?> { ["address"] = target.Facts.ObjectPointer, ["maxBacktrack"] = 0, ["maximumFields"] = 32 });
			Assert.Equal(target.Facts.ObjectPointer, Normalize(Text(obj, "address")));
			Assert.Equal(target.Facts.ClassName, Text(obj, "className"));
			Assert.Equal(target.Facts.Namespace, Text(obj, "namespace"));
			Assert.Equal((0L, classHandle, image, 5), (obj["offsetInObject"]!.GetValue<long>(), Text(obj, "classHandle"), Text(obj, "imageHandle"), obj["totalFields"]!.GetValue<int>()));
			Assert.Equal(5, obj["fields"]!.AsArray().Count);
			AssertExactObjectFields(obj["fields"]!.AsArray(), fieldList, target.Facts.ObjectPointer, Text(staticAddress, "address"));
			AssertField(obj["fields"]!.AsArray(), "Speed", "1.25");
			AssertField(obj["fields"]!.AsArray(), "StaticMagic", "424242");
			AssertField(obj["fields"]!.AsArray(), "Magic", target.Facts.Magic.ToString(CultureInfo.InvariantCulture));
			AssertField(obj["fields"]!.AsArray(), "Counter", target.Facts.ExpectedCounterAfterInvoke.ToString(CultureInfo.InvariantCulture));
			Receipt(sandbox, CheatEngineToolNames.MonoGetObject, obj);
			await AssertMemoryAsync(instance, Add(target.Facts.ObjectPointer, magic["offset"]!.GetValue<long>()), "int32", target.Facts.Magic.ToString(CultureInfo.InvariantCulture));
			await AssertMemoryAsync(instance, Add(target.Facts.ObjectPointer, counter["offset"]!.GetValue<long>()), "int64", target.Facts.ExpectedCounterAfterInvoke.ToString(CultureInfo.InvariantCulture));
			await AssertMemoryAsync(instance, Add(target.Facts.ObjectPointer, speed["offset"]!.GetValue<long>()), "double", target.Facts.Speed.ToString(CultureInfo.InvariantCulture));
			await AssertMemoryAsync(instance, Add(Text(staticAddress, "address"), staticMagic["offset"]!.GetValue<long>()), "int32", target.Facts.StaticMagic.ToString(CultureInfo.InvariantCulture));
			Receipt(sandbox, CheatEngineToolNames.MonoInvokeMethod, new JsonObject { ["static"] = staticInvoke.DeepClone(), ["instance"] = instanceInvoke.DeepClone(), ["calls"] = 2 });

			jobsBefore = JobIds(await CallAsync(instance, CheatEngineToolNames.RuntimeListJobs));
			searchAttempted = true;
			JsonNode search = await CallAsync(instance, CheatEngineToolNames.MonoStartInstanceSearch, new Dictionary<string, object?> { ["classHandle"] = classHandle, ["maximumResults"] = 64, ["lifetimeSeconds"] = 30 });
			jobId = Text(search, "jobId");
			Receipt(sandbox, CheatEngineToolNames.MonoStartInstanceSearch, search);
			ownedJobs.Add(jobId);
			Assert.Equal((classHandle, 64), (Text(search, "classHandle"), search["maximumResults"]!.GetValue<int>()));
			JsonNode page = await PollAsync(instance, jobId, overall.Token);
			Assert.Equal(jobId, Text(page["job"]!, "jobId"));
			Assert.Equal(0L, page["dropped"]!.GetValue<long>());
			int returned = page["instances"]!.AsArray().Count;
			Assert.False(page["more"]!.GetValue<bool>());
			Assert.Equal((1L, (long) returned), (page["firstSequence"]!.GetValue<long>(), page["nextAfterSequence"]!.GetValue<long>()));
			Assert.Equal("monoinstances", Text(page["job"]!, "kind"));
			Assert.Equal(((long) returned, (long) returned, 0L), (page["job"]!["buffered"]!.GetValue<long>(), page["job"]!["total"]!.GetValue<long>(), page["job"]!["dropped"]!.GetValue<long>()));
			Assert.Equal((long) returned, page["job"]!["progressDone"]!.GetValue<long>());
			Assert.True(page["job"]!["progressTotal"]!.GetValue<long>() >= returned);
			Assert.Null(page["job"]!["error"]);
			Assert.Null(page["job"]!["cleanupError"]);
			Assert.False(page["job"]!["requiresManualRecovery"]!.GetValue<bool>());
			Assert.InRange(page["instances"]!.AsArray().Count, 1, 64);
			Assert.Equal(page["instances"]!.AsArray().Count, page["instances"]!.AsArray().Select(item => ParseHex(Text(item!, "address"))).Distinct().Count());
			Assert.Contains(page["instances"]!.AsArray(), item => string.Equals(Normalize(Text(item!, "address")), target.Facts.ObjectPointer, StringComparison.OrdinalIgnoreCase));
			Receipt(sandbox, CheatEngineToolNames.MonoPollInstanceSearch, page);
		}
		catch (Exception exception) { failure = exception; throw; }
		finally
		{
			if (searchAttempted)
			{
				await CleanupAsync(cleanup, async () =>
				{
					using CancellationTokenSource deadline = new(Deadline);
					JsonNode jobs = await CallAsync(instanceA.WithCancellation(deadline.Token), CheatEngineToolNames.RuntimeListJobs);
					foreach (JsonNode? job in jobs["jobs"]!.AsArray())
					{
						if (Text(job!, "kind") == "monoinstances" && !jobsBefore!.Contains(Text(job!, "jobId")))
						{
							ownedJobs.Add(Text(job!, "jobId"));
						}
					}
				});
				foreach (string ownedJob in ownedJobs)
				{
					await CleanupAsync(cleanup, () => StopJobAsync(instanceA, ownedJob));
				}
			}
			if (attached)
			{
				await CleanupAsync(cleanup, async () =>
			{
				using CancellationTokenSource deadline = new(Deadline);
				LiveMcpInstanceClient bounded = instanceA.WithCancellation(deadline.Token);
				LiveMcpToolResult detached = await bounded.CallToolRawAsync(CheatEngineToolNames.MonoDetach);
				if (detached.IsError)
				{
					Assert.Equal("not_found", Text(detached.Payload!["error"]!, "kind"));
				}
				else
				{
					Assert.True(detached.Payload!["detached"]!.GetValue<bool>());
				}

				JsonNode status = await CallAsync(bounded, CheatEngineToolNames.MonoGetStatus);
				Assert.False(status["attached"]!.GetValue<bool>());
				JsonNode resources = await CallAsync(bounded, CheatEngineToolNames.RuntimeListResources);
				Assert.DoesNotContain(resources["resources"]!.AsArray(), resource => Text(resource!, "kind") == "mono");
				collectorDetached = true;
				if (!detached.IsError)
				{
					Receipt(sandbox, CheatEngineToolNames.MonoDetach, detached.Payload!);
				}
			});
			}

			await CleanupAsync(cleanup, async () => await target.DisposeAsync());
			if (failure is null && cleanup.Count == 0)
			{
				sandbox.Record("live_mono_cleanup", new
				{
					collectorDetached,
					stoppedJobIds = ownedJobs.Order(StringComparer.Ordinal).ToArray(),
					targetExitCode = 0
				});
			}

			LiveLifecycleQualification.ThrowWithCleanup(failure, cleanup);
		}
	}

	private static async Task<JsonNode> PollAsync(LiveMcpInstanceClient original, string jobId, CancellationToken token)
	{
		using CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
		deadline.CancelAfter(Deadline);
		LiveMcpInstanceClient instance = original.WithCancellation(deadline.Token);
		while (true)
		{
			JsonNode page = await CallAsync(instance, CheatEngineToolNames.MonoPollInstanceSearch, new Dictionary<string, object?> { ["jobId"] = jobId, ["afterSequence"] = 0L, ["limit"] = 64 });
			if (Text(page["job"]!, "state") == "completed")
			{
				return page;
			}

			Assert.Equal("running", Text(page["job"]!, "state"));
			await Task.Delay(50, deadline.Token);
		}
	}

	private static async Task StopJobAsync(LiveMcpInstanceClient original, string jobId)
	{
		using CancellationTokenSource deadline = new(Deadline);
		LiveMcpInstanceClient instance = original.WithCancellation(deadline.Token);
		LiveMcpToolResult stopped = await instance.CallToolRawAsync(CheatEngineToolNames.RuntimeStopJob, new Dictionary<string, object?> { ["jobId"] = jobId });
		if (stopped.IsError)
		{
			Assert.Equal("not_found", Text(stopped.Payload!["error"]!, "kind"));
		}
		else
		{
			Assert.True(stopped.Payload!["released"]!.GetValue<bool>());
		}

		JsonNode jobs = await CallAsync(instance, CheatEngineToolNames.RuntimeListJobs);
		Assert.DoesNotContain(jobs["jobs"]!.AsArray(), job => Text(job!, "jobId") == jobId);
		LiveMcpToolResult absent = await instance.CallToolRawAsync(CheatEngineToolNames.MonoPollInstanceSearch, new Dictionary<string, object?> { ["jobId"] = jobId });
		Assert.True(absent.IsError);
		Assert.Equal("not_found", Text(absent.Payload!["error"]!, "kind"));
	}

	private static void AssertCompletePage(JsonNode page, string collection, int maximum)
	{
		Assert.InRange(page["total"]!.GetValue<int>(), 1, maximum);
		Assert.Equal(page["total"]!.GetValue<int>(), page[collection]!.AsArray().Count);
		Assert.Null(page["nextOffset"]);
	}

	private static void AssertExactObjectFields(JsonArray actual, JsonArray listed, string objectAddress, string staticBase)
	{
		Assert.Equal(FieldNames, actual.Select(field => Text(field!, "name")).Order(StringComparer.Ordinal));
		foreach ((string name, string type, long element, bool isStatic, string? value) in new[]
		{
			("Magic", "System.Int32", 8L, false, "314159"), ("Counter", "System.Int64", 10L, false, "7007"),
			("Speed", "System.Double", 13L, false, "1.25"), ("StaticMagic", "System.Int32", 8L, true, "424242"),
			("StaticRoot", "CheatEngine.Mcp.MonoFixture.MonoPrimitiveProbe", 18L, true, (string?) null)
		})
		{
			JsonNode field = Field(actual, name);
			JsonNode metadata = Field(listed, name);
			long offset = metadata["offset"]!.GetValue<long>();
			Assert.InRange(offset, 0, 64);
			Assert.Equal((offset, isStatic, false, element), (field["offset"]!.GetValue<long>(), field["isStatic"]!.GetValue<bool>(), field["isConst"]!.GetValue<bool>(), field["elementType"]!.GetValue<long>()));
			Assert.Equal(isStatic, metadata["isStatic"]!.GetValue<bool>());
			Assert.False(metadata["isConst"]!.GetValue<bool>());
			Assert.Equal(type, Text(field, "typeName"));
			Assert.Equal(type, Text(metadata, "typeName"));
			Assert.Equal(ParseHex(Add(isStatic ? staticBase : objectAddress, offset)), ParseHex(Text(field, "address")));
			if (value is null)
			{
				Assert.Equal(ParseHex(objectAddress), ParseHex(Text(field, "value")));
			}
			else
			{
				Assert.Equal(value, Text(field, "value"));
			}
		}
	}
	private static void AssertMethod(JsonNode method, string name, bool isStatic, string parameterName, string returnType)
	{
		Assert.Equal(name, Text(method, "name"));
		Assert.Equal(isStatic, method["isStatic"]!.GetValue<bool>());
		Assert.Equal("System.Int32", CanonicalType(Text(method, "signature")));
		Assert.Equal(returnType, CanonicalType(Text(method, "returnType")));
		Assert.Equal(parameterName, Assert.Single(method["parameterNames"]!.AsArray())!.GetValue<string>());
	}
	private static string CanonicalType(string value) => value.Trim() switch
	{
		"int" or "int32" or "System.Int32" => "System.Int32",
		"long" or "int64" or "System.Int64" => "System.Int64",
		"double" or "System.Double" => "System.Double",
		_ => throw new InvalidDataException($"Unexpected fixed Mono fixture type '{value}'.")
	};
	private static HashSet<string> JobIds(JsonNode list) => [.. list["jobs"]!.AsArray().Select(job => Text(job!, "jobId"))];
	private static ulong ParseHex(string value) => ulong.Parse(Normalize(value), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture);

	private static async Task AssertMemoryAsync(LiveMcpInstanceClient instance, string address, string valueType, string expected)
	{
		JsonNode read = await CallAsync(instance, CheatEngineToolNames.MemoryRead, new Dictionary<string, object?> { ["address"] = address, ["valueType"] = valueType });
		Assert.Equal(expected, Text(read, "value"));
	}
	private static async Task CleanupAsync(List<Exception> failures, Func<Task> action)
	{
		try
		{
			await action();
		}
		catch (Exception exception) { failures.Add(exception); }
	}
	private static JsonNode Field(JsonArray fields, string name) => fields.Single(field => Text(field!, "name") == name)!;
	private static void AssertField(JsonArray fields, string name, string expected) => Assert.Equal(expected, Text(Field(fields, name), "value"));
	private static string Text(JsonNode node, string property) => node[property]?.GetValue<string>() ?? throw new InvalidDataException($"Missing string {property}.");
	private static string Normalize(string value) => value.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? value[2..] : value;
	private static string Add(string address, long offset) => (ulong.Parse(Normalize(address), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture) + checked((ulong) offset)).ToString("X", CultureInfo.InvariantCulture);
	private static void Receipt(LiveSandboxSession sandbox, string tool, JsonNode result) => sandbox.Record("all_tools_" + tool, result.DeepClone());
	private static async Task<JsonNode> CallAsync(LiveMcpInstanceClient instance, string tool, IReadOnlyDictionary<string, object?>? arguments = null) => await instance.CallToolAsync(tool, arguments) ?? throw new InvalidDataException($"Tool '{tool}' returned no payload.");
}
