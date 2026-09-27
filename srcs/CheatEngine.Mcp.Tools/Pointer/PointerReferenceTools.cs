using System.Buffers.Binary;
using System.Collections.Immutable;
using System.ComponentModel;

using CheatEngine.Client;
using CheatEngine.Client.Memory;
using CheatEngine.Client.Results;
using CheatEngine.Client.Scanning;
using CheatEngine.Mcp.Core.Contract;
using CheatEngine.Mcp.Core.Values;
using CheatEngine.SDK.Engine.Inspection;
using CheatEngine.SDK.Engine.Values;

using ModelContextProtocol.Server;

using ClientAobPattern = CheatEngine.Client.Scanning.AobPattern;

namespace CheatEngine.Mcp.Tools.Pointer;

/// <summary>Finds the holders of a pointer to an address, in a stored map or in live memory.</summary>
[McpServerToolType]
public sealed class PointerReferenceTools
{
	/// <summary>The largest distance below the target a live or map search accepts.</summary>
	internal const int MaximumOffset = 65_536;

	/// <summary>The most holders one search returns.</summary>
	internal const int MaximumLimit = 1_000;

	/// <summary>The last user-mode address of a 64-bit Windows process, the bound of a live 64-bit search.</summary>
	internal const ulong UserLast64 = 0x7FFF_FFFF_FFFF;

	private readonly ToolDispatch _dispatch;
	private readonly TargetResources _resources;
	private readonly PointerStore _store;

	/// <summary>Creates the tool; nothing touches Cheat Engine until a call.</summary>
	/// <param name="dispatch">The activation's dispatch facade.</param>
	/// <param name="store">The activation's pointer maps.</param>
	/// <param name="resources">The activation's target resources, which keep a temporary scan that could not be released.</param>
	public PointerReferenceTools(ToolDispatch dispatch, PointerStore store, TargetResources resources)
	{
		ArgumentNullException.ThrowIfNull(dispatch);
		ArgumentNullException.ThrowIfNull(store);
		ArgumentNullException.ThrowIfNull(resources);
		_dispatch = dispatch;
		_store = store;
		_resources = resources;
	}

	/// <summary>Finds the addresses that hold a pointer to a target or just below it.</summary>
	/// <param name="target">The address the pointers lead to.</param>
	/// <param name="maxOffset">The largest distance below the target.</param>
	/// <param name="mapName">A stored map to search instead of live memory.</param>
	/// <param name="module">The module that must hold the pointers.</param>
	/// <param name="writableOnly">Whether a live search keeps only writable holders.</param>
	/// <param name="limit">The most holders returned.</param>
	/// <param name="cancellationToken">The MCP request's token.</param>
	/// <returns>The holders.</returns>
	[McpServerTool(Name = CheatEngineToolNames.PointerFindReferences, Title = "Find pointer references",
		ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
	[McpMeta(McpDispatchClass.MetaKey, McpDispatchClass.HostScan)]
	[Description(
		"Finds the addresses that hold a pointer to target, or to at most maxOffset bytes below it: the holders of " +
		"an object, one level of a hand-built chain. With mapName it searches that stored map, nearest first, " +
		"without scanning. Otherwise it scans live memory once, blocking Cheat Engine while the scan runs: an " +
		"exact 4-aligned AOB scan for maxOffset 0, else a temporary value scan released before the call returns. " +
		"A symbol marks a holder inside a module image, a static root.")]
	public PointerReferenceResult FindReferences(
		[Description("The address the pointers lead to: an address, symbol or expression.")]
		string target,
		[Description("The largest distance below target a pointer may point, 0 to 65536; 0 finds exact pointers.")]
		int maxOffset = 0,
		[Description("A stored pointer map to search instead of live memory.")]
		string? mapName = null,
		[Description("Only holders inside this module image, such as game.exe.")]
		string? module = null,
		[Description("Live search only: keep holders in writable memory, where static pointers live.")]
		bool writableOnly = true,
		[Description("The most holders returned, 1 to 1000.")]
		int limit = 100,
		CancellationToken cancellationToken = default)
	{
		string targetText = PointerSupport.Expression(target, "target");
		PointerSupport.Range(maxOffset, 0, MaximumOffset, "maxOffset");
		PointerSupport.Range(limit, 1, MaximumLimit, "limit");
		string? moduleName = module is null ? null : PointerSupport.Name(module, "module");
		return mapName is null
			? FindLive(targetText, maxOffset, moduleName, writableOnly, limit, cancellationToken)
			: FindInMap(PointerSupport.Name(mapName, "mapName"), targetText, maxOffset, moduleName, limit,
				cancellationToken);
	}

	private PointerReferenceResult FindInMap(string mapName, string target, int maxOffset, string? moduleName,
		int limit, CancellationToken cancellationToken)
	{
		PointerMap map = _store.GetMap(mapName).GetUsableMap();
		PointerModule? scope = moduleName is null ? null : ScopeModule(map.Modules, moduleName);
		ulong address = PointerTargets.Resolve(_dispatch, CheatEngineToolNames.PointerFindReferences, target,
			map.Width, cancellationToken);
		List<PointerEntry> found = [];
		int count = map.FindReferences(address, maxOffset, scope, limit, found);
		PointerReference[] references =
		[
			.. found.Select(entry => new PointerReference(HexFormat.Address(entry.Address),
				HexFormat.Address(entry.Value), HexFormat.Offset(unchecked((long) (address - entry.Value))),
				Symbol(map.FindModule(entry.Address), entry.Address)))
		];
		return new PointerReferenceResult(HexFormat.Address(address), PointerReferenceSource.Map, count,
			count > references.Length, references);
	}

	private PointerReferenceResult FindLive(string target, int maxOffset, string? moduleName, bool writableOnly,
		int limit, CancellationToken cancellationToken)
	{
		return _dispatch.Run(CheatEngineToolNames.PointerFindReferences, token =>
		{
			ICheatEngineClient client = _dispatch.Client;
			int width = PointerSupport.Width(client.Processes.GetCurrentProcess(token));
			ulong address = PointerTargets.Check(PointerSupport.Resolve(client, target, token), width, "target");
			PointerModule[] modules = PointerSupport.Modules(client, null, token);
			PointerModule? scope = moduleName is null ? null : ScopeModule(modules, moduleName);
			ScanProtectionFilter protection = writableOnly
				? new ScanProtectionFilter(ScanProtectionRequirement.Unspecified,
					ScanProtectionRequirement.Unspecified, ScanProtectionRequirement.Required)
				: default;
			return maxOffset == 0
				? FindExact(client, address, width, scope, protection, limit, modules, token)
				: FindRange(client, address, maxOffset, width, scope, protection, limit, modules, token);
		}, cancellationToken);
	}

	private static PointerReferenceResult FindExact(ICheatEngineClient client, ulong address, int width,
		PointerModule? scope, ScanProtectionFilter protection, int limit, PointerModule[] modules,
		CancellationToken cancellationToken)
	{
		byte[] bytes = new byte[width];
		if (width == 8)
		{
			BinaryPrimitives.WriteUInt64LittleEndian(bytes, address);
		}
		else
		{
			BinaryPrimitives.WriteUInt32LittleEndian(bytes, (uint) address);
		}

		// A scope always runs Cheat Engine's bounded scan, whose empty result is a fact, not an indeterminate answer.
		AobScanRange? range = scope is null
			? new AobScanRange(new Address(0), new Address(width == 8 ? UserLast64 : uint.MaxValue))
			: null;
		ModuleName? module = scope is null ? null : new ModuleName(scope.Name);
		PatternScanOutcome outcome = client.Patterns.ScanDetailed(
			new AobScanRequest(new ClientAobPattern(HexFormat.Bytes(bytes)), limit, module, range, protection,
				ScanAlignment.AlignedTo(4)), cancellationToken);
		if (outcome.Result is not { } result)
		{
			throw outcome.Failure is { } failure
				? CheatEngineToolException.FromFailure(failure, client.Stopping.IsCancellationRequested)
				: CheatEngineToolException.Internal("The pointer scan returned neither a result nor a failure.");
		}

		PointerReference[] references =
		[
			.. result.Matches.Select(match => match.ToUInt64()).Order()
				.Select(holder => new PointerReference(HexFormat.Address(holder), HexFormat.Address(address), "0",
					PointerSupport.Symbol(modules, holder)))
		];
		int count = outcome.Metrics is { InBoundsCountIsExact: true } metrics
			? (int) Math.Min(metrics.ExaminedCount - metrics.FilteredOutCount, int.MaxValue)
			: references.Length;
		return new PointerReferenceResult(HexFormat.Address(address), PointerReferenceSource.Live,
			Math.Max(count, references.Length), result.IsTruncated, references);
	}

	private PointerReferenceResult FindRange(ICheatEngineClient client, ulong address, int maxOffset, int width,
		PointerModule? scope, ScanProtectionFilter protection, int limit, PointerModule[] modules,
		CancellationToken cancellationToken)
	{
		// Cheat Engine compares ordered scans of integers as signed values: a bound past the sign bit would invert.
		if (address > (width == 8 ? (ulong) long.MaxValue : int.MaxValue))
		{
			throw CheatEngineToolException.Unsupported(
				"A live range search cannot compare pointers above the target's signed range; use maxOffset 0, or search a pointer map with mapName.");
		}

		ulong low = address >= (ulong) maxOffset ? address - (ulong) maxOffset : 0;
		ValueScanFirstRequest request = width == 8
			? ValueScanFirstRequest.Between(ValueScanValue.FromInt64((long) low), ValueScanValue.FromInt64((long) address))
			: ValueScanFirstRequest.Between(ValueScanValue.FromInt32((int) low), ValueScanValue.FromInt32((int) address));
		ulong start = scope?.BaseAddress ?? 0;
		ulong stop = scope is null
			? width == 8 ? UserLast64 + 1 : 0x1_0000_0000UL
			: scope.BaseAddress + scope.Size;
		request = request.WithProtection(protection).WithAlignment(ScanAlignment.AlignedTo(4))
			.WithRange(new Address(start), new Address(stop));
		IValueScanSession session = client.ValueScans.CreateSession(cancellationToken);
		PointerReferenceResult result;
		try
		{
			session.FirstScan(request, cancellationToken);
			ValueScanPage page = session.Read(new ValueScanReadRequest(0, limit), cancellationToken);
			// Cheat Engine's start bound is not byte-exact: keep only holders inside the requested range.
			Address[] holders =
			[
				.. page.Matches.Select(static match => match.Address)
					.Where(holder => holder.ToUInt64() >= start && holder.ToUInt64() < stop)
			];
			List<PointerEntry> found = [];
			bool rereadComplete = true;
			if (holders.Length > 0)
			{
				MemoryPrimitiveBatchReadOutcome<Address> values = client.Memory.ReadPrimitiveBatchDetailed(
					new MemoryPrimitiveBatchReadRequest<Address>(holders), cancellationToken);
				ImmutableArray<Address> read = values.Values;
				rereadComplete = values.IsSuccess && values.CompletedCount == holders.Length;
				for (int index = 0; index < read.Length; index++)
				{
					ulong value = read[index].ToUInt64();
					// A holder rewritten since the scan no longer points into the range.
					if (value >= low && value <= address)
					{
						found.Add(new PointerEntry(holders[index].ToUInt64(), value));
					}
				}
			}

			// Nearest first, then by address, like a map search.
			found.Sort(static (left, right) => left.Value != right.Value
				? right.Value.CompareTo(left.Value)
				: left.Address.CompareTo(right.Address));
			PointerReference[] references =
			[
				.. found.Select(entry => new PointerReference(HexFormat.Address(entry.Address),
					HexFormat.Address(entry.Value), HexFormat.Offset((long) (address - entry.Value)),
					PointerSupport.Symbol(modules, entry.Address)))
			];
			int count = (int) Math.Min(page.ResultCount, int.MaxValue);
			result = new PointerReferenceResult(HexFormat.Address(address), PointerReferenceSource.Live,
				Math.Max(count, references.Length), page.ResultCount > (ulong) page.Matches.Length || !rereadComplete,
				references);
		}
		catch
		{
			// Keep a scan that could not be released visible, then report the original failure.
			LeaseReleaseOutcome cleanup = session.Release();
			if (!cleanup.IsComplete)
			{
				_resources.Track(session, "scan", name: CheatEngineToolNames.PointerFindReferences);
			}

			throw;
		}

		LeaseReleaseOutcome release = session.Release();
		if (!release.IsComplete)
		{
			ITargetResource kept = _resources.Track(session, "scan", name: CheatEngineToolNames.PointerFindReferences);
			throw CheatEngineToolException.PartialEffect(
				$"The references were found, but the temporary value scan could not be released; it is kept as {kept.Descriptor.Id}.",
				ToolFailureMapping.MapHostEffect(release.HostEffect),
				new PointerCleanupDetails(kept.Descriptor.Id, release.IsRetryable),
				PointerJsonContext.Default.PointerCleanupDetails, false,
				"Release it with runtime_release_resources, then repeat the search.");
		}

		return result;
	}

	private static PointerModule ScopeModule(IEnumerable<PointerModule> modules, string name)
	{
		PointerModule[] matches =
			[.. modules.Where(module => string.Equals(module.Name, name, StringComparison.OrdinalIgnoreCase))];
		return matches.Length == 1
			? matches[0]
			: throw CheatEngineToolException.NotFound(
				matches.Length == 0 ? $"No module is named {name}." : $"Several modules are named {name}.",
				"List the modules with module_list.");
	}

	private static string? Symbol(PointerModule? module, ulong address)
	{
		return module is null ? null : $"{module.Name}+{HexFormat.Address(address - module.BaseAddress)}";
	}
}
