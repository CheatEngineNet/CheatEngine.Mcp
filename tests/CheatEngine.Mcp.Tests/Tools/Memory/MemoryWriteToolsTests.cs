using System.Collections.Immutable;
using System.Text.Json;

using CheatEngine.Client.Inspection;
using CheatEngine.Client.Memory;
using CheatEngine.Client.Results;
using CheatEngine.Mcp.Core.Contract;
using CheatEngine.Mcp.Core.Values;
using CheatEngine.Mcp.Tools.Memory;
using CheatEngine.SDK.Engine.Enums;
using CheatEngine.SDK.Engine.Inspection;
using CheatEngine.SDK.Engine.Runtime;
using CheatEngine.SDK.Engine.Values;

using Xunit.Sdk;

namespace CheatEngine.Mcp.Tests.Tools.Memory;

/// <summary>The memory tools that change the target: typed routing, previous bytes, limits and batch partial effects.</summary>
public sealed class MemoryWriteToolsTests
{
	private static CancellationToken Token => TestContext.Current.CancellationToken;

	[Fact]
	public void Write_Int32_PreReadsPreviousWritesTypedAndVerifiesTheReadBack()
	{
		TargetDouble target = new();
		object? written = null;
		target.Memory = (method, arguments) => method.Name switch
		{
			nameof(IMemoryClient.ReadBytes) => ImmutableArray.Create<byte>(0x64, 0x00, 0x00, 0x00),
			nameof(IMemoryClient.WritePrimitive) => written = arguments[1],
			nameof(IMemoryClient.ReadBytesDetailed) => new MemoryBytesReadOutcome(4,
				ImmutableArray.Create<byte>(0xE8, 0x03, 0x00, 0x00), null),
			_ => throw new XunitException($"Unexpected {method.Name}.")
		};

		MemoryWriteResult result =
			new MemoryWriteTools(target.Dispatch).Write("1000", McpValueType.Int32, "1000", cancellationToken: Token);

		Assert.Equal(new MemoryWriteResult("1000", 4, "64 00 00 00", true), result);
		Assert.Equal(1000, written);
		Assert.Equal(["Memory.ReadBytes", "Memory.WritePrimitive<Int32>", "Memory.ReadBytesDetailed"],
			target.CallsTo("Memory"));
	}

	[Fact]
	public void Write_RepeatedBytes_WritesThePatternBackToBackWithoutTypedRouting()
	{
		TargetDouble target = new();
		MemoryBytesWriteRequest? request = null;
		target.Memory = (method, arguments) => method.Name switch
		{
			nameof(IMemoryClient.ReadBytes) => ImmutableArray.Create(new byte[4]),
			nameof(IMemoryClient.WriteBytes) => request = (MemoryBytesWriteRequest) arguments[0]!,
			_ => throw new XunitException($"Unexpected {method.Name}.")
		};

		MemoryWriteResult result = new MemoryWriteTools(target.Dispatch).Write("1000", McpValueType.Bytes, "90 C3",
			2, verify: false, cancellationToken: Token);

		Assert.Equal([0x90, 0xC3, 0x90, 0xC3], request!.Value.Bytes);
		Assert.Equal(4, result.BytesWritten);
		Assert.False(result.Verified);
	}

	[Fact]
	public void Write_TerminatedWideString_AppendsTwoZeroBytes()
	{
		TargetDouble target = new();
		MemoryBytesWriteRequest? request = null;
		target.Memory = (method, arguments) => method.Name switch
		{
			nameof(IMemoryClient.ReadBytes) => ImmutableArray.Create(new byte[6]),
			nameof(IMemoryClient.WriteBytes) => request = (MemoryBytesWriteRequest) arguments[0]!,
			_ => throw new XunitException($"Unexpected {method.Name}.")
		};

		new MemoryWriteTools(target.Dispatch).Write("1000", McpValueType.WString, "Hi", nullTerminate: true,
			verify: false, cancellationToken: Token);

		Assert.Equal([(byte) 'H', 0, (byte) 'i', 0, 0, 0], request!.Value.Bytes);
	}

	[Theory]
	[InlineData(McpValueType.Bytes, "90", 65537, false, ToolErrorKind.LimitExceeded)]
	[InlineData(McpValueType.Bytes, "90 90 90 90 90 90 90 90 90 90 90 90 90 90 90 90 90", 65536, false,
		ToolErrorKind.LimitExceeded)]
	[InlineData(McpValueType.Int32, "1", 2, false, ToolErrorKind.InvalidArgument)]
	[InlineData(McpValueType.Int32, "1", 1, true, ToolErrorKind.InvalidArgument)]
	[InlineData(McpValueType.Int8, "300", 1, false, ToolErrorKind.InvalidArgument)]
	[InlineData(McpValueType.String, "", 1, false, ToolErrorKind.InvalidArgument)]
	public void Write_InvalidArguments_RefuseBeforeAnyDispatch(McpValueType type, string value, int repeat,
		bool nullTerminate, ToolErrorKind kind)
	{
		TargetDouble target = new();

		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() =>
			new MemoryWriteTools(target.Dispatch).Write("1000", type, value, repeat, nullTerminate,
				cancellationToken: Token));

		Assert.Equal(kind, exception.Error.Kind);
		Assert.Equal(ToolHostEffect.NotStarted, exception.Error.HostEffect);
		Assert.Equal(0, target.Dispatcher.Calls);
	}

	[Fact]
	public void Write_PointerAbove32BitsOnA32BitTarget_IsRefusedBeforeAnyWrite()
	{
		TargetDouble target = new()
		{
			Bitness = PointerSize.Bit32
		};

		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() =>
			new MemoryWriteTools(target.Dispatch).Write("1000", McpValueType.Pointer, "1FFFFFFFF",
				cancellationToken: Token));

		Assert.Equal(ToolErrorKind.InvalidArgument, exception.Error.Kind);
		Assert.Equal(0, target.Mutations);
	}

	[Fact]
	public void WriteBatch_FailureAfterAPrefix_IsAPartialEffectWithCompletedAndFailedIndex()
	{
		TargetDouble target = new();
		target.Memory = (method, _) =>
		{
			Assert.Equal(nameof(IMemoryClient.WritePrimitiveBatchDetailed), method.Name);
			return new MemoryPrimitiveBatchWriteOutcome(3, 1, 1,
				new CheatEngineFailure(CheatEngineFailureKind.MemoryWriteFailed, "Memory.WritePrimitiveBatch",
					"The page is read-only.", hostEffect: CheatEngineHostEffect.Started),
				MemoryBatchWriteEffectState.Partial);
		};

		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() =>
			new MemoryWriteTools(target.Dispatch).WriteBatch(
			[
				new MemoryWriteItem("1000", McpValueType.Int32, "1"),
				new MemoryWriteItem("2000", McpValueType.Int32, "2"),
				new MemoryWriteItem("3000", McpValueType.Int32, "3")
			], cancellationToken: Token));

		Assert.Equal(ToolErrorKind.PartialEffect, exception.Error.Kind);
		Assert.Equal(ToolHostEffect.Started, exception.Error.HostEffect);
		MemoryWriteBatchFailure details = exception.Error.Details!.Value.Deserialize(
			MemoryJsonContext.Default.MemoryWriteBatchFailure)!;
		Assert.Equal(new MemoryWriteBatchFailure(1, 1, BatchWriteEffect.Partial), details);
		Assert.Single(target.CallsTo("Memory"));
	}

	[Fact]
	public void WriteBatch_FirstWriteFailsBeforeAnyEffect_IsTheWriteFailureWithNotStartedDetails()
	{
		TargetDouble target = new();
		target.Memory = (method, _) => method.Name == nameof(IMemoryClient.TryWriteBytes)
			? throw new XunitException("The bytes item follows the failed run.")
			: new MemoryPrimitiveBatchWriteOutcome(1, 0, 0,
				new CheatEngineFailure(CheatEngineFailureKind.MemoryWriteFailed, "Memory.WritePrimitiveBatch",
					"The page is read-only.", hostEffect: CheatEngineHostEffect.NotStarted),
				MemoryBatchWriteEffectState.NotStarted);

		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() =>
			new MemoryWriteTools(target.Dispatch).WriteBatch(
			[
				new MemoryWriteItem("1000", McpValueType.Float, "1.5"),
				new MemoryWriteItem("2000", McpValueType.Bytes, "90")
			], cancellationToken: Token));

		Assert.Equal(ToolErrorKind.MemoryWriteFailed, exception.Error.Kind);
		Assert.Equal(new MemoryWriteBatchFailure(0, 0, BatchWriteEffect.NotStarted),
			exception.Error.Details!.Value.Deserialize(MemoryJsonContext.Default.MemoryWriteBatchFailure));
	}

	[Fact]
	public void WriteBatch_ConsecutiveTypes_WriteInRequestOrderAndVerify()
	{
		TargetDouble target = new();
		List<string> order = [];
		target.Memory = (method, arguments) =>
		{
			switch (method.Name)
			{
				case nameof(IMemoryClient.WritePrimitiveBatchDetailed):
					int count = method.GetGenericArguments()[0] == typeof(int)
						? ((MemoryPrimitiveBatchWriteRequest<int>) arguments[0]!).Values.Length
						: ((MemoryPrimitiveBatchWriteRequest<Address>) arguments[0]!).Values.Length;
					order.Add($"{method.GetGenericArguments()[0].Name}x{count}");
					return new MemoryPrimitiveBatchWriteOutcome(count, count, null, null,
						MemoryBatchWriteEffectState.Completed);
				case nameof(IMemoryClient.TryWriteString):
					order.Add("string");
					arguments[1] = default(CheatEngineFailure);
					return true;
				case nameof(IMemoryClient.ReadBytesDetailed):
					MemoryBytesReadRequest request = (MemoryBytesReadRequest) arguments[0]!;
					byte[] bytes = request.Address == new Address(0x3000)
						? [9, 9, 9, 9, 9, 9, 9, 9]
						: ExpectedAt(request);
					return new MemoryBytesReadOutcome(request.Length, bytes.ToImmutableArray(), null);
				default:
					throw new XunitException($"Unexpected {method.Name}.");
			}
		};

		MemoryWriteBatchResult result = new MemoryWriteTools(target.Dispatch).WriteBatch(
		[
			new MemoryWriteItem("1000", McpValueType.Int32, "1"), new MemoryWriteItem("2000", McpValueType.Int32, "2"),
			new MemoryWriteItem("3000", McpValueType.Pointer, "401000"),
			new MemoryWriteItem("4000", McpValueType.String, "ab"), new MemoryWriteItem("5000", McpValueType.Int32, "5")
		], true, Token);

		Assert.Equal(["Int32x2", "Addressx1", "string", "Int32x1"], order);
		Assert.Equal((5, false), (result.Written, result.Verified));
		Assert.Equal([2], result.Mismatched!);
	}

	[Fact]
	public void WriteBatch_UnresolvedAddress_RefusesTheWholeBatchBeforeAnyWrite()
	{
		TargetDouble target = new();

		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() =>
			new MemoryWriteTools(target.Dispatch).WriteBatch(
			[
				new MemoryWriteItem("1000", McpValueType.Int32, "1"),
				new MemoryWriteItem("nowhere", McpValueType.Int32, "2")
			], cancellationToken: Token));

		Assert.Equal(ToolErrorKind.NotFound, exception.Error.Kind);
		Assert.Contains("items[1].address", exception.Error.Message, StringComparison.Ordinal);
		Assert.Equal(0, target.Mutations);
	}

	[Fact]
	public void Copy_Range_ReadsTheWholeSourceBeforeWritingTheDestination()
	{
		TargetDouble target = new();
		target.Memory = (method, arguments) => method.Name switch
		{
			nameof(IMemoryClient.ReadBytes) => ImmutableArray.Create<byte>(1, 2, 3),
			nameof(IMemoryClient.WriteBytes) => Assert.IsType<MemoryBytesWriteRequest>(arguments[0]).Bytes,
			_ => throw new XunitException($"Unexpected {method.Name}.")
		};

		MemoryCopyResult result = new MemoryWriteTools(target.Dispatch).Copy("1000", "2001", 3, Token);

		Assert.Equal(new MemoryCopyResult("1000", "2001", 3), result);
		Assert.Equal(["Memory.ReadBytes", "Memory.WriteBytes"], target.CallsTo("Memory"));
	}

	[Fact]
	public void SetProtection_FixedScript_ReportsPreviousAndCurrentAccess()
	{
		TargetDouble target = new();
		UseMemoryRegion(target, Region(0x401000, 0x1000));
		string? source = null;
		target.LuaResult = script =>
		{
			source = script;
			return new ProtectionProbe(new ProtectionFlags(true, false, true), new ProtectionFlags(true, true, true));
		};

		ProtectionChange change =
			new MemoryWriteTools(target.Dispatch).SetProtection("401000", 4096, true, true, true, Token);

		Assert.Equal(new ProtectionChange("401000", 4096, new ProtectionFlags(true, false, true),
			new ProtectionFlags(true, true, true)), change);
		Assert.Contains("[1] = 0x401000, [2] = 4096, [3] = true, [4] = true, [5] = true", source,
			StringComparison.Ordinal);
		Assert.Equal(1, target.LuaCalls);
	}

	[Fact]
	public void SetProtection_RangeCrossingACommittedRegion_IsRefusedBeforeLua()
	{
		TargetDouble target = new();
		UseMemoryRegion(target, Region(0x401000, 0x1000));

		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() =>
			new MemoryWriteTools(target.Dispatch).SetProtection("401800", 0x801, cancellationToken: Token));

		Assert.Equal(ToolErrorKind.InvalidArgument, exception.Error.Kind);
		Assert.Equal(ToolHostEffect.NotStarted, exception.Error.HostEffect);
		Assert.Equal(["Inspection.TryResolveAddress", "Inspection.TryGetMemoryRegion"], target.CallsTo("Inspection"));
		Assert.Equal(0, target.LuaCalls);
	}

	[Fact]
	public void SetProtection_UncommittedRegion_IsRefusedBeforeLua()
	{
		TargetDouble target = new();
		UseMemoryRegion(target, Region(0x401000, 0x1000, MemoryRegionState.Reserved));

		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() =>
			new MemoryWriteTools(target.Dispatch).SetProtection("401000", 0x1000, cancellationToken: Token));

		Assert.Equal(ToolErrorKind.InvalidState, exception.Error.Kind);
		Assert.Equal(ToolHostEffect.NotStarted, exception.Error.HostEffect);
		Assert.Equal(0, target.LuaCalls);
	}

	[Fact]
	public void SetProtection_UnknownRegion_IsRefusedBeforeLua()
	{
		TargetDouble target = new();
		target.Inspection = (method, arguments) =>
		{
			Assert.Equal(nameof(IInspectionClient.TryGetMemoryRegion), method.Name);
			arguments[1] = default(MemoryRegionInfo);
			arguments[2] = new CheatEngineFailure(CheatEngineFailureKind.NotFound, "Inspection.GetMemoryRegion",
				"No region contains the address.", hostEffect: CheatEngineHostEffect.Completed);
			return false;
		};

		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() =>
			new MemoryWriteTools(target.Dispatch).SetProtection("401000", 0x1000, cancellationToken: Token));

		Assert.Equal(ToolErrorKind.NotFound, exception.Error.Kind);
		Assert.Equal(ToolHostEffect.NotStarted, exception.Error.HostEffect);
		Assert.Equal(0, target.LuaCalls);
	}

	[Fact]
	public void SetProtection_RangeAbove16MiB_RefusesBeforeLua()
	{
		TargetDouble target = new();

		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() =>
			new MemoryWriteTools(target.Dispatch).SetProtection("401000", (16 * 1024 * 1024) + 1,
				cancellationToken: Token));

		Assert.Equal(ToolErrorKind.LimitExceeded, exception.Error.Kind);
		Assert.Equal(0, target.Dispatcher.Calls);
		Assert.Equal(0, target.LuaCalls);
	}

	private static byte[] ExpectedAt(MemoryBytesReadRequest request)
	{
		return request.Address.ToUInt64() switch
		{
			0x1000 => [1, 0, 0, 0],
			0x2000 => [2, 0, 0, 0],
			0x4000 => [(byte) 'a', (byte) 'b'],
			0x5000 => [5, 0, 0, 0],
			_ => throw new XunitException($"Unexpected read-back at {request.Address}.")
		};
	}

	private static void UseMemoryRegion(TargetDouble target, MemoryRegionInfo region)
	{
		target.Inspection = (method, arguments) =>
		{
			Assert.Equal(nameof(IInspectionClient.TryGetMemoryRegion), method.Name);
			arguments[1] = region;
			arguments[2] = default(CheatEngineFailure);
			return true;
		};
	}

	private static MemoryRegionInfo Region(ulong baseAddress, ulong size,
		MemoryRegionState state = MemoryRegionState.Committed)
	{
		return new MemoryRegionInfo(new Address(baseAddress), new Address(baseAddress), MemoryProtection.ReadWrite,
			new MemorySize(size), state, MemoryProtection.ReadWrite, MemoryRegionType.Private, null);
	}
}
