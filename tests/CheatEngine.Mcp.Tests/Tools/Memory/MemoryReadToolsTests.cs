using System.Collections.Immutable;
using System.Security.Cryptography;

using CheatEngine.Client.Memory;
using CheatEngine.Client.Results;
using CheatEngine.Mcp.Core.Contract;
using CheatEngine.Mcp.Core.Values;
using CheatEngine.Mcp.Tools.Memory;
using CheatEngine.SDK.Engine.Runtime;
using CheatEngine.SDK.Engine.Values;

using Xunit.Sdk;

namespace CheatEngine.Mcp.Tests.Tools.Memory;

/// <summary>The read-only memory tools: typed routing, limits, batch resumption and chunked range work.</summary>
public sealed class MemoryReadToolsTests
{
	private static CancellationToken Token => TestContext.Current.CancellationToken;

	[Theory]
	[InlineData(McpValueType.Int8, "-5")]
	[InlineData(McpValueType.UInt8, "200")]
	[InlineData(McpValueType.Int16, "-300")]
	[InlineData(McpValueType.UInt16, "60000")]
	[InlineData(McpValueType.Int32, "1337")]
	[InlineData(McpValueType.UInt32, "4000000000")]
	[InlineData(McpValueType.Int64, "-9000000000")]
	[InlineData(McpValueType.UInt64, "18000000000000000000")]
	[InlineData(McpValueType.Float, "1.5")]
	[InlineData(McpValueType.Double, "-0.25")]
	[InlineData(McpValueType.Pointer, "7FF6A1B2C3D0")]
	public void Read_FixedType_RoutesToTheTypedPrimitiveOfItsWidth(McpValueType type, string expected)
	{
		(Type primitive, object value) = Primitive(type);
		TargetDouble target = new();
		target.Symbols["game.exe+10"] = 0x401010;
		target.Memory = (method, _) =>
		{
			Assert.Equal(nameof(IMemoryClient.ReadPrimitive), method.Name);
			Assert.Equal(primitive, method.GetGenericArguments()[0]);
			return value;
		};

		MemoryReadResult result =
			new MemoryReadTools(target.Dispatch).Read("game.exe+10", type, cancellationToken: Token);

		Assert.Equal(new MemoryReadResult("401010", type, expected), result);
		Assert.Equal(1, target.Dispatcher.Calls);
	}

	[Fact]
	public void Read_CountAboveOne_ReadsOneRangeAndDecodesEachValue()
	{
		TargetDouble target = new();
		MemoryBytesReadRequest? request = null;
		target.Memory = (method, arguments) =>
		{
			Assert.Equal(nameof(IMemoryClient.ReadBytes), method.Name);
			request = (MemoryBytesReadRequest) arguments[0]!;
			return ImmutableArray.Create<byte>(0x01, 0x00, 0xFF, 0xFF, 0x2A, 0x00);
		};

		MemoryReadResult result =
			new MemoryReadTools(target.Dispatch).Read("1000", McpValueType.Int16, 3, cancellationToken: Token);

		Assert.Equal(6, request!.Value.Length);
		Assert.Equal(["1", "-1", "42"], result.Values!);
		Assert.Null(result.Value);
	}

	[Fact]
	public void Read_PointersOfA32BitTarget_AreDecodedAtFourBytes()
	{
		TargetDouble target = new()
		{
			Bitness = PointerSize.Bit32
		};
		target.Memory = (_, arguments) =>
		{
			Assert.Equal(8, ((MemoryBytesReadRequest) arguments[0]!).Length);
			return ImmutableArray.Create<byte>(0x10, 0x20, 0x40, 0x00, 0xFF, 0xFF, 0xFF, 0xFF);
		};

		MemoryReadResult result =
			new MemoryReadTools(target.Dispatch).Read("1000", McpValueType.Pointer, 2, cancellationToken: Token);

		Assert.Equal(["402010", "FFFFFFFF"], result.Values!);
	}

	[Theory]
	[InlineData(McpValueType.Bytes, 1, null, null, "size", ToolErrorKind.InvalidArgument)]
	[InlineData(McpValueType.Bytes, 1, 16385, null, "size", ToolErrorKind.LimitExceeded)]
	[InlineData(McpValueType.Int32, 1025, null, null, "count", ToolErrorKind.LimitExceeded)]
	[InlineData(McpValueType.String, 2, null, null, "count", ToolErrorKind.InvalidArgument)]
	[InlineData(McpValueType.String, 1, null, 4097, "length", ToolErrorKind.LimitExceeded)]
	[InlineData(McpValueType.Int32, 1, 4, null, "size", ToolErrorKind.InvalidArgument)]
	public void Read_InvalidShape_RefusesBeforeAnyDispatch(McpValueType type, int count, int? size, int? length,
		string parameter, ToolErrorKind kind)
	{
		TargetDouble target = new();

		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() =>
			new MemoryReadTools(target.Dispatch).Read("1000", type, count, size, length, cancellationToken: Token));

		Assert.Equal(kind, exception.Error.Kind);
		Assert.Equal(ToolHostEffect.NotStarted, exception.Error.HostEffect);
		Assert.StartsWith(parameter, exception.Error.Message, StringComparison.Ordinal);
		Assert.Equal(0, target.Dispatcher.Calls);
	}

	[Fact]
	public void Read_UnresolvedExpression_IsNotFoundWithoutAMemoryCall()
	{
		TargetDouble target = new();

		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() =>
			new MemoryReadTools(target.Dispatch).Read("missing.symbol", McpValueType.Int32, cancellationToken: Token));

		Assert.Equal(ToolErrorKind.NotFound, exception.Error.Kind);
		Assert.Empty(target.CallsTo("Memory"));
	}

	[Fact]
	public void ReadBatch_FailedIndex_ReportsTheItemInBandAndResumesAfterIt()
	{
		TargetDouble target = new();
		List<Address[]> requests = [];
		target.Memory = (method, arguments) =>
		{
			Assert.Equal(nameof(IMemoryClient.ReadPrimitiveBatchDetailed), method.Name);
			Address[] addresses = [.. ((MemoryPrimitiveBatchReadRequest<int>) arguments[0]!).Addresses];
			requests.Add(addresses);
			return requests.Count == 1
				? new MemoryPrimitiveBatchReadOutcome<int>(3, [7], 1,
					new CheatEngineFailure(CheatEngineFailureKind.MemoryReadFailed, "Memory.ReadPrimitiveBatch",
						"The page is not readable.", hostEffect: CheatEngineHostEffect.Completed))
				: new MemoryPrimitiveBatchReadOutcome<int>(1, [9], null, null);
		};

		MemoryReadBatchResult result = new MemoryReadTools(target.Dispatch).ReadBatch(
		[
			new MemoryReadItem("1000", McpValueType.Int32), new MemoryReadItem("2000", McpValueType.Int32),
			new MemoryReadItem("3000", McpValueType.Int32)
		], Token);

		Assert.Equal(1, result.Failed);
		Assert.Equal(new MemoryReadBatchEntry("1000", "7"), result.Items[0]);
		Assert.Equal(ToolErrorKind.MemoryReadFailed, result.Items[1].Error!.Kind);
		Assert.Equal("2000", result.Items[1].Address);
		Assert.Equal(new MemoryReadBatchEntry("3000", "9"), result.Items[2]);
		Assert.Equal([new Address(0x3000)], requests[1]);
		Assert.Equal(1, target.Dispatcher.Calls);
	}

	[Fact]
	public void ReadBatch_MixedItems_ReadsEachFixedTypeInOneTypedBatchAndKeepsOrder()
	{
		TargetDouble target = new();
		target.Memory = (method, arguments) => method.Name switch
		{
			nameof(IMemoryClient.ReadPrimitiveBatchDetailed) when method.GetGenericArguments()[0] == typeof(int) =>
				new MemoryPrimitiveBatchReadOutcome<int>(2, [1, 2], null, null),
			nameof(IMemoryClient.ReadPrimitiveBatchDetailed) when method.GetGenericArguments()[0] == typeof(float) =>
				new MemoryPrimitiveBatchReadOutcome<float>(1, [0.5F], null, null),
			nameof(IMemoryClient.ReadBytesDetailed) => new MemoryBytesReadOutcome(2,
				ImmutableArray.Create<byte>(0x90, 0xC3), null),
			_ => throw new XunitException($"Unexpected {method.Name}.")
		};

		MemoryReadBatchResult result = new MemoryReadTools(target.Dispatch).ReadBatch(
		[
			new MemoryReadItem("10", McpValueType.Int32), new MemoryReadItem("20", McpValueType.Float),
			new MemoryReadItem("30", McpValueType.Bytes, 2), new MemoryReadItem("40", McpValueType.Int32),
			new MemoryReadItem("nowhere", McpValueType.Int32)
		], Token);

		Assert.Equal(["1", "0.5", "90 C3", "2", null], result.Items.Select(static item => item.Value));
		Assert.Equal(ToolErrorKind.NotFound, result.Items[4].Error!.Kind);
		Assert.Equal("nowhere", result.Items[4].Address);
		Assert.Equal([
			"Memory.ReadPrimitiveBatchDetailed<Int32>", "Memory.ReadPrimitiveBatchDetailed<Single>",
			"Memory.ReadBytesDetailed"
		], target.CallsTo("Memory"));
	}

	[Fact]
	public void ReadBatch_TotalAbove256KiB_RefusesBeforeAnyDispatch()
	{
		TargetDouble target = new();
		MemoryReadItem[] items =
			[.. Enumerable.Range(0, 17).Select(static index => new MemoryReadItem("1000", McpValueType.Bytes, 16384))];

		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() =>
			new MemoryReadTools(target.Dispatch).ReadBatch(items, Token));

		Assert.Equal(ToolErrorKind.LimitExceeded, exception.Error.Kind);
		Assert.Equal(0, target.Dispatcher.Calls);
	}

	[Fact]
	public void Compare_Differences_ListsRunsAndStopsAtTheLimit()
	{
		TargetDouble target = new();
		byte[] a = [1, 2, 3, 4, 5, 6, 7, 8];
		byte[] b = [1, 9, 9, 4, 5, 6, 0, 8];
		target.Memory = (_, arguments) =>
		{
			MemoryBytesReadRequest request = (MemoryBytesReadRequest) arguments[0]!;
			return request.Address == new Address(0x100) ? a.ToImmutableArray() : b.ToImmutableArray();
		};

		MemoryCompareResult all =
			new MemoryReadTools(target.Dispatch).Compare("100", "200", 8, cancellationToken: Token);
		MemoryCompareResult first = new MemoryReadTools(target.Dispatch).Compare("100", "200", 8, 1, Token);

		Assert.False(all.Equal);
		Assert.False(all.Truncated);
		Assert.Equal([new MemoryDifference("1", 2, "02 03", "09 09"), new MemoryDifference("6", 1, "07", "00")],
			all.Differences);
		Assert.True(first.Truncated);
		Assert.Single(first.Differences);
	}

	[Fact]
	public void Compare_TargetChangedBetweenChunks_IsTargetChanged()
	{
		TargetDouble target = new();
		target.Epochs.Enqueue(1);
		target.Epochs.Enqueue(2);
		target.Memory = (_, arguments) =>
			ImmutableArray.Create(new byte[((MemoryBytesReadRequest) arguments[0]!).Length]);

		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() =>
			new MemoryReadTools(target.Dispatch).Compare("100", "200", MemoryTargets.ChunkBytes + 1,
				cancellationToken: Token));

		Assert.Equal(ToolErrorKind.TargetChanged, exception.Error.Kind);
		Assert.Equal(2, target.Dispatcher.Calls);
		Assert.Equal(2, target.CallsTo("Memory").Length);
	}

	[Fact]
	public void Hash_ChunkedRange_MatchesTheManagedDigest()
	{
		TargetDouble target = new();
		byte[] memory = new byte[MemoryTargets.ChunkBytes + 10];
		Random.Shared.NextBytes(memory);
		target.Memory = (_, arguments) =>
		{
			MemoryBytesReadRequest request = (MemoryBytesReadRequest) arguments[0]!;
			int offset = (int) (request.Address.ToUInt64() - 0x10000);
			return memory.AsSpan(offset, request.Length).ToImmutableArray();
		};

		MemoryHashResult sha256 =
			new MemoryReadTools(target.Dispatch).Hash("10000", memory.Length, cancellationToken: Token);
		MemoryHashResult md5 =
			new MemoryReadTools(target.Dispatch).Hash("10000", memory.Length, MemoryHashAlgorithm.Md5, Token);

		Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(memory)), sha256.Hash);
		using IncrementalHash expected = IncrementalHash.CreateHash(HashAlgorithmName.MD5);
		expected.AppendData(memory);
		Assert.Equal(Convert.ToHexStringLower(expected.GetHashAndReset()), md5.Hash);
		Assert.Equal(4, target.Dispatcher.Calls);
	}

	private static (Type Primitive, object Value) Primitive(McpValueType type)
	{
		return type switch
		{
			McpValueType.Int8 => (typeof(sbyte), (sbyte) -5),
			McpValueType.UInt8 => (typeof(byte), (byte) 200),
			McpValueType.Int16 => (typeof(short), (short) -300),
			McpValueType.UInt16 => (typeof(ushort), (ushort) 60000),
			McpValueType.Int32 => (typeof(int), 1337),
			McpValueType.UInt32 => (typeof(uint), 4000000000U),
			McpValueType.Int64 => (typeof(long), -9000000000L),
			McpValueType.UInt64 => (typeof(ulong), 18000000000000000000UL),
			McpValueType.Float => (typeof(float), 1.5F),
			McpValueType.Double => (typeof(double), -0.25D),
			_ => (typeof(Address), new Address(0x7FF6A1B2C3D0))
		};
	}
}
