using System.Globalization;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text.Json.Nodes;

using CheatEngine.Mcp.Core.Contract;

namespace CheatEngine.Mcp.Tests.LiveQualification;

/// <summary>Reversible qualification for opening and saving a private file-as-process target.</summary>
[SupportedOSPlatform("windows")]
internal static class LiveProcessFileQualification
{
	internal const int InputLength = 4096;
	private const string FileBaseAddress = "0x400000";
	private const long FileTargetSentinel = 4294967295L;

	internal static async Task VerifyAsync(LiveSandboxSession sandbox, LiveMcpInstanceClient instance)
	{
		ArgumentNullException.ThrowIfNull(sandbox);
		ArgumentNullException.ThrowIfNull(instance);
		string root = Path.Combine(sandbox.RunDirectory, "roadmap-qualification", "process-files");
		string input = OwnedPath(root, "opened-file.bin");
		string output = OwnedPath(root, "saved-file.bin");
		Directory.CreateDirectory(root);
		Assert.False(File.Exists(input));
		Assert.False(File.Exists(output));
		int targetProcessId = sandbox.HostA.TargetProcessId;
		bool targetChangeAttempted = false;
		bool targetRestored = false;
		bool ownsInput = false;
		bool ownsOutput = false;
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
		}
		catch (Exception exception) { failure = exception; throw; }
		finally
		{
			using CancellationTokenSource cleanupTimeout = new(TimeSpan.FromSeconds(10));
			LiveMcpInstanceClient cleanupInstance = instance.WithCancellation(cleanupTimeout.Token);
			if (targetChangeAttempted)
			{
				await CleanupAsync(cleanup, async () =>
				{
					JsonNode attached = await CallAsync(cleanupInstance, CheatEngineToolNames.ProcessAttach,
						new Dictionary<string, object?>
						{
							["process"] = targetProcessId.ToString(CultureInfo.InvariantCulture)
						});
					Assert.Equal(targetProcessId, attached["processId"]!.GetValue<int>());
					JsonNode restored = await CallAsync(cleanupInstance, CheatEngineToolNames.ProcessGetCurrent);
					sandbox.Record("process_file_reattachment_observed", new
					{
						attached = attached.DeepClone(),
						current = restored.DeepClone()
					});
					Assert.True(restored["isOpen"]!.GetValue<bool>());
					Assert.Equal(targetProcessId, restored["processId"]!.GetValue<int>());
					LiveMcpToolResult sentinel = await cleanupInstance.CallToolRawAsync(CheatEngineToolNames.MemoryRead,
						new Dictionary<string, object?> { ["address"] = sandbox.HostA.TargetAddress, ["valueType"] = "int32" });
					sandbox.Record("process_file_restored_memory_observed", new
					{
						address = sandbox.HostA.TargetAddress,
						sentinel.IsError,
						payload = sentinel.Payload?.DeepClone()
					});
					Assert.False(sentinel.IsError, $"The original target memory was not restored: {sentinel.Payload?.ToJsonString()}");
					Assert.Equal("20260926", sentinel.Payload!["value"]!.GetValue<string>());
					targetRestored = true;
				});
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
				(!ownsOutput || !File.Exists(output)))
			{
				sandbox.Record("live_process_file_cleanup", new
				{
					targetChangeAttempted,
					targetRestored,
					inputDeleted = ownsInput,
					outputDeleted = ownsOutput
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
