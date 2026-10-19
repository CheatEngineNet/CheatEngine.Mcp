using System.Diagnostics;
using System.Globalization;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text.Json.Nodes;

using CheatEngine.Mcp.Core.Contract;

namespace CheatEngine.Mcp.Tests.LiveQualification;

/// <summary>Qualifies owned file targets, pre-mutation refusal and recovery through a private host restart.</summary>
[SupportedOSPlatform("windows")]
internal static class LiveProcessFileQualification
{
	internal const int InputLength = 4096;
	private const string FileBaseAddress = "0x400000";
	private const long FileTargetSentinel = 4294967295L;

	internal static async Task VerifyAsync(LiveSandboxSession sandbox, LiveLifecycleSession connection)
	{
		ArgumentNullException.ThrowIfNull(sandbox);
		ArgumentNullException.ThrowIfNull(connection);
		LiveSandboxHost beforeHost = sandbox.HostA;
		LiveSandboxHost unaffected = sandbox.HostB;
		LiveMcpInstanceClient instance = connection.Gateway.Bind(beforeHost.InstanceId);
		using Process originalHost = Process.GetProcessById(beforeHost.ProcessId);
		using Process originalTarget = Process.GetProcessById(beforeHost.TargetProcessId);
		_ = originalHost.Handle;
		_ = originalTarget.Handle;
		string root = Path.Combine(sandbox.RunDirectory, "roadmap-qualification", "process-files");
		string input = OwnedPath(root, "opened-file.bin");
		string output = OwnedPath(root, "saved-file.bin");
		string proofOutput = OwnedPath(root, "saved-after-refusal.bin");
		Directory.CreateDirectory(root);
		Assert.False(File.Exists(input));
		Assert.False(File.Exists(output));
		Assert.False(File.Exists(proofOutput));
		int targetProcessId = sandbox.HostA.TargetProcessId;
		bool targetChangeAttempted = false;
		bool targetRestored = false;
		bool ownsInput = false;
		bool ownsOutput = false;
		bool ownsProofOutput = false;
		Exception? failure = null;
		List<Exception> cleanup = [];
		try
		{
			await VerifyGatesAsync(instance);
			JsonNode original = await CallAsync(instance, CheatEngineToolNames.ProcessGetCurrent);
			Assert.True(original["isOpen"]!.GetValue<bool>());
			Assert.Equal(targetProcessId, original["processId"]!.GetValue<int>());
			JsonNode originalValue = await CallAsync(instance, CheatEngineToolNames.MemoryRead,
				new Dictionary<string, object?> { ["address"] = sandbox.HostA.TargetAddress, ["valueType"] = "int32" });
			Assert.Equal("20260926", originalValue["value"]!.GetValue<string>());
			sandbox.Record("process_file_original_target_observed", new
			{
				current = original.DeepClone(),
				address = sandbox.HostA.TargetAddress,
				memory = originalValue.DeepClone()
			});

			byte[] expectedBytes = CreateInputBytes();
			await using (FileStream inputFile = new(input, FileMode.CreateNew, FileAccess.Write, FileShare.None))
			{
				ownsInput = true;
				await inputFile.WriteAsync(expectedBytes);
			}
			FileContent expected = await DescribeFileAsync(input);
			Assert.Equal(expectedBytes, expected.Bytes);
			Assert.Equal(Path.GetFullPath(input), expected.Fact.Path, ignoreCase: true);

			bool is64Bit = sandbox.TargetArchitecture == "x64";
			targetChangeAttempted = true;
			JsonNode opened = await CallAsync(instance, CheatEngineToolNames.ProcessOpenFile, new Dictionary<string, object?>
			{
				["filename"] = input,
				["is64Bit"] = is64Bit,
				["startAddress"] = FileBaseAddress
			});
			Assert.Equal(Path.GetFileName(input), Text(opened, "inputFileName"));
			Assert.Equal(is64Bit, opened["requested64Bit"]!.GetValue<bool>());
			Assert.Equal(expected.Fact.Length, opened["observedFileSize"]!.GetValue<long>());
			Assert.Equal(FileTargetSentinel, opened["observedProcessId"]!.GetValue<long>());
			Assert.False(opened["monoAutoAttach"]!.GetValue<bool>());

			ownsOutput = true;
			JsonNode saved = await CallAsync(instance, CheatEngineToolNames.ProcessSaveFile,
				new Dictionary<string, object?> { ["filename"] = output });
			Assert.True(saved["saved"]!.GetValue<bool>());
			Assert.Equal(Path.GetFileName(output), Text(saved, "fileName"));
			FileContent preserved = await DescribeFileAsync(input);
			FileContent actual = await DescribeFileAsync(output);
			Assert.Equal(expected.Bytes, preserved.Bytes);
			Assert.Equal(expected.Fact.Length, preserved.Fact.Length);
			Assert.Equal(expected.Fact.Sha256, preserved.Fact.Sha256);
			Assert.Equal(expected.Bytes, actual.Bytes);
			Assert.Equal(expected.Fact.Length, actual.Fact.Length);
			Assert.Equal(expected.Fact.Sha256, actual.Fact.Sha256);
			Assert.Equal(Path.GetFullPath(output), actual.Fact.Path, ignoreCase: true);
			Receipt(sandbox, CheatEngineToolNames.ProcessOpenFile, new JsonObject
			{
				["result"] = opened.DeepClone(),
				["input"] = expected.Fact.ToJson()
			});
			Receipt(sandbox, CheatEngineToolNames.ProcessSaveFile, new JsonObject
			{
				["result"] = saved.DeepClone(),
				["input"] = expected.Fact.ToJson(),
				["inputAfterSave"] = preserved.Fact.ToJson(),
				["output"] = actual.Fact.ToJson(),
				["bytesMatch"] = true
			});

			LiveMcpToolResult refused = await instance.CallToolRawAsync(CheatEngineToolNames.ProcessAttach,
				new Dictionary<string, object?> { ["process"] = targetProcessId.ToString(CultureInfo.InvariantCulture) });
			sandbox.Record("process_file_transition_guard_observed", new
			{
				result = refused.Payload?.DeepClone()
			});
			Assert.True(refused.IsError);
			Assert.Equal("unsupported", Text(refused.Payload!["error"]!, "kind"));
			Assert.Equal("not_started", Text(refused.Payload["error"]!, "hostEffect"));
			ownsProofOutput = true;
			JsonNode savedAfterRefusal = await CallAsync(instance, CheatEngineToolNames.ProcessSaveFile,
				new Dictionary<string, object?> { ["filename"] = proofOutput });
			Assert.True(savedAfterRefusal["saved"]!.GetValue<bool>());
			FileContent proof = await DescribeFileAsync(proofOutput);
			Assert.Equal(expected.Bytes, proof.Bytes);
			Assert.Equal(expected.Fact.Sha256, proof.Fact.Sha256);
			sandbox.Record("process_file_refused_transition_preserved_file", new
			{
				refusal = refused.Payload.DeepClone(),
				proof = proof.Fact.ToJson()
			});
		}
		catch (Exception exception) { failure = exception; throw; }
		finally
		{
			if (targetChangeAttempted)
			{
				await CleanupAsync(cleanup, async () =>
				{
					await sandbox.RestartHostAsync("A");
					LiveLifecycleQualification.RequireHostRestart(beforeHost, sandbox.HostA, unaffected);
					Assert.True(originalHost.HasExited);
					Assert.True(originalTarget.HasExited);
					Assert.NotEqual(beforeHost.InstanceId, sandbox.HostA.InstanceId);
					Assert.Equal(unaffected, sandbox.HostB);
					using CancellationTokenSource recoveryDeadline = new(TimeSpan.FromSeconds(30));
					LiveMcpClient recoveryGateway = await LiveMcpClient.ConnectGatewayAsync(sandbox.GatewayExecutablePath,
						sandbox.InstanceDirectory, recoveryDeadline.Token);
					await using LiveLifecycleSession recovery = new(sandbox, recoveryGateway, recoveryDeadline.Token);
					await recovery.RequireInstancesAsync(sandbox.HostA.InstanceId, sandbox.HostB.InstanceId);
					await recovery.RejectStaleAsync(beforeHost.InstanceId);
					await recovery.AttachBothAsync();
					await recovery.VerifyBothAsync();
					sandbox.Record("process_file_recovery_after_restart", new
					{
						before = beforeHost,
						after = sandbox.HostA,
						originalHostExited = true,
						originalTargetExited = true,
						replacementTargetKnownValueVerified = true,
						unaffectedHostVerified = true
					});
					targetRestored = true;
				});
			}
			if (ownsProofOutput)
			{
				await CleanupAsync(cleanup, () => DeleteOwnedFileAsync(proofOutput));
			}
			if (ownsOutput)
			{
				await CleanupAsync(cleanup, () => DeleteOwnedFileAsync(output));
			}
			if (ownsInput)
			{
				await CleanupAsync(cleanup, () => DeleteOwnedFileAsync(input));
			}
			if (cleanup.Count == 0 && (!targetChangeAttempted || targetRestored) && (!ownsInput || !File.Exists(input)) &&
				(!ownsOutput || !File.Exists(output)) && (!ownsProofOutput || !File.Exists(proofOutput)))
			{
				sandbox.Record("live_process_file_cleanup", new
				{
					targetChangeAttempted,
					targetRestored,
					targetRecovery = "host_and_target_restart",
					inputDeleted = ownsInput,
					outputDeleted = ownsOutput,
					proofOutputDeleted = ownsProofOutput
				});
			}
			LiveLifecycleQualification.ThrowWithCleanup(failure, cleanup);
		}
	}

	internal static byte[] CreateInputBytes()
	{
		byte[] bytes = new byte[InputLength];
		for (int index = 0; index < bytes.Length; index++)
		{
			bytes[index] = (byte) (((index * 37) + 11) & byte.MaxValue);
		}

		return bytes;
	}

	internal static async Task<FileContent> DescribeFileAsync(string path)
	{
		string fullPath = Path.GetFullPath(path);
		await using FileStream stream = new(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read);
		if (stream.Length > InputLength)
		{
			throw new InvalidDataException($"The process-file qualification output exceeds {InputLength} bytes.");
		}

		byte[] bytes = new byte[checked((int) stream.Length)];
		int offset = 0;
		while (offset < bytes.Length)
		{
			int read = await stream.ReadAsync(bytes.AsMemory(offset));
			if (read == 0)
			{
				throw new EndOfStreamException("The process-file qualification output changed while it was read.");
			}

			offset += read;
		}

		return new FileContent(bytes, new FileFact(fullPath, bytes.LongLength,
			Convert.ToHexString(SHA256.HashData(bytes))));
	}

	private static async Task VerifyGatesAsync(LiveMcpInstanceClient instance)
	{
		JsonNode runtime = await CallAsync(instance, CheatEngineToolNames.RuntimeGetInfo);
		JsonNode gates = runtime["gates"] ?? throw new InvalidDataException("runtime_get_info omitted gates.");
		Assert.False(gates["unsafeLua"]!.GetValue<bool>());
		Assert.False(gates["autoAssembler"]!.GetValue<bool>());
		Assert.False(gates["targetCodeExecution"]!.GetValue<bool>());
		Assert.False(gates["kernelAccess"]!.GetValue<bool>());
	}

	private static string OwnedPath(string root, string filename)
	{
		string path = Path.GetFullPath(Path.Combine(root, filename));
		if (!LiveQualificationOptIn.IsSameOrBelow(path, root))
		{
			throw new InvalidOperationException("The process-file qualification path escaped its owned root.");
		}

		return path;
	}

	private static Task DeleteOwnedFileAsync(string path)
	{
		File.Delete(path);
		Assert.False(File.Exists(path));
		return Task.CompletedTask;
	}

	private static async Task CleanupAsync(List<Exception> cleanup, Func<Task> action)
	{
		try
		{
			await action();
		}
		catch (Exception exception)
		{
			cleanup.Add(exception);
		}
	}

	private static void Receipt(LiveSandboxSession sandbox, string tool, JsonNode result) =>
		sandbox.Record("all_tools_" + tool, result.DeepClone());
	private static string Text(JsonNode node, string property) => node[property]!.GetValue<string>();
	private static async Task<JsonNode> CallAsync(LiveMcpInstanceClient instance, string tool,
		IReadOnlyDictionary<string, object?>? arguments = null) => await instance.CallToolAsync(tool, arguments)
		?? throw new InvalidDataException($"Tool '{tool}' returned no payload.");

	internal sealed record FileFact(string Path, long Length, string Sha256)
	{
		internal JsonObject ToJson() => new()
		{
			["path"] = Path,
			["length"] = Length,
			["sha256"] = Sha256
		};
	}

	internal sealed record FileContent(byte[] Bytes, FileFact Fact);
}
