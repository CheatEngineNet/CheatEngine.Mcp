using System.Buffers.Binary;
using System.Collections.Immutable;
using System.ComponentModel;

using CheatEngine.Client;
using CheatEngine.Client.Memory;
using CheatEngine.Client.Results;
using CheatEngine.Mcp.Core.Contract;
using CheatEngine.Mcp.Core.Values;
using CheatEngine.SDK.Engine.Values;

using ModelContextProtocol.Server;

namespace CheatEngine.Mcp.Tools.Memory;

/// <summary>The read-only, explicitly bounded vtable inspection tool.</summary>
[McpServerToolType]
public sealed class MemoryVtableTools
{
	/// <summary>The most vtable slots one request reads.</summary>
	internal const int MaximumCount = 128;

	/// <summary>The highest accepted first vtable slot index.</summary>
	internal const int MaximumStartIndex = 4095;

	private readonly ToolDispatch _dispatch;

	/// <summary>Creates the tool; the constructor does no Cheat Engine work.</summary>
	/// <param name="dispatch">The activation's dispatch facade.</param>
	public MemoryVtableTools(ToolDispatch dispatch)
	{
		ArgumentNullException.ThrowIfNull(dispatch);
		_dispatch = dispatch;
	}

	/// <summary>Reads a fixed, explicit range of function pointers from a native C++ vtable.</summary>
	[McpServerTool(Name = CheatEngineToolNames.MemoryReadVtable, Title = "Read a vtable", ReadOnly = true,
		Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
	[McpMeta(McpDispatchClass.MetaKey, McpDispatchClass.Short)]
	[Description(
		"Read a fixed range of native vtable slots without invoking them. By default address is an object or subobject and its first pointer is read as the vtable; set addressIsVtable when address already is the table. The target's x86 or x64 pointer width determines every slot. This does not infer a table's extent or verify RTTI or a class. An unreadable slot reports its own error while later slots are still read; a null slot is returned as a pointer value.")]
	public MemoryVtableResult ReadVtable(
		[Description("An object or subobject address, or a direct vtable address when addressIsVtable is true.")]
		string address,
		[Description("Whether address is already the vtable instead of an object whose first pointer is the vtable.")]
		bool addressIsVtable = false,
		[Description("The zero-based index of the first vtable slot to read, 0 to 4095.")]
		int startIndex = 0,
		[Description("The number of consecutive vtable slots to read, 1 to 128.")]
		int count = 16,
		CancellationToken cancellationToken = default)
	{
		string expression = MemoryTargets.RequireExpression(address, "address");
		MemoryTargets.RequireRange(startIndex, "startIndex", 0, MaximumStartIndex);
		MemoryTargets.RequireRange(count, "count", 1, MaximumCount);

		return _dispatch.Run(CheatEngineToolNames.MemoryReadVtable, token =>
		{
			ICheatEngineClient client = _dispatch.Client;
			long epoch = MemoryTargets.SelectionEpoch(client, token);
			Address requested = MemoryTargets.Resolve(client, expression, "address", token);
			MemoryTargets.RequireSameTarget(client, epoch, CheatEngineToolNames.MemoryReadVtable, token);
			int pointerBytes = MemoryTargets.PointerBytes(client, token);
			MemoryTargets.RequireSameTarget(client, epoch, CheatEngineToolNames.MemoryReadVtable, token);
			ulong requestedValue = RequireTargetAddress(requested.ToUInt64(), pointerBytes, "address");
			ulong table = addressIsVtable
				? requestedValue
				: ReadObjectVtable(client, requestedValue, pointerBytes, epoch, token);
			ValidateSlotRange(table, pointerBytes, startIndex, count);

			MemoryVtableSlot[] slots = new MemoryVtableSlot[count];
			for (int offset = 0; offset < count; offset++)
			{
				token.ThrowIfCancellationRequested();
				int index = startIndex + offset;
				ulong slot = table + ((ulong) index * (uint) pointerBytes);
				slots[offset] = ReadSlot(client, slot, index, pointerBytes, epoch, token);
			}

			return new MemoryVtableResult(HexFormat.Address(requested), HexFormat.Address(table), pointerBytes,
				startIndex, slots);
		}, cancellationToken);
	}

	private static ulong ReadObjectVtable(ICheatEngineClient client, ulong address, int pointerBytes, long epoch,
		CancellationToken cancellationToken)
	{
		EnsureFits(address, pointerBytes, "address");
		MemoryBytesReadOutcome outcome = client.Memory.ReadBytesDetailed(
			new MemoryBytesReadRequest(new Address(address), pointerBytes), cancellationToken);
		MemoryTargets.RequireSameTarget(client, epoch, CheatEngineToolNames.MemoryReadVtable, cancellationToken);
		if (!outcome.IsSuccess)
		{
			throw CheatEngineToolException.FromFailure(outcome.Failure!.Value, client.Stopping.IsCancellationRequested);
		}

		ulong table = Pointer(outcome.Bytes, pointerBytes);
		return table == 0
			? throw CheatEngineToolException.InvalidArgument("address",
				"points to an object whose first pointer is null, so it has no vtable to read.")
			: table;
	}

	private static MemoryVtableSlot ReadSlot(ICheatEngineClient client, ulong slot, int index, int pointerBytes,
		long epoch, CancellationToken cancellationToken)
	{
		MemoryBytesReadOutcome outcome = client.Memory.ReadBytesDetailed(
			new MemoryBytesReadRequest(new Address(slot), pointerBytes), cancellationToken);
		MemoryTargets.RequireSameTarget(client, epoch, CheatEngineToolNames.MemoryReadVtable, cancellationToken);
		if (!outcome.IsSuccess)
		{
			CheatEngineFailure failure = outcome.Failure!.Value;
			return failure.Kind is not CheatEngineFailureKind.MemoryReadFailed and not CheatEngineFailureKind.NotFound
				? throw CheatEngineToolException.FromFailure(failure, client.Stopping.IsCancellationRequested)
				: new MemoryVtableSlot(index, HexFormat.Address(slot), Error: MemoryTargets.ItemError(client, failure));
		}

		ulong function = Pointer(outcome.Bytes, pointerBytes);
		return new MemoryVtableSlot(index, HexFormat.Address(slot), HexFormat.Address(function),
			function == 0 ? null : Symbol(client, function, epoch, cancellationToken));
	}

	private static string? Symbol(ICheatEngineClient client, ulong address, long epoch, CancellationToken cancellationToken)
	{
		if (client.Inspection.TryResolveName(new Address(address), out string? name, out CheatEngineFailure failure,
				cancellationToken))
		{
			MemoryTargets.RequireSameTarget(client, epoch, CheatEngineToolNames.MemoryReadVtable, cancellationToken);
			return string.IsNullOrWhiteSpace(name) || (HexParse.TryAddress(name, out ulong echoed) && echoed == address)
				? null
				: name;
		}

		MemoryTargets.RequireSameTarget(client, epoch, CheatEngineToolNames.MemoryReadVtable, cancellationToken);
		return failure.Kind is CheatEngineFailureKind.TargetNotAttached or CheatEngineFailureKind.TargetChanged
			or CheatEngineFailureKind.TargetIdentityUnavailable or CheatEngineFailureKind.ActivationExpired
			or CheatEngineFailureKind.Cancelled or CheatEngineFailureKind.RuntimeChanged
			? throw CheatEngineToolException.FromFailure(failure, client.Stopping.IsCancellationRequested)
			: null;
	}

	private static ulong RequireTargetAddress(ulong address, int pointerBytes, string parameter)
	{
		ulong maximum = pointerBytes == 4 ? uint.MaxValue : ulong.MaxValue;
		return address <= maximum
			? address
			: throw CheatEngineToolException.InvalidArgument(parameter,
				"resolves beyond the target's pointer-sized address space.");
	}

	private static void ValidateSlotRange(ulong table, int pointerBytes, int startIndex, int count)
	{
		ulong maximum = pointerBytes == 4 ? uint.MaxValue : ulong.MaxValue;
		ulong firstOffset = (ulong) startIndex * (uint) pointerBytes;
		ulong lastOffset = (ulong) (startIndex + count - 1) * (uint) pointerBytes;
		if (table > maximum - firstOffset || table > maximum - lastOffset ||
			table + lastOffset > maximum - ((uint) pointerBytes - 1))
		{
			throw CheatEngineToolException.InvalidArgument("startIndex",
				"and count place a vtable slot beyond the target's pointer-sized address space.");
		}
	}

	private static void EnsureFits(ulong address, int pointerBytes, string parameter)
	{
		ulong maximum = pointerBytes == 4 ? uint.MaxValue : ulong.MaxValue;
		if (address > maximum - ((uint) pointerBytes - 1))
		{
			throw CheatEngineToolException.InvalidArgument(parameter,
				"does not leave room for one target-sized pointer.");
		}
	}

	private static ulong Pointer(ImmutableArray<byte> bytes, int pointerBytes)
	{
		return bytes.Length != pointerBytes
			? throw CheatEngineToolException.InvalidState(
				"Cheat Engine reported a successful pointer read with an unexpected byte count.")
			: pointerBytes == 4
			? BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan())
			: BinaryPrimitives.ReadUInt64LittleEndian(bytes.AsSpan());
	}
}
