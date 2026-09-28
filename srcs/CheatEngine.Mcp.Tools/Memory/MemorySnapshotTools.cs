using System.ComponentModel;
using System.Numerics;

using CheatEngine.Client;
using CheatEngine.Client.Processes;
using CheatEngine.Mcp.Core.Contract;
using CheatEngine.Mcp.Core.Values;
using CheatEngine.SDK.Engine.Values;

using ModelContextProtocol.Server;

namespace CheatEngine.Mcp.Tools.Memory;

/// <summary>
///     Takes, compares, lists and deletes named memory snapshots: copies of a target range held in this activation's
///     managed memory (<see cref="MemorySnapshotStore" />), to find which fields of a structure or range changed
///     between two moments.
/// </summary>
[McpServerToolType]
public sealed class MemorySnapshotTools
{
	/// <summary>The most changes one <c>memory_compare_snapshot</c> page returns.</summary>
	internal const int MaximumLimit = 1000;

	private readonly ToolDispatch _dispatch;
	private readonly MemorySnapshotStore _store;
	private readonly TimeProvider _time;

	/// <summary>Creates the tools; the constructor does no Cheat Engine work.</summary>
	/// <param name="dispatch">The activation's dispatch facade.</param>
	/// <param name="store">The activation's snapshots.</param>
	/// <param name="time">The clock that dates snapshots.</param>
	public MemorySnapshotTools(ToolDispatch dispatch, MemorySnapshotStore store, TimeProvider time)
	{
		ArgumentNullException.ThrowIfNull(dispatch);
		ArgumentNullException.ThrowIfNull(store);
		ArgumentNullException.ThrowIfNull(time);
		_dispatch = dispatch;
		_store = store;
		_time = time;
	}

	/// <summary>Copies a target range into a named snapshot.</summary>
	[McpServerTool(Name = CheatEngineToolNames.MemoryCreateSnapshot, Title = "Create a memory snapshot",
		ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false, UseStructuredContent = true)]
	[McpMeta(McpDispatchClass.MetaKey, McpDispatchClass.Short)]
	[Description(
		"Copy up to 16 MiB of target memory into a named snapshot that this activation holds in managed memory (not a target resource; at most 16 snapshots and 64 MiB in total, else limit_exceeded; a taken name fails with invalid_state). Reads 1 MiB per Cheat Engine dispatch and fails with target_changed if Cheat Engine selects another process in between. Unreadable memory is stored as zeros, listed, and skipped by comparisons. Take a snapshot, act in the target, then call memory_compare_snapshot. Snapshots survive a process switch, but compare with live memory only while the same target stays selected; free them with memory_delete_snapshot.")]
	public MemorySnapshotInfo CreateSnapshot(
		[Description("The new snapshot's name, 1 to 64 characters of A-Z, a-z, 0-9, '_', '.' or '-'.")]
		string name,
		[Description("An address or Cheat Engine address expression, such as game.exe+1C or [player]+10.")]
		string address,
		[Description("The number of bytes to copy, 1 to 16777216.")]
		int size,
		CancellationToken cancellationToken = default)
	{
		string snapshotName = MemorySnapshotStore.RequireName(name, "name");
		string expression = MemoryTargets.RequireExpression(address, "address");
		MemoryTargets.RequireRange(size, "size", 1, MemorySnapshotStore.MaximumSnapshotBytes);
		_store.Reserve(snapshotName, size);
		try
		{
			(Address target, ProcessSnapshot process, MemoryFileTools.Chunk first) = _dispatch.Run(
				CheatEngineToolNames.MemoryCreateSnapshot, token =>
				{
					ICheatEngineClient client = _dispatch.Client;
					Address resolved = MemoryTargets.Resolve(client, expression, "address", token);
					if (resolved.ToUInt64() > ulong.MaxValue - (ulong) (size - 1))
					{
						throw CheatEngineToolException.InvalidArgument("size",
							"the range would cross the end of the address space.");
					}

					ProcessSnapshot current = client.Processes.GetCurrentProcess(token);
					return (resolved, current, MemoryFileTools.ReadChunk(client, resolved,
						Math.Min(size, MemoryTargets.ChunkBytes), true, token));
				}, cancellationToken);
			MemoryImage image = ReadImage(CheatEngineToolNames.MemoryCreateSnapshot, target, process.SelectionEpoch,
				size, first, cancellationToken);
			MemorySnapshot snapshot = new(snapshotName, target.ToUInt64(), process.Id.Value, process.SelectionEpoch,
				process.Bitness.IsKnown ? process.Bitness.Bytes : null, _time.GetUtcNow(), image);
			_store.Commit(snapshot);
			return snapshot.Describe();
		}
		catch
		{
			_store.Cancel(snapshotName);
			throw;
		}
	}

	/// <summary>Compares a snapshot with another snapshot or with live memory, slot by slot.</summary>
	[McpServerTool(Name = CheatEngineToolNames.MemoryCompareSnapshot, Title = "Compare a memory snapshot",
		ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
	[McpMeta(McpDispatchClass.MetaKey, McpDispatchClass.Short)]
	[Description(
		"Compare a snapshot slot by slot, reading each slot as valueType every alignment bytes, with another snapshot of the same size (compareTo: no Cheat Engine call; the addresses may differ, so two objects compare field by field) or with live memory at the snapshot's address (compareTo omitted: 1 MiB per dispatch; target_changed once Cheat Engine selected another target since the snapshot). change keeps changed or unchanged slots by their bytes, or increased or decreased slots by numeric order (NaN never matches). Slots touching unreadable bytes on either side are skipped and counted. A live comparison reads memory again on every call, so its pages can shift; for stable paging, take a second snapshot and compare the two.")]
	public MemorySnapshotDiff CompareSnapshot(
		[Description("The snapshot to compare; its bytes are the before values.")]
		string name,
		[Description(
			"Another snapshot of the same size whose bytes are the after values; omit it to compare with live memory.")]
		string? compareTo = null,
		[Description("The type each slot is read as: int8 to uint64, float, double or pointer (default int32).")]
		FixedValueType valueType = FixedValueType.Int32,
		[Description(
			"The distance between two slots from offset 0: 1, 2, 4 or 8, at most the size of valueType (default that size).")]
		int? alignment = null,
		[Description(
			"The slots to keep: changed (default) or unchanged by bytes, increased or decreased by numeric order.")]
		SnapshotChangeFilter change = SnapshotChangeFilter.Changed,
		[Description("The zero-based index of the first matching slot to return.")]
		int offset = 0,
		[Description("The most matching slots to return, 1 to 1000.")]
		int limit = 100,
		CancellationToken cancellationToken = default)
	{
		string snapshotName = MemorySnapshotStore.RequireName(name, "name");
		string? otherName = compareTo is null ? null : MemorySnapshotStore.RequireName(compareTo, "compareTo");
		McpValueType type = MemoryTargets.RequireFixedType(valueType, "valueType");
		if (!Enum.IsDefined(change))
		{
			throw CheatEngineToolException.InvalidArgument("change",
				"must be changed, unchanged, increased or decreased.");
		}

		_ = Paging.Slice(Array.Empty<SnapshotChange>(), offset, limit, MaximumLimit);
		MemorySnapshot snapshot = _store.Get(snapshotName, "name");
		MemorySnapshot? other = otherName is null ? null : _store.Get(otherName, "compareTo");
		SnapshotSlots shape = Shape(snapshot, other, type, alignment);
		if (other is not null && other.Size != snapshot.Size)
		{
			throw CheatEngineToolException.InvalidArgument("compareTo",
				$"holds {other.Size} bytes but {snapshot.Name} holds {snapshot.Size}; compare snapshots of the same size.");
		}

		MemoryImage after = other?.Image ?? ReadLive(snapshot, cancellationToken);
		SnapshotMatches matches = MemorySnapshotComparer.Compare(snapshot.Image, after, shape, change,
			snapshot.Address, offset, limit);
		return new MemorySnapshotDiff(snapshot.Name, HexFormat.Address(snapshot.Address), valueType, shape.Alignment,
			change, matches.Compared, matches.Skipped, matches.Total, matches.Page, other?.Name, matches.NextOffset);
	}

	/// <summary>Lists the snapshots without any Cheat Engine call.</summary>
	[McpServerTool(Name = CheatEngineToolNames.MemoryListSnapshots, Title = "List memory snapshots", ReadOnly = true,
		Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
	[McpMeta(McpDispatchClass.MetaKey, McpDispatchClass.Short)]
	[Description(
		"List this activation's memory snapshots, oldest first, with their address, size, process, selection epoch and unreadable ranges, and the bytes they hold against the 64 MiB limit, without any Cheat Engine call.")]
	public MemorySnapshotList ListSnapshots()
	{
		return new MemorySnapshotList([.. _store.List().Select(static snapshot => snapshot.Describe())],
			_store.TotalBytes, MemorySnapshotStore.MaximumTotalBytes, MemorySnapshotStore.MaximumSnapshots);
	}

	/// <summary>Deletes a snapshot without any Cheat Engine call.</summary>
	[McpServerTool(Name = CheatEngineToolNames.MemoryDeleteSnapshot, Title = "Delete a memory snapshot",
		ReadOnly = false, Destructive = true, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
	[McpMeta(McpDispatchClass.MetaKey, McpDispatchClass.Short)]
	[Description(
		"Delete a memory snapshot and free its managed memory, without any Cheat Engine call. An unknown name fails with not_found.")]
	public MemorySnapshotDeleted DeleteSnapshot(
		[Description("The snapshot to delete.")]
		string name)
	{
		MemorySnapshot snapshot = _store.Remove(MemorySnapshotStore.RequireName(name, "name"));
		return new MemorySnapshotDeleted(snapshot.Name, snapshot.Size);
	}

	/// <summary>Checks the slot shape against the snapshots, before any dispatch.</summary>
	private static SnapshotSlots Shape(MemorySnapshot snapshot, MemorySnapshot? other, McpValueType valueType,
		int? alignment)
	{
		int pointerBytes = 8;
		if (valueType is McpValueType.Pointer)
		{
			pointerBytes = snapshot.PointerBytes ?? throw CheatEngineToolException.Unsupported(
				$"Cheat Engine did not report the pointer size of the target of {snapshot.Name}, so its pointers cannot be sized.",
				CheatEngineToolNames.MemoryCompareSnapshot);
			if (other is not null && other.PointerBytes != pointerBytes)
			{
				throw CheatEngineToolException.InvalidArgument("compareTo",
					"was taken from a target with another pointer size; compare its pointers as uint32 or uint64.");
			}
		}

		int width = McpValueCodec.FixedSize(valueType, pointerBytes)!.Value;
		int step = alignment ?? width;
		return step >= 1 && step <= width && BitOperations.IsPow2(step)
			? new SnapshotSlots(valueType, width, step, pointerBytes)
			: throw CheatEngineToolException.InvalidArgument("alignment",
				$"must be 1, 2, 4 or 8 and at most {width}, the size of valueType in bytes.");
	}

	/// <summary>Reads the snapshot's range from live memory, in the snapshot's target-selection epoch.</summary>
	private MemoryImage ReadLive(MemorySnapshot snapshot, CancellationToken cancellationToken)
	{
		Address start = new(snapshot.Address);
		MemoryFileTools.Chunk first = _dispatch.Run(CheatEngineToolNames.MemoryCompareSnapshot, token =>
		{
			ICheatEngineClient client = _dispatch.Client;
			if (MemoryTargets.SelectionEpoch(client, token) != snapshot.SelectionEpoch)
			{
				throw new CheatEngineToolException(new ToolError(ToolErrorKind.TargetChanged,
					$"Cheat Engine selected another target since the snapshot {snapshot.Name} was taken, so it cannot be compared with live memory.",
					CheatEngineToolNames.MemoryCompareSnapshot, ToolHostEffect.NotApplied, false,
					"Compare it with another snapshot through compareTo, or take a new snapshot of the selected target."));
			}

			return MemoryFileTools.ReadChunk(client, start, Math.Min(snapshot.Size, MemoryTargets.ChunkBytes), true,
				token);
		}, cancellationToken);
		return ReadImage(CheatEngineToolNames.MemoryCompareSnapshot, start, snapshot.SelectionEpoch, snapshot.Size,
			first, cancellationToken);
	}

	/// <summary>Reads the chunks after the first, one dispatch each, refusing a changed target between them.</summary>
	private MemoryImage ReadImage(string operation, Address start, long epoch, int size, MemoryFileTools.Chunk first,
		CancellationToken cancellationToken)
	{
		return MemoryImage.Read(size, first, (offset, length) => _dispatch.Run(operation, token =>
		{
			ICheatEngineClient client = _dispatch.Client;
			MemoryTargets.RequireSameTarget(client, epoch, operation, token);
			return MemoryFileTools.ReadChunk(client, start + offset, length, true, token);
		}, cancellationToken));
	}
}
