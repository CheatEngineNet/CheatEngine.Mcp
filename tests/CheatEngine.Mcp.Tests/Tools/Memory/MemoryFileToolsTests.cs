using System.Collections.Immutable;
using System.Text.Json;

using CheatEngine.Client.Inspection;
using CheatEngine.Client.Memory;
using CheatEngine.Client.Results;
using CheatEngine.Mcp.Core.Contract;
using CheatEngine.Mcp.Core.Files;
using CheatEngine.Mcp.Tools.Memory;
using CheatEngine.SDK.Engine.Enums;
using CheatEngine.SDK.Engine.Inspection;
using CheatEngine.SDK.Engine.Values;

namespace CheatEngine.Mcp.Tests.Tools.Memory;

/// <summary>Memory dumps: the host-file policy runs before any read, and only a complete dump replaces the file.</summary>
public sealed class MemoryFileToolsTests : IDisposable
{
	private readonly string _root = Path.Combine(Path.GetTempPath(), $"CheatEngine.Mcp.Tests-{Guid.NewGuid():N}");

	public MemoryFileToolsTests()
	{
		Directory.CreateDirectory(Path.Combine(_root, "dumps"));
		Directory.CreateDirectory(Path.Combine(_root, "data"));
		Directory.CreateDirectory(Path.Combine(_root, "registry"));
	}

	private static CancellationToken Token => TestContext.Current.CancellationToken;

	public void Dispose()
	{
		Directory.Delete(_root, true);
	}

	[Theory]
	[InlineData(@"\\server\share\dump.bin")]
	[InlineData(@"dumps\relative.bin")]
	[InlineData("{root}\\dumps\\dump.bin:stream")]
	[InlineData("{root}\\data\\dump.bin")]
	[InlineData("{root}\\elsewhere.bin")]
	public void DumpToFile_RefusedPath_NeverReadsTheTarget(string path)
	{
		TargetDouble target = new();

		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() =>
			Tools(target, true).DumpToFile("1000", 16, path.Replace("{root}", _root, StringComparison.Ordinal),
				cancellationToken: Token));

		Assert.Equal(ToolErrorKind.InvalidArgument, exception.Error.Kind);
		Assert.Equal(ToolHostEffect.NotStarted, exception.Error.HostEffect);
		Assert.Equal(0, target.Dispatcher.Calls);
	}

	[Fact]
	public void DumpToFile_NoWriteRoot_RefusesEveryPath()
	{
		TargetDouble target = new();

		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() =>
			Tools(target, false).DumpToFile("1000", 16, Path.Combine(_root, "dumps", "dump.bin"),
				cancellationToken: Token));

		Assert.Equal(ToolErrorKind.InvalidArgument, exception.Error.Kind);
		Assert.Contains("no write root", exception.Error.Message, StringComparison.Ordinal);
		Assert.Equal(0, target.Dispatcher.Calls);
	}

	[Fact]
	public void DumpToFile_ReadableRange_WritesTheBytesAndLeavesNoTemporaryFile()
	{
		TargetDouble target = new();
		byte[] memory = [.. Enumerable.Range(0, 300).Select(static value => (byte) value)];
		target.Memory = (_, arguments) =>
		{
			MemoryBytesReadRequest request = (MemoryBytesReadRequest) arguments[0]!;
			return new MemoryBytesReadOutcome(request.Length, memory.AsSpan(0, request.Length).ToImmutableArray(),
				null);
		};
		string path = Path.Combine(_root, "dumps", "dump.bin");

		FileDumpResult result = Tools(target, true).DumpToFile("1000", memory.Length, path, cancellationToken: Token);

		Assert.Equal((path, "1000", 300, 0), (result.Path, result.Address, result.BytesWritten,
			result.ZeroFilledBytes));
		Assert.Equal(memory, File.ReadAllBytes(path));
		Assert.Single(Directory.GetFiles(Path.Combine(_root, "dumps")));
	}

	[Fact]
	public void DumpToFile_UnreadablePagesWithZero_ZeroFillsAndListsThem()
	{
		TargetDouble target = new();
		target.Memory = (_, arguments) =>
		{
			MemoryBytesReadRequest request = (MemoryBytesReadRequest) arguments[0]!;
			ulong start = request.Address.ToUInt64();
			// 0x11000-0x12FFF is reserved; the rest reads as 0xAB.
			int readable = start is >= 0x11000 and < 0x13000
				? 0
				: (int) Math.Min((ulong) request.Length, start < 0x11000 ? 0x11000 - start : ulong.MaxValue);
			return readable == request.Length
				? new MemoryBytesReadOutcome(request.Length, Filled(readable), null)
				: new MemoryBytesReadOutcome(request.Length, Filled(readable),
					new CheatEngineFailure(CheatEngineFailureKind.MemoryReadFailed, "Memory.ReadBytes",
						"Partial read.", hostEffect: CheatEngineHostEffect.Completed));
		};
		target.Inspection = (method, arguments) =>
		{
			Assert.Equal(nameof(IInspectionClient.TryGetMemoryRegion), method.Name);
			arguments[1] = new MemoryRegionInfo(new Address(0x11000), new Address(0x11000), MemoryProtection.None,
				new MemorySize(0x2000), MemoryRegionState.Reserved, MemoryProtection.None, MemoryRegionType.Private,
				null);
			arguments[2] = default(CheatEngineFailure);
			return true;
		};
		string path = Path.Combine(_root, "dumps", "sparse.bin");

		FileDumpResult result =
			Tools(target, true).DumpToFile("10000", 0x4000, path, unreadable: UnreadableMemory.Zero,
				cancellationToken: Token);

		Assert.Equal(0x2000, result.ZeroFilledBytes);
		Assert.Equal([new ZeroFilledRange("1000", 0x2000)], result.ZeroFilled);
		byte[] file = File.ReadAllBytes(path);
		Assert.All(file.AsSpan(0x1000, 0x2000).ToArray(), static value => Assert.Equal(0, value));
		Assert.All(file.AsSpan(0x3000, 0x1000).ToArray(), static value => Assert.Equal(0xAB, value));
	}

	[Fact]
	public void DumpToFile_TargetChangedBetweenChunks_LeavesNoFile()
	{
		TargetDouble target = new();
		target.Epochs.Enqueue(1);
		target.Epochs.Enqueue(2);
		target.Memory = (_, arguments) =>
		{
			MemoryBytesReadRequest request = (MemoryBytesReadRequest) arguments[0]!;
			return new MemoryBytesReadOutcome(request.Length, Filled(request.Length), null);
		};
		string path = Path.Combine(_root, "dumps", "changed.bin");

		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() =>
			Tools(target, true).DumpToFile("10000", MemoryTargets.ChunkBytes + 1, path, cancellationToken: Token));

		Assert.Equal(ToolErrorKind.TargetChanged, exception.Error.Kind);
		Assert.Empty(Directory.GetFiles(Path.Combine(_root, "dumps")));
	}

	[Fact]
	public void DumpToFile_ExistingFileWithoutOverwrite_IsRefusedBeforeAnyRead()
	{
		TargetDouble target = new();
		string path = Path.Combine(_root, "dumps", "kept.bin");
		File.WriteAllBytes(path, [1, 2, 3]);

		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() =>
			Tools(target, true).DumpToFile("1000", 16, path, cancellationToken: Token));

		Assert.Equal(ToolErrorKind.InvalidArgument, exception.Error.Kind);
		Assert.Equal([1, 2, 3], File.ReadAllBytes(path));
		Assert.Equal(0, target.Dispatcher.Calls);
	}

	[Theory]
	[InlineData(@"\\server\share\input.bin")]
	[InlineData(@"dumps\relative.bin")]
	[InlineData(@"{root}\data\input.bin")]
	[InlineData(@"{root}\registry\input.bin")]
	public void LoadFromFile_RefusedPath_NeverResolvesOrWrites(string path)
	{
		TargetDouble target = new();
		string candidate = path.Replace("{root}", _root, StringComparison.Ordinal);
		if (Path.IsPathFullyQualified(candidate) && !candidate.StartsWith(@"\\", StringComparison.Ordinal))
		{
			File.WriteAllBytes(candidate, [0x90]);
		}

		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() =>
			Tools(target, true).LoadFromFile("1000", candidate, Token));

		Assert.Equal((ToolErrorKind.InvalidArgument, ToolHostEffect.NotStarted),
			(exception.Error.Kind, exception.Error.HostEffect));
		Assert.Equal(0, target.Dispatcher.Calls);
		Assert.Empty(target.Calls);
	}

	[Fact]
	public void LoadFromFile_FileLargerThanOneChunk_WritesContiguousBoundedBlocks()
	{
		TargetDouble target = new();
		byte[] input = new byte[MemoryTargets.ChunkBytes + 3];
		input[0] = 0x10;
		input[MemoryTargets.ChunkBytes - 1] = 0x20;
		input[MemoryTargets.ChunkBytes] = 0x30;
		input[^1] = 0x40;
		string path = Path.Combine(_root, "dumps", "input.bin");
		File.WriteAllBytes(path, input);
		List<MemoryBytesWriteRequest> writes = [];
		target.Memory = (method, arguments) =>
		{
			Assert.Equal(nameof(IMemoryClient.WriteBytes), method.Name);
			writes.Add(Assert.IsType<MemoryBytesWriteRequest>(arguments[0]));
			return null;
		};

		FileLoadResult result = Tools(target, true).LoadFromFile("1000", path, Token);

		Assert.Equal(new FileLoadResult(path, "1000", input.Length), result);
		Assert.Collection(writes,
			first =>
			{
				Assert.Equal((ulong) 0x1000, first.Address.ToUInt64());
				Assert.Equal(MemoryTargets.ChunkBytes, first.Bytes.Length);
				Assert.Equal(input[..MemoryTargets.ChunkBytes], first.Bytes);
			},
			second =>
			{
				Assert.Equal(0x1000 + (ulong) MemoryTargets.ChunkBytes, second.Address.ToUInt64());
				Assert.Equal(3, second.Bytes.Length);
				Assert.Equal(input[MemoryTargets.ChunkBytes..], second.Bytes);
			});
		Assert.Equal(["Memory.WriteBytes", "Memory.WriteBytes"], target.CallsTo("Memory"));
	}

	[Fact]
	public void LoadFromFile_SecondBlockHasUnknownEffect_ReportsTheConfirmedPrefix()
	{
		TargetDouble target = new();
		byte[] input = new byte[MemoryTargets.ChunkBytes + 1];
		string path = Path.Combine(_root, "dumps", "partial.bin");
		File.WriteAllBytes(path, input);
		int writes = 0;
		target.Memory = (method, _) =>
		{
			Assert.Equal(nameof(IMemoryClient.WriteBytes), method.Name);
			writes++;
			if (writes == 2)
			{
				throw new CheatEngineFailure(CheatEngineFailureKind.MemoryWriteFailed, "Memory.WriteBytes",
						"The second block may have changed memory.", hostEffect: CheatEngineHostEffect.Unknown)
					.ToException();
			}

			return null;
		};

		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() =>
			Tools(target, true).LoadFromFile("1000", path, Token));

		Assert.Equal((ToolErrorKind.PartialEffect, ToolHostEffect.Started),
			(exception.Error.Kind, exception.Error.HostEffect));
		FileLoadFailure failure =
			exception.Error.Details!.Value.Deserialize(MemoryJsonContext.Default.FileLoadFailure)!;
		Assert.Equal(new FileLoadFailure(path, "1000", MemoryTargets.ChunkBytes, input.Length), failure);
		Assert.Equal(2, writes);
	}

	[Fact]
	public void LoadFromFile_FirstBlockNotApplied_PreservesTheClientFailure()
	{
		TargetDouble target = new();
		string path = Path.Combine(_root, "dumps", "unwritten.bin");
		File.WriteAllBytes(path, [0x90]);
		target.Memory = (method, _) =>
		{
			Assert.Equal(nameof(IMemoryClient.WriteBytes), method.Name);
			throw new CheatEngineFailure(CheatEngineFailureKind.MemoryWriteFailed, "Memory.WriteBytes",
				"The page is read-only.", hostEffect: CheatEngineHostEffect.NotStarted).ToException();
		};

		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() =>
			Tools(target, true).LoadFromFile("1000", path, Token));

		Assert.Equal((ToolErrorKind.MemoryWriteFailed, ToolHostEffect.NotStarted),
			(exception.Error.Kind, exception.Error.HostEffect));
		Assert.Single(target.CallsTo("Memory"));
	}

	[Fact]
	public void LoadFromFile_ResultsAndPartialDetails_UseTheGeneratedJsonSchema()
	{
		string result = JsonSerializer.Serialize(new FileLoadResult("C:/input.bin", "1000", 4),
			MemoryJsonContext.Default.FileLoadResult);
		string failure = JsonSerializer.Serialize(new FileLoadFailure("C:/input.bin", "1000", 2, 4),
			MemoryJsonContext.Default.FileLoadFailure);

		Assert.Equal("{\"path\":\"C:/input.bin\",\"address\":\"1000\",\"bytesWritten\":4}", result);
		Assert.Equal("{\"path\":\"C:/input.bin\",\"address\":\"1000\",\"bytesWritten\":2,\"totalBytes\":4}", failure);
	}

	private static ImmutableArray<byte> Filled(int length)
	{
		return Enumerable.Repeat((byte) 0xAB, length).ToImmutableArray();
	}

	private MemoryFileTools Tools(TargetDouble target, bool withRoot)
	{
		McpFileOptions options = new()
		{
			AllowedRoots = withRoot ? [Path.Combine(_root, "dumps")] : []
		};
		return new MemoryFileTools(target.Dispatch,
			new McpFilePaths(options, Path.Combine(_root, "registry"), Path.Combine(_root, "data")));
	}
}
