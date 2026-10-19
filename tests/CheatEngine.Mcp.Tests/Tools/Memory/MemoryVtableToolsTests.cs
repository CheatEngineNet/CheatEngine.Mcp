using CheatEngine.Client.Inspection;
using CheatEngine.Client.Memory;
using CheatEngine.Client.Results;
using CheatEngine.Mcp.Core.Contract;
using CheatEngine.Mcp.Tools.Memory;
using CheatEngine.SDK.Engine.Values;

using Xunit.Sdk;

namespace CheatEngine.Mcp.Tests.Tools.Memory;

/// <summary>The bounded, read-only native vtable inspection tool.</summary>
public sealed class MemoryVtableToolsTests
{
	private static CancellationToken Token => TestContext.Current.CancellationToken;

	[Fact]
	public void ReadVtable_ObjectAddress_ReadsTheFirstPointerThenNamesEachNonNullSlot()
	{
		TargetDouble target = new()
		{
			Memory = (method, arguments) =>
			{
				Assert.Equal(nameof(IMemoryClient.ReadBytesDetailed), method.Name);
				MemoryBytesReadRequest request = (MemoryBytesReadRequest) arguments[0]!;
				Assert.Equal(8, request.Length);
				return request.Address.ToUInt64() switch
				{
					0x2000 => Bytes(0x5000, 8),
					0x5000 => Bytes(0x12345678, 8),
					0x5008 => Bytes(0x87654321, 8),
					_ => throw new XunitException($"Unexpected read at {request.Address}.")
				};
			},
			Inspection = (method, arguments) =>
				{
					Assert.Equal(nameof(IInspectionClient.TryResolveName), method.Name);
					ulong function = ((Address) arguments[0]!).ToUInt64();
					arguments[1] = function == 0x12345678 ? "game!First" : "game!Second";
					arguments[2] = default(CheatEngineFailure);
					return true;
				}
		};

		MemoryVtableResult result = new MemoryVtableTools(target.Dispatch).ReadVtable("2000", count: 2,
			cancellationToken: Token);

		Assert.Equal("2000", result.Address);
		Assert.Equal("5000", result.VtableAddress);
		Assert.Equal(8, result.PointerBytes);
		Assert.Equal(["5000", "5008"], result.Slots.Select(static slot => slot.SlotAddress));
		Assert.Equal(["12345678", "87654321"], result.Slots.Select(static slot => slot.FunctionAddress));
		Assert.Equal(["game!First", "game!Second"], result.Slots.Select(static slot => slot.Symbol));
		Assert.Equal(["Memory.ReadBytesDetailed", "Memory.ReadBytesDetailed", "Memory.ReadBytesDetailed"],
			target.CallsTo("Memory"));
	}

	[Fact]
	public void ReadVtable_DirectX86Table_UsesFourByteSlotsAndStartIndex()
	{
		TargetDouble target = new()
		{
			Bitness = CheatEngine.SDK.Engine.Runtime.PointerSize.Bit32
		};
		List<MemoryBytesReadRequest> requests = [];
		target.Memory = (method, arguments) =>
		{
			Assert.Equal(nameof(IMemoryClient.ReadBytesDetailed), method.Name);
			MemoryBytesReadRequest request = (MemoryBytesReadRequest) arguments[0]!;
			requests.Add(request);
			Assert.Equal(4, request.Length);
			return Bytes(request.Address.ToUInt64() == 0x1008 ? 0x400010UL : 0x400020UL, 4);
		};
		target.Inspection = (method, arguments) =>
		{
			Assert.Equal(nameof(IInspectionClient.TryResolveName), method.Name);
			arguments[1] = null;
			arguments[2] = new CheatEngineFailure(CheatEngineFailureKind.NotFound, "Inspection.TryResolveName",
				"No symbol.", hostEffect: CheatEngineHostEffect.Completed);
			return false;
		};

		MemoryVtableResult result = new MemoryVtableTools(target.Dispatch).ReadVtable("1000", true, 2, 2, Token);

		Assert.Equal("1000", result.Address);
		Assert.Equal("1000", result.VtableAddress);
		Assert.Equal(4, result.PointerBytes);
		Assert.Equal(2, result.StartIndex);
		Assert.Equal([0x1008UL, 0x100CUL], requests.Select(static request => request.Address.ToUInt64()));
		Assert.Equal(["400010", "400020"], result.Slots.Select(static slot => slot.FunctionAddress));
		Assert.Equal([2, 3], result.Slots.Select(static slot => slot.Index));
	}

	[Fact]
	public void ReadVtable_NullSlot_IsReturnedAsDataWithoutNameLookup()
	{
		TargetDouble target = new()
		{
			Memory = (_, _) => Bytes(0, 8)
		};

		MemoryVtableResult result = new MemoryVtableTools(target.Dispatch).ReadVtable("5000", true, count: 1,
			cancellationToken: Token);

		MemoryVtableSlot slot = Assert.Single(result.Slots);
		Assert.Equal("0", slot.FunctionAddress);
		Assert.Null(slot.Symbol);
		Assert.Null(slot.Error);
		Assert.DoesNotContain(target.CallsTo("Inspection"), static call => call.Contains("TryResolveName", StringComparison.Ordinal));
	}

	[Fact]
	public void ReadVtable_UnreadableSlot_ReportsItAndContinuesToLaterSlots()
	{
		TargetDouble target = new()
		{
			Memory = (_, arguments) =>
			{
				ulong address = ((MemoryBytesReadRequest) arguments[0]!).Address.ToUInt64();
				return address == 0x5008
					? new MemoryBytesReadOutcome(8, [], new CheatEngineFailure(CheatEngineFailureKind.MemoryReadFailed,
						"Memory.ReadBytesDetailed", "Unreadable.", hostEffect: CheatEngineHostEffect.Completed))
					: Bytes(0, 8);
			}
		};

		MemoryVtableResult result = new MemoryVtableTools(target.Dispatch).ReadVtable("5000", true, count: 3,
			cancellationToken: Token);

		Assert.Null(result.Slots[1].FunctionAddress);
		Assert.Equal(ToolErrorKind.MemoryReadFailed, result.Slots[1].Error!.Kind);
		Assert.Equal("5010", result.Slots[2].SlotAddress);
		Assert.Equal("0", result.Slots[2].FunctionAddress);
		Assert.Equal(3, target.CallsTo("Memory").Length);
	}

	[Fact]
	public void ReadVtable_UnreadableObjectPointer_FailsTheWholeRequest()
	{
		TargetDouble target = new()
		{
			Memory = (_, _) => new MemoryBytesReadOutcome(8, [],
				new CheatEngineFailure(CheatEngineFailureKind.MemoryReadFailed, "Memory.ReadBytesDetailed", "Unreadable.",
					hostEffect: CheatEngineHostEffect.Completed))
		};

		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() =>
			new MemoryVtableTools(target.Dispatch).ReadVtable("2000", cancellationToken: Token));

		Assert.Equal(ToolErrorKind.MemoryReadFailed, exception.Error.Kind);
		Assert.Single(target.CallsTo("Memory"));
	}

	[Fact]
	public void ReadVtable_UnavailableSymbol_IsOmittedWithoutDiscardingTheSlot()
	{
		TargetDouble target = new()
		{
			Memory = (_, _) => Bytes(0x1234, 8),
			Inspection = (method, arguments) =>
				{
					Assert.Equal(nameof(IInspectionClient.TryResolveName), method.Name);
					arguments[1] = null;
					arguments[2] = new CheatEngineFailure(CheatEngineFailureKind.NotFound, "Inspection.TryResolveName",
						"No symbol.", hostEffect: CheatEngineHostEffect.Completed);
					return false;
				}
		};

		MemoryVtableResult result = new MemoryVtableTools(target.Dispatch).ReadVtable("5000", true, count: 1,
			cancellationToken: Token);

		MemoryVtableSlot slot = Assert.Single(result.Slots);
		Assert.Equal("1234", slot.FunctionAddress);
		Assert.Null(slot.Symbol);
		Assert.Null(slot.Error);
	}

	[Fact]
	public void ReadVtable_TargetChangedDuringSlots_PropagatesTheLifecycleFailure()
	{
		TargetDouble target = new();
		target.Epochs.Enqueue(1);
		target.Epochs.Enqueue(1);
		target.Epochs.Enqueue(1);
		target.Epochs.Enqueue(1);
		target.Epochs.Enqueue(1);
		target.Epochs.Enqueue(2);
		target.Memory = (_, _) => Bytes(0, 8);

		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() =>
			new MemoryVtableTools(target.Dispatch).ReadVtable("5000", true, count: 2, cancellationToken: Token));

		Assert.Equal(ToolErrorKind.TargetChanged, exception.Error.Kind);
		Assert.Equal(2, target.CallsTo("Memory").Length);
	}

	[Fact]
	public void ReadVtable_CancelledBetweenSlots_DoesNotHideTheCancellation()
	{
		using CancellationTokenSource cancellation = CancellationTokenSource.CreateLinkedTokenSource(Token);
		TargetDouble target = new()
		{
			Memory = (_, _) =>
			{
				cancellation.Cancel();
				return Bytes(0, 8);
			}
		};

		Assert.ThrowsAny<OperationCanceledException>(() =>
			new MemoryVtableTools(target.Dispatch).ReadVtable("5000", true, count: 2,
				cancellationToken: cancellation.Token));

		Assert.Single(target.CallsTo("Memory"));
	}

	[Fact]
	public void ReadVtable_ShortSuccessfulSlotRead_IsRejectedAsAnInvalidHostResult()
	{
		TargetDouble target = new()
		{
			Memory = (_, _) => new MemoryBytesReadOutcome(4, [1, 2, 3, 4], null)
		};

		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() =>
			new MemoryVtableTools(target.Dispatch).ReadVtable("5000", true, cancellationToken: Token));

		Assert.Equal(ToolErrorKind.InvalidState, exception.Error.Kind);
	}

	[Theory]
	[InlineData(-1, 1, "startIndex", ToolErrorKind.InvalidArgument)]
	[InlineData(4096, 1, "startIndex", ToolErrorKind.LimitExceeded)]
	[InlineData(0, 0, "count", ToolErrorKind.InvalidArgument)]
	[InlineData(0, 129, "count", ToolErrorKind.LimitExceeded)]
	public void ReadVtable_InvalidBounds_RefusesBeforeAnyDispatch(int startIndex, int count, string parameter,
		ToolErrorKind kind)
	{
		TargetDouble target = new();

		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() =>
			new MemoryVtableTools(target.Dispatch).ReadVtable("1000", true, startIndex, count, Token));

		Assert.Equal(kind, exception.Error.Kind);
		Assert.StartsWith(parameter, exception.Error.Message, StringComparison.Ordinal);
		Assert.Equal(0, target.Dispatcher.Calls);
	}

	[Fact]
	public void ReadVtable_SlotRangeOverflow_IsRejectedBeforeMemoryRead()
	{
		TargetDouble target = new()
		{
			Bitness = CheatEngine.SDK.Engine.Runtime.PointerSize.Bit32,
			Memory = (_, _) => throw new XunitException("Overflow must not read memory.")
		};

		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() =>
			new MemoryVtableTools(target.Dispatch).ReadVtable("FFFFFFFC", true, count: 2, cancellationToken: Token));

		Assert.Equal(ToolErrorKind.InvalidArgument, exception.Error.Kind);
		Assert.StartsWith("startIndex", exception.Error.Message, StringComparison.Ordinal);
		Assert.Empty(target.CallsTo("Memory"));
	}

	[Fact]
	public void ReadVtable_NullObjectPointer_RejectsWithoutReadingTheZeroAddress()
	{
		TargetDouble target = new()
		{
			Memory = (_, _) => Bytes(0, 8)
		};

		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() =>
			new MemoryVtableTools(target.Dispatch).ReadVtable("2000", cancellationToken: Token));

		Assert.Equal(ToolErrorKind.InvalidArgument, exception.Error.Kind);
		Assert.Contains("null", exception.Error.Message, StringComparison.Ordinal);
		Assert.Single(target.CallsTo("Memory"));
	}

	[Fact]
	public void ReadVtable_TargetChangedDuringResolution_DoesNotReadPointers()
	{
		TargetDouble target = new();
		target.Epochs.Enqueue(1);
		target.Epochs.Enqueue(2);

		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() =>
			new MemoryVtableTools(target.Dispatch).ReadVtable("2000", cancellationToken: Token));

		Assert.Equal(ToolErrorKind.TargetChanged, exception.Error.Kind);
		Assert.Empty(target.CallsTo("Memory"));
	}

	private static MemoryBytesReadOutcome Bytes(ulong value, int width)
	{
		byte[] bytes = width == 4
			? BitConverter.GetBytes((uint) value)
			: BitConverter.GetBytes(value);
		return new MemoryBytesReadOutcome(width, [.. bytes], null);
	}
}
