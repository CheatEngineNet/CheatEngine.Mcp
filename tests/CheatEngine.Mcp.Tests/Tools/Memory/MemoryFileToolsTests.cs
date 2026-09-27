using System.Collections.Immutable;

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
	private static CancellationToken Token => TestContext.Current.CancellationToken;

	private readonly string _root = Path.Combine(Path.GetTempPath(), $"CheatEngine.Mcp.Tests-{Guid.NewGuid():N}");

	public MemoryFileToolsTests()
	{
		Directory.CreateDirectory(Path.Combine(_root, "dumps"));
		Directory.CreateDirectory(Path.Combine(_root, "data"));
		Directory.CreateDirectory(Path.Combine(_root, "registry"));
	}

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
			Tools(target, true).DumpToFile("1000", 16, path.Replace("{root}", _root, StringComparison.Ordinal), cancellationToken: Token));

		Assert.Equal(ToolErrorKind.InvalidArgument, exception.Error.Kind);
		Assert.Equal(ToolHostEffect.NotStarted, exception.Error.HostEffect);
		Assert.Equal(0, target.Dispatcher.Calls);
	}

	[Fact]
	public void DumpToFile_NoWriteRoot_RefusesEveryPath()
	{
		TargetDouble target = new();

		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() =>
			Tools(target, false).DumpToFile("1000", 16, Path.Combine(_root, "dumps", "dump.bin"), cancellationToken: Token));

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
			Tools(target, true).DumpToFile("10000", 0x4000, path, unreadable: UnreadableMemory.Zero, cancellationToken: Token);

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
