using System.Diagnostics;
using System.Globalization;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

using CheatEngine.Mcp.Core.Contract;

namespace CheatEngine.Mcp.Tests.LiveQualification;

/// <summary>Runs only the fixed, separately authorized compiler phases; it never loads the generated assembly.</summary>
[SupportedOSPlatform("windows")]
internal static class LiveCompilerQualification
{
	private static readonly TimeSpan QuietInterval = TimeSpan.FromSeconds(2);
	private const long MaximumArtifactBytes = 16 * 1024 * 1024;

	internal static async Task RunAsync(LiveQualificationInputs inputs)
	{
		Assert.Equal(LiveQualificationScenario.Compiler, inputs.Scenario);
		object candidate = LiveCompilerCandidate.Verify(inputs);
		await VerifyDisabledGateAsync(inputs, candidate);
		// The first session has fully restored user state before the enabled activation starts.
		Assert.Equal(JsonSerializer.Serialize(candidate), JsonSerializer.Serialize(LiveCompilerCandidate.Verify(inputs)));
		await VerifyCompilerAsync(inputs, candidate);
	}

	private static async Task VerifyDisabledGateAsync(LiveQualificationInputs inputs, object candidate)
	{
		await using LiveSandboxSession sandbox = await LiveSandboxSession.StartAsync(inputs, new LiveSandboxOptions(true, false));
		RecordAdmission(sandbox, candidate, "disabled");
		await using LiveMcpClient gateway = await ConnectAsync(sandbox);
		LiveMcpInstanceClient instance = gateway.Bind(sandbox.HostA.InstanceId);
		await VerifyGatesAsync(sandbox, instance, "A", false);
		await VerifyGatesAsync(sandbox, gateway.Bind(sandbox.HostB.InstanceId), "B", false);
		long receipts = await ReceiptCountAsync(sandbox, "A");
		FileFact[] before = Snapshot(sandbox, sandbox.CompilerA.Temp);
		string output = Path.Combine(sandbox.CompilerA.Output, "gate-disabled.dll");
		LiveMcpToolResult result = await CompileAsync(sandbox, instance, "gate_disabled", Arguments(LiveCompilerPayload.ValidSource, output));
		AssertError(result, "capability_disabled", "not_started");
		Assert.Equal(receipts, await ReceiptCountAsync(sandbox, "A"));
		Assert.Equal(before, Snapshot(sandbox, sandbox.CompilerA.Temp));
		Assert.Empty(Snapshot(sandbox, sandbox.CompilerA.Output));
		await sandbox.StopHostAsync("A");
		await RequireInstancesAsync(gateway, sandbox.HostB.InstanceId);
		await RejectStaleInstanceAsync(instance);
		sandbox.Record("compiler_gate_refusal", new
		{
			noCompilerReceipt = true,
			noOutput = true,
			tempUnchanged = true
		});
		sandbox.MarkPassed();
	}

	private static async Task VerifyCompilerAsync(LiveQualificationInputs inputs, object candidate)
	{
		await using LiveSandboxSession sandbox = await LiveSandboxSession.StartAsync(inputs, new LiveSandboxOptions(true, true));
		RecordAdmission(sandbox, candidate, "enabled");
		await using LiveMcpClient gateway = await ConnectAsync(sandbox);
		LiveMcpInstanceClient instanceA = gateway.Bind(sandbox.HostA.InstanceId);
		LiveMcpInstanceClient instanceB = gateway.Bind(sandbox.HostB.InstanceId);
		await VerifyGatesAsync(sandbox, instanceA, "A", true);
		await VerifyGatesAsync(sandbox, instanceB, "B", true);
		await AttachAsync(instanceA, sandbox.HostA);
		await AttachAsync(instanceB, sandbox.HostB);
		JsonNode status = await sandbox.CompilerProbeAsync("A", "status");
		Assert.True(status["available"]!.GetValue<bool>(), "The installed CE host does not expose compileCS; qualification stops without installing prerequisites.");
		sandbox.Record("compiler_availability", status.DeepClone());
		FileFact[] baselineA = Snapshot(sandbox, sandbox.CompilerA.Temp);
		FileFact[] baselineB = SnapshotCompiler(sandbox, sandbox.CompilerB);
		sandbox.Record("compiler_baseline", new
		{
			A = baselineA,
			B = baselineB
		});
		long receipts = await ReceiptCountAsync(sandbox, "A");
		string output = Path.Combine(sandbox.CompilerA.Output, "probe.dll");
		LiveMcpToolResult compiled = await CompileAsync(sandbox, instanceA, "valid_source", Arguments(LiveCompilerPayload.ValidSource, output));
		Assert.False(compiled.IsError, compiled.ErrorText);
		JsonNode payload = Assert.IsAssignableFrom<JsonNode>(compiled.Payload);
		Assert.Equal(output, payload["outputPath"]!.GetValue<string>(), true);
		FileFact exported = ReadFile(sandbox, output, sandbox.CompilerA.Output, true);
		Assert.Equal(exported.Length, payload["length"]!.GetValue<long>());
		Assert.Equal(exported.Sha256, payload["sha256"]!.GetValue<string>());
		Assert.Equal(receipts + 1, await ReceiptCountAsync(sandbox, "A"));
		AssertOnlyExport(sandbox, exported);
		await VerifySelectedTargetAsync(instanceB, sandbox.HostB);
		Assert.Equal(baselineB, SnapshotCompiler(sandbox, sandbox.CompilerB));
		sandbox.Record("compiler_export", new
		{
			file = exported,
			observedAt = DateTimeOffset.UtcNow
		});

		await VerifyInvalidAsync(sandbox, instanceA, "invalid_source", LiveCompilerPayload.InvalidSource, null, null, exported);
		string missing = Path.Combine(sandbox.CompilerA.References, "missing.dll");
		string invalid = Path.Combine(sandbox.CompilerA.References, "invalid.dll");
		RequireOwnedPath(sandbox, invalid, sandbox.CompilerA.References);
		await File.WriteAllTextAsync(invalid, LiveCompilerPayload.InvalidReference, new UTF8Encoding(false), TestContext.Current.CancellationToken);
		Assert.Equal(LiveCompilerPayload.InvalidReferenceSha256, ReadFile(sandbox, invalid, sandbox.CompilerA.References).Sha256);
		await VerifyMissingAsync(sandbox, instanceA, "missing_reference", missing, false, exported);
		await VerifyInvalidAsync(sandbox, instanceA, "invalid_reference", LiveCompilerPayload.ValidSource, [invalid], null, exported);
		// CE 7.7's reviewed compileCS signature supports its optional third coreAssembly argument.
		await VerifyMissingAsync(sandbox, instanceA, "missing_core", missing, true, exported);
		await VerifyInvalidAsync(sandbox, instanceA, "invalid_core", LiveCompilerPayload.ValidSource, null, invalid, exported);
		Assert.Equal(baselineB, SnapshotCompiler(sandbox, sandbox.CompilerB));
		await VerifySelectedTargetAsync(instanceA, sandbox.HostA);
		await VerifySelectedTargetAsync(instanceB, sandbox.HostB);

		receipts = await ReceiptCountAsync(sandbox, "A");
		FileFact[] beforeRaw = Snapshot(sandbox, sandbox.CompilerA.Temp);
		JsonNode rawResult = await sandbox.CompilerProbeAsync("A", "compile");
		Assert.Equal(receipts + 1, rawResult["receiptCount"]!.GetValue<long>());
		string rawPath = rawResult["assemblyPath"]?.GetValue<string>()
			?? throw new InvalidOperationException("The fixed raw compiler bridge returned no assembly; qualification stops.");
		// Containment and every existing path component are checked before any read of the CE-returned path.
		FileFact raw = ReadFile(sandbox, rawPath, sandbox.CompilerA.Temp, true);
		Assert.DoesNotContain(beforeRaw, file => file.Path == raw.Path);
		Assert.Equal(exported, ReadFile(sandbox, output, sandbox.CompilerA.Output, true));
		sandbox.Record("compiler_raw_before_shutdown", new
		{
			file = raw,
			exported,
			observedAt = DateTimeOffset.UtcNow
		});
		await sandbox.StopHostAsync("B");
		await RequireInstancesAsync(gateway, sandbox.HostA.InstanceId);
		await RejectStaleInstanceAsync(instanceB);
		await Task.Delay(QuietInterval, TestContext.Current.CancellationToken);
		FileFact? afterB = ObserveRaw(sandbox, rawPath);
		if (afterB is not null)
		{
			Assert.Equal(raw, afterB);
		}
		Assert.Equal(exported, ReadFile(sandbox, output, sandbox.CompilerA.Output, true));
		await VerifySelectedTargetAsync(instanceA, sandbox.HostA);
		sandbox.Record("compiler_after_b", new
		{
			raw = afterB,
			exported,
			observedAt = DateTimeOffset.UtcNow
		});
		await sandbox.StopHostAsync("A");
		await RequireInstancesAsync(gateway);
		await RejectStaleInstanceAsync(instanceA);
		await Task.Delay(QuietInterval, TestContext.Current.CancellationToken);
		FileFact? afterA = ObserveRaw(sandbox, rawPath);
		if (afterA is not null)
		{
			Assert.Equal(raw, afterA);
		}
		Assert.Equal(exported, ReadFile(sandbox, output, sandbox.CompilerA.Output, true));
		sandbox.Record("compiler_after_a", new
		{
			raw = afterA,
			exported,
			observedAt = DateTimeOffset.UtcNow,
			classification = afterB is null ? "expired_after_b" : afterA is null ? "retained_after_b_expired_after_a" : "retained_after_a",
			distinctTemporaryRoots = true,
			injectionAttempted = false
		});
		sandbox.MarkPassed();
	}

	private static void RecordAdmission(LiveSandboxSession sandbox, object candidate, string phase)
	{
		sandbox.Record("compiler_admission", new
		{
			candidate,
			phase,
			liveAcknowledgement = true,
			compilerAcknowledgement = true,
			targetArchitecture = sandbox.TargetArchitecture,
			injectionApproved = false
		});
	}

	private static Task<LiveMcpClient> ConnectAsync(LiveSandboxSession sandbox) =>
		LiveMcpClient.ConnectGatewayAsync(sandbox.GatewayExecutablePath, sandbox.InstanceDirectory, TestContext.Current.CancellationToken);

	private static async Task VerifyGatesAsync(LiveSandboxSession sandbox, LiveMcpInstanceClient client, string name, bool enabled)
	{
		JsonNode runtime = (await client.CallToolAsync(CheatEngineToolNames.RuntimeGetInfo))!;
		JsonNode gates = runtime["gates"]!;
		Assert.Equal(enabled, gates["targetCodeExecution"]!.GetValue<bool>());
		Assert.False(gates["unsafeLua"]!.GetValue<bool>());
		Assert.False(gates["autoAssembler"]!.GetValue<bool>());
		Assert.False(gates["kernelAccess"]!.GetValue<bool>());
		sandbox.Record($"compiler_runtime_{name}", runtime.DeepClone());
	}

	private static async Task AttachAsync(LiveMcpInstanceClient client, LiveSandboxHost host)
	{
		JsonNode opened = (await client.CallToolAsync(CheatEngineToolNames.ProcessAttach,
			new Dictionary<string, object?> { ["process"] = host.TargetProcessId.ToString(CultureInfo.InvariantCulture) }))!;
		Assert.Equal(host.TargetProcessId, opened["processId"]!.GetValue<int>());
		await VerifySelectedTargetAsync(client, host);
	}

	private static async Task VerifySelectedTargetAsync(LiveMcpInstanceClient client, LiveSandboxHost host)
	{
		JsonNode overview = (await client.CallToolAsync(CheatEngineToolNames.RuntimeGetOverview))!;
		Assert.Equal(host.TargetProcessId, overview["process"]!["processId"]!.GetValue<int>());
		JsonNode memory = (await client.CallToolAsync(CheatEngineToolNames.MemoryRead,
			new Dictionary<string, object?> { ["address"] = host.TargetAddress, ["valueType"] = "int32" }))!;
		Assert.Equal("20260926", memory["value"]!.GetValue<string>());
	}

	private static async Task<long> ReceiptCountAsync(LiveSandboxSession sandbox, string name) =>
		(await sandbox.CompilerProbeAsync(name, "status"))["receiptCount"]!.GetValue<long>();

	private static Dictionary<string, object?> Arguments(string source, string output, string[]? references = null, string? core = null) =>
		new()
		{
			["source"] = source,
			["outputPath"] = output,
			["overwrite"] = false,
			["referencePaths"] = references,
			["coreAssembly"] = core
		};

	private static async Task<LiveMcpToolResult> CompileAsync(LiveSandboxSession sandbox, LiveMcpInstanceClient client,
		string name, Dictionary<string, object?> arguments)
	{
		DateTimeOffset started = DateTimeOffset.UtcNow;
		using Process host = Process.GetProcessById(sandbox.HostA.ProcessId);
		long privateBytes = host.PrivateMemorySize64;
		int handles = host.HandleCount;
		LiveMcpToolResult result = await client.CallToolRawAsync(CheatEngineToolNames.ExecCompileCSharp, arguments);
		host.Refresh();
		JsonNode? error = result.IsError ? result.Payload?["error"] : null;
		string? diagnostic = error?["details"]?["text"]?.GetValue<string>();
		sandbox.Record($"compiler_call_{name}", new
		{
			instanceId = client.InstanceId,
			operation = CheatEngineToolNames.ExecCompileCSharp,
			started,
			ended = DateTimeOffset.UtcNow,
			durationMilliseconds = (DateTimeOffset.UtcNow - started).TotalMilliseconds,
			result.IsError,
			kind = error?["kind"]?.GetValue<string>(),
			hostEffect = error?["hostEffect"]?.GetValue<string>(),
			retryable = error?["retryable"]?.GetValue<bool>(),
			correlationId = error?["details"]?["errorId"]?.GetValue<string>(),
			diagnosticBytes = diagnostic is null ? 0 : Encoding.UTF8.GetByteCount(diagnostic),
			diagnosticSha256 = diagnostic is null ? null : LiveCompilerPayload.Sha256(diagnostic),
			diagnosticTruncated = error?["details"]?["truncated"]?.GetValue<bool>(),
			privateBytesDelta = host.PrivateMemorySize64 - privateBytes,
			handleDelta = host.HandleCount - handles
		});
		return result;
	}

	private static JsonNode AssertError(LiveMcpToolResult result, string kind, string effect)
	{
		Assert.True(result.IsError);
		JsonNode error = result.Payload!["error"]!;
		Assert.Equal(kind, error["kind"]!.GetValue<string>());
		Assert.Equal(effect, error["hostEffect"]!.GetValue<string>());
		Assert.False(error["retryable"]!.GetValue<bool>());
		return error;
	}

	private static async Task VerifyMissingAsync(LiveSandboxSession sandbox, LiveMcpInstanceClient instance, string name,
		string missing, bool core, FileFact exported)
	{
		long receipts = await ReceiptCountAsync(sandbox, "A");
		FileFact[] before = Snapshot(sandbox, sandbox.CompilerA.Temp);
		LiveMcpToolResult result = await CompileAsync(sandbox, instance, name,
			Arguments(LiveCompilerPayload.ValidSource, Path.Combine(sandbox.CompilerA.Output, name + ".dll"), core ? null : [missing], core ? missing : null));
		AssertError(result, "not_found", "not_started");
		Assert.Equal(receipts, await ReceiptCountAsync(sandbox, "A"));
		Assert.Equal(before, Snapshot(sandbox, sandbox.CompilerA.Temp));
		AssertOnlyExport(sandbox, exported);
	}

	private static async Task VerifyInvalidAsync(LiveSandboxSession sandbox, LiveMcpInstanceClient instance, string name,
		string source, string[]? references, string? core, FileFact exported)
	{
		long receipts = await ReceiptCountAsync(sandbox, "A");
		LiveMcpToolResult result = await CompileAsync(sandbox, instance, name,
			Arguments(source, Path.Combine(sandbox.CompilerA.Output, name + ".dll"), references, core));
		JsonNode error = AssertError(result, "host_refused", "unknown");
		string diagnostic = error["details"]!["text"]!.GetValue<string>();
		Assert.InRange(Encoding.UTF8.GetByteCount(diagnostic), 1, 16_384);
		// These three tiny fixed failures must fit the bound without truncation.
		Assert.False(error["details"]!["truncated"]!.GetValue<bool>());
		Assert.Equal(receipts + 1, await ReceiptCountAsync(sandbox, "A"));
		AssertOnlyExport(sandbox, exported);
		sandbox.Record($"compiler_temp_after_{name}", Snapshot(sandbox, sandbox.CompilerA.Temp));
	}

	private static void AssertOnlyExport(LiveSandboxSession sandbox, FileFact exported) =>
		Assert.Equal(exported, Assert.Single(Snapshot(sandbox, sandbox.CompilerA.Output)));

	private static async Task RequireInstancesAsync(LiveMcpClient gateway, params string[] expected)
	{
		DateTimeOffset deadline = DateTimeOffset.UtcNow.AddSeconds(10);
		do
		{
			JsonNode response = (await gateway.CallToolAsync(CheatEngineToolNames.InstanceList))!;
			string[] actual = response["instances"]!.AsArray().Select(instance => instance!["instanceId"]!.GetValue<string>()).Order(StringComparer.Ordinal).ToArray();
			if (actual.SequenceEqual(expected.Order(StringComparer.Ordinal)))
			{
				return;
			}
			await Task.Delay(100, TestContext.Current.CancellationToken);
		} while (DateTimeOffset.UtcNow < deadline);
		Assert.Fail("The gateway did not report exactly the remaining private instances.");
	}

	private static async Task RejectStaleInstanceAsync(LiveMcpInstanceClient instance)
	{
		LiveMcpToolResult result = await instance.CallToolRawAsync(CheatEngineToolNames.RuntimeGetInfo);
		AssertError(result, "instance_unavailable", "not_started");
	}

	private static FileFact? ObserveRaw(LiveSandboxSession sandbox, string path)
	{
		RequireOwnedPath(sandbox, path, sandbox.CompilerA.Temp);
		return File.Exists(path) ? ReadFile(sandbox, path, sandbox.CompilerA.Temp, true) : null;
	}

	private static FileFact[] SnapshotCompiler(LiveSandboxSession sandbox, LiveCompilerRoots roots) =>
		[.. Snapshot(sandbox, roots.Temp), .. Snapshot(sandbox, roots.References), .. Snapshot(sandbox, roots.Output)];

	private static FileFact[] Snapshot(LiveSandboxSession sandbox, string root)
	{
		List<FileFact> files = [];
		Visit(root);
		return files.OrderBy(file => file.Path, StringComparer.Ordinal).ToArray();
		void Visit(string directory)
		{
			RequireOwnedPath(sandbox, directory, root);
			foreach (string entry in Directory.EnumerateFileSystemEntries(directory))
			{
				RequireOwnedPath(sandbox, entry, root);
				if (Directory.Exists(entry))
				{
					Visit(entry);
				}
				else
				{
					Assert.True(files.Count < 256, "The compiler scratch tree exceeds the bounded file inventory.");
					files.Add(ReadFile(sandbox, entry, root));
				}
			}
		}
	}

	private static FileFact ReadFile(LiveSandboxSession sandbox, string path, string root, bool assembly = false)
	{
		RequireOwnedPath(sandbox, path, root);
		using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read);
		// Recheck after acquiring the non-delete-shared handle and before reading any file data.
		RequireOwnedPath(sandbox, path, root);
		Assert.InRange(stream.Length, assembly ? 2 : 0, MaximumArtifactBytes);
		if (assembly)
		{
			Assert.Equal('M', stream.ReadByte());
			Assert.Equal('Z', stream.ReadByte());
			stream.Position = 0;
		}
		return new FileFact(Path.GetRelativePath(sandbox.RunDirectory, path), stream.Length, Convert.ToHexString(SHA256.HashData(stream)));
	}

	private static void RequireOwnedPath(LiveSandboxSession sandbox, string path, string root)
	{
		Assert.True(Path.IsPathFullyQualified(path));
		Assert.True(LiveQualificationOptIn.IsSameOrBelow(root, sandbox.RunDirectory));
		Assert.True(LiveQualificationOptIn.IsSameOrBelow(path, root), "The compiler returned a path outside its owned root; it will not be read or modified.");
		for (string? component = Path.GetFullPath(path); component is not null; component = Path.GetDirectoryName(component))
		{
			if (File.Exists(component) || Directory.Exists(component))
			{
				Assert.False((File.GetAttributes(component) & FileAttributes.ReparsePoint) != 0, "Compiler paths must not contain reparse points.");
			}
		}
	}

	private sealed record FileFact(string Path, long Length, string Sha256);
}
