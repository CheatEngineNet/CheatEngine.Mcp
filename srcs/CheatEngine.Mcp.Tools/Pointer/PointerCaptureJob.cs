using System.Collections.Immutable;
using System.Diagnostics;

using CheatEngine.Client;
using CheatEngine.Client.Inspection;
using CheatEngine.Client.Memory;
using CheatEngine.Client.Processes;
using CheatEngine.Client.Results;
using CheatEngine.Mcp.Core.Contract;
using CheatEngine.Mcp.Core.Jobs;
using CheatEngine.SDK.Engine.Inspection;
using CheatEngine.SDK.Engine.Values;

namespace CheatEngine.Mcp.Tools.Pointer;

/// <summary>One inclusive address range a capture reads.</summary>
/// <param name="Start">The first address.</param>
/// <param name="Last">The last address.</param>
internal readonly record struct PointerCaptureRange(ulong Start, ulong Last);

/// <summary>What a capture reads, fixed by its setup dispatch before the job starts.</summary>
/// <param name="Process">The selected process; a different selection ends the capture.</param>
/// <param name="Width">The pointer width.</param>
/// <param name="Alignment">The slot alignment.</param>
/// <param name="MaximumBytes">The byte limit.</param>
/// <param name="MaximumPointers">The pointer limit.</param>
/// <param name="Ranges">The ranges to read, in address order.</param>
/// <param name="Modules">The process's modules.</param>
/// <param name="Filter">The memory a pointer must point into.</param>
/// <param name="PlannedBytes">The bytes the capture plans to read.</param>
/// <param name="RegionsTruncated">Whether the region list hit its limit, so memory may be missing.</param>
/// <param name="ModulesTruncated">Whether the module list hit its limit, so module roots may be missing.</param>
internal sealed record PointerCapturePlan(
	ProcessSnapshot Process,
	int Width,
	int Alignment,
	long MaximumBytes,
	int MaximumPointers,
	PointerCaptureRange[] Ranges,
	PointerModule[] Modules,
	PointerValueFilter Filter,
	long PlannedBytes,
	bool RegionsTruncated,
	bool ModulesTruncated);

/// <summary>One read of a capture.</summary>
/// <param name="Address">The first address.</param>
/// <param name="Requested">The requested length.</param>
/// <param name="Bytes">The confirmed bytes.</param>
internal sealed record PointerChunk(ulong Address, int Requested, ImmutableArray<byte> Bytes);

/// <summary>
///     The pointer map capture: a setup dispatch fixes the plan, then a managed job reads it in short dispatches,
///     rechecks the selected process in each, and parses the bytes off Cheat Engine's main thread.
/// </summary>
internal static class PointerCaptureJob
{
	/// <summary>The longest single read.</summary>
	internal const int MaximumReadLength = 65_536;

	/// <summary>The most reads in one dispatch.</summary>
	internal const int MaximumReadsPerDispatch = 64;

	/// <summary>
	///     Plans a capture inside the setup dispatch: the selected process, the range and the committed, readable (and,
	///     with <paramref name="writableOnly" />, writable) regions it covers. Guard and no-access pages are never read.
	/// </summary>
	/// <param name="client">The activation's Client.</param>
	/// <param name="startAddress">The checked first-address expression, or <see langword="null" />.</param>
	/// <param name="endAddress">The checked last-address expression, or <see langword="null" />.</param>
	/// <param name="writableOnly">Whether only writable memory is read.</param>
	/// <param name="alignment">The slot alignment.</param>
	/// <param name="maximumBytes">The byte limit.</param>
	/// <param name="maximumPointers">The pointer limit.</param>
	/// <param name="cancellationToken">The dispatch body's token.</param>
	/// <returns>The plan.</returns>
	internal static PointerCapturePlan Plan(ICheatEngineClient client, string? startAddress, string? endAddress,
		bool writableOnly, int alignment, long maximumBytes, int maximumPointers, CancellationToken cancellationToken)
	{
		ProcessSnapshot process = client.Processes.GetCurrentProcess(cancellationToken);
		int width = PointerSupport.Width(process);
		ulong limit = width == 4 ? uint.MaxValue : ulong.MaxValue;
		ulong first = startAddress is null ? 0 : PointerSupport.Resolve(client, startAddress, cancellationToken);
		ulong last = endAddress is null ? limit : PointerSupport.Resolve(client, endAddress, cancellationToken);
		if (last < first || last > limit)
		{
			throw CheatEngineToolException.InvalidArgument("endAddress",
				"must not be below startAddress or beyond the target's address width.");
		}

		ImmutableArray<MemoryRegionInfo> regions =
			client.Inspection.GetMemoryRegions(new InspectionCollectionRequest(PointerSupport.RegionLimit),
				cancellationToken);
		List<(ulong Start, ulong Last)> readable = [];
		List<PointerCaptureRange> ranges = [];
		foreach (MemoryRegionInfo region in regions.OrderBy(static region => region.BaseAddress.ToUInt64()))
		{
			ulong size = region.Size.Value;
			if (region.State != MemoryRegionState.Committed || size == 0 || !IsReadable(region))
			{
				continue;
			}

			ulong start = region.BaseAddress.ToUInt64();
			ulong regionLast = start > ulong.MaxValue - (size - 1) ? ulong.MaxValue : start + (size - 1);
			readable.Add((start, regionLast));
			if (writableOnly && !IsWritable(region))
			{
				continue;
			}

			ulong clippedStart = Math.Max(start, first);
			ulong clippedLast = Math.Min(regionLast, last);
			if (clippedStart <= clippedLast && clippedLast - clippedStart >= (ulong) (width - 1))
			{
				ranges.Add(new PointerCaptureRange(clippedStart, clippedLast));
			}
		}

		PointerModule[] modules = PointerSupport.Modules(client, regions, cancellationToken, out bool modulesTruncated);
		return new PointerCapturePlan(process, width, alignment, maximumBytes, maximumPointers, [.. ranges], modules,
			new PointerValueFilter(readable), CalculatePlannedBytes(ranges, width, maximumBytes),
			regions.Length >= PointerSupport.RegionLimit, modulesTruncated);
	}

	/// <summary>
	///     The capture job's work: reads the plan chunk by chunk, parses each chunk off Cheat Engine's main thread, and
	///     stores the map, or what was read before a stop, in its slot.
	/// </summary>
	/// <param name="dispatch">The activation's dispatch facade.</param>
	/// <param name="plan">The plan.</param>
	/// <param name="map">The map's slot.</param>
	/// <param name="writer">The job's progress writer.</param>
	/// <param name="slice">How long one dispatch may read.</param>
	/// <param name="expires">When the job's TTL ends.</param>
	/// <param name="time">The clock of the TTL.</param>
	/// <param name="cancellationToken">The job's token.</param>
	/// <returns>The work.</returns>
	internal static async Task RunAsync(ToolDispatch dispatch, PointerCapturePlan plan, PointerMapSlot map,
		JobWriter<int> writer, TimeSpan slice, DateTimeOffset expires, TimeProvider time,
		CancellationToken cancellationToken)
	{
		PointerEntryBuffer entries = new();
		PointerCaptureCursor cursor = new(plan);
		long bytesRead = 0;
		long unreadableBytes = 0;
		bool limited = false;
		try
		{
			while (!cursor.Done)
			{
				PointerChunk[] chunks = await PointerJobs.DispatchAsync(dispatch,
					CheatEngineToolNames.PointerCreateMap, token => Read(dispatch.Client, plan, cursor, slice, token),
					cancellationToken).ConfigureAwait(false);
				foreach (PointerChunk chunk in chunks)
				{
					bytesRead += chunk.Bytes.Length;
					unreadableBytes += chunk.Requested - chunk.Bytes.Length;
					PointerMap.CopyEntries(entries, chunk.Address, chunk.Bytes.AsSpan(), plan.Width, plan.Alignment,
						plan.MaximumPointers, plan.Filter);
				}

				map.Report(cursor.Attempted, bytesRead, unreadableBytes, entries.Count);
				writer.Progress(cursor.Attempted, plan.PlannedBytes);
				if (entries.Count >= plan.MaximumPointers)
				{
					limited = true;
					break;
				}

				// Leave Cheat Engine's main thread to its own work between two chunks.
				await Task.Yield();
			}

			bool incomplete = limited || cursor.Limited || unreadableBytes != 0 || plan.RegionsTruncated ||
							  plan.ModulesTruncated;
			map.Complete(new PointerMap(plan.Process.Id.Value, plan.Width, entries.ToArray(), plan.Modules,
				incomplete, bytesRead, unreadableBytes));
		}
		catch (Exception exception)
		{
			(PointerJobState state, string message) = PointerJobs.Classify(exception, dispatch.Client,
				time.GetUtcNow() >= expires, cancellationToken);
			if (state is PointerJobState.Stopped or PointerJobState.Expired)
			{
				int processId = plan.Process.Id.Value;
				long read = bytesRead;
				long unreadable = unreadableBytes;
				map.Interrupt(state, message,
					() => new PointerMap(processId, plan.Width, entries.ToArray(), plan.Modules, true, read,
						unreadable),
					entries.Count);
			}
			else
			{
				map.Fail(state, message);
			}

			throw;
		}
	}

	private static PointerChunk[] Read(ICheatEngineClient client, PointerCapturePlan plan, PointerCaptureCursor cursor,
		TimeSpan slice, CancellationToken cancellationToken)
	{
		if (client.Processes.GetCurrentProcess(cancellationToken) != plan.Process)
		{
			throw PointerSupport.TargetChanged(
				"The selected process changed during the capture; the pointer map was discarded.");
		}

		long started = Stopwatch.GetTimestamp();
		List<PointerChunk> chunks = [];
		// A stop ends the dispatch early but keeps what it read: the job observes the stop right after.
		while (chunks.Count < MaximumReadsPerDispatch && !cancellationToken.IsCancellationRequested &&
			   cursor.TryNext(out ulong address, out int length))
		{
			MemoryBytesReadOutcome read =
				client.Memory.ReadBytesDetailed(new MemoryBytesReadRequest(new Address(address), length),
					cancellationToken);
			if (read.Failure is { Kind: CheatEngineFailureKind.Cancelled } && chunks.Count > 0)
			{
				break;
			}

			if (read.Failure is { Kind: not CheatEngineFailureKind.MemoryReadFailed } failure)
			{
				throw CheatEngineToolException.FromFailure(failure, client.Stopping.IsCancellationRequested);
			}

			chunks.Add(new PointerChunk(address, length, read.Bytes));
			cursor.Advance(address, length, read.IsSuccess);
			if (Stopwatch.GetElapsedTime(started) >= slice)
			{
				break;
			}
		}

		return [.. chunks];
	}

	private static bool IsReadable(MemoryRegionInfo region)
	{
		// Never touch guard (0x100) or no-access (0x01) pages: reading one can change the target's behavior.
		uint protection = (uint) region.Protection;
		return (protection & 0x101) == 0 && (protection & 0xEE) != 0;
	}

	private static bool IsWritable(MemoryRegionInfo region)
	{
		// Read-write, write-copy and their executable forms.
		return ((uint) region.Protection & 0xCC) != 0;
	}

	private static long CalculatePlannedBytes(IEnumerable<PointerCaptureRange> ranges, int width, long maximumBytes)
	{
		long attempted = 0;
		foreach (PointerCaptureRange range in ranges)
		{
			ulong address = range.Start;
			while (address <= range.Last && range.Last - address >= (ulong) (width - 1))
			{
				long remaining = maximumBytes - attempted;
				ulong span = Math.Min(range.Last - address, MaximumReadLength - 1) + 1;
				int length = (int) Math.Min(span, (ulong) Math.Max(remaining, 0));
				if (length < width)
				{
					return attempted;
				}

				attempted += length;
				if (address + (ulong) (length - 1) >= range.Last)
				{
					break;
				}

				address += (ulong) (length - width + 1);
			}
		}

		return attempted;
	}
}

/// <summary>Where a capture stands in its plan; only the capture job moves it, one dispatch at a time.</summary>
internal sealed class PointerCaptureCursor
{
	private readonly PointerCapturePlan _plan;
	private ulong _address;
	private int _range;

	internal PointerCaptureCursor(PointerCapturePlan plan)
	{
		_plan = plan;
		_address = plan.Ranges.Length > 0 ? plan.Ranges[0].Start : 0;
	}

	/// <summary>The bytes requested so far, the unit of the capture's progress.</summary>
	internal long Attempted
	{
		get;
		private set;
	}

	/// <summary>Whether the byte limit stopped the capture before its plan ended.</summary>
	internal bool Limited
	{
		get;
		private set;
	}

	/// <summary>Whether the plan is read.</summary>
	internal bool Done => _range >= _plan.Ranges.Length;

	/// <summary>The next read, if any.</summary>
	/// <param name="address">Its first address.</param>
	/// <param name="length">Its length, at least one pointer wide.</param>
	/// <returns><see langword="false" /> once the plan is read or the byte limit is reached.</returns>
	internal bool TryNext(out ulong address, out int length)
	{
		int width = _plan.Width;
		while (_range < _plan.Ranges.Length)
		{
			PointerCaptureRange range = _plan.Ranges[_range];
			if (_address > range.Last || range.Last - _address < (ulong) (width - 1))
			{
				NextRange();
				continue;
			}

			long remaining = _plan.MaximumBytes - Attempted;
			ulong span = Math.Min(range.Last - _address, PointerCaptureJob.MaximumReadLength - 1) + 1;
			length = (int) Math.Min(span, (ulong) Math.Max(remaining, 0));
			if (length < width)
			{
				Limited = true;
				_range = _plan.Ranges.Length;
				break;
			}

			address = _address;
			return true;
		}

		address = 0;
		length = 0;
		return false;
	}

	/// <summary>Moves past a read.</summary>
	/// <param name="address">The read's first address.</param>
	/// <param name="length">The read's length.</param>
	/// <param name="complete">Whether every byte was read.</param>
	internal void Advance(ulong address, int length, bool complete)
	{
		Attempted += length;
		PointerCaptureRange range = _plan.Ranges[_range];
		// Overlap a complete read by one pointer minus a byte, so a pointer that straddles two reads is still found.
		ulong advance = complete && address + (ulong) (length - 1) < range.Last
			? (ulong) (length - _plan.Width + 1)
			: (ulong) length;
		if (address > ulong.MaxValue - advance)
		{
			NextRange();
			return;
		}

		_address = address + advance;
	}

	private void NextRange()
	{
		_range++;
		if (_range < _plan.Ranges.Length)
		{
			_address = _plan.Ranges[_range].Start;
		}
	}
}
