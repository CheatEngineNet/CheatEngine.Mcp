using System.ComponentModel;

using CheatEngine.Client.Results;
using CheatEngine.Client.Scanning;
using CheatEngine.Mcp.Core.Contract;
using CheatEngine.Mcp.Core.Values;
using CheatEngine.Mcp.Tools.Memory;

using ModelContextProtocol.Server;

namespace CheatEngine.Mcp.Tools.Scan;

/// <summary>UI and independent Client value scans with bounded result pages and explicit lifecycle operations.</summary>
/// <remarks>
///     <para>
///         <c>main</c> drives Cheat Engine's visible scanner through fixed Lua (<see cref="ScanScripts" />): its scans run
///         asynchronously with Cheat Engine's own range, protection, fast-scan and rounding settings. Any other scanner
///         name is an independent Client session whose first and next scans run synchronously on Cheat Engine's main
///         thread, which is why <c>scan_first</c> and <c>scan_next</c> are <see cref="McpDispatchClass.HostScan" />.
///     </para>
///     <para>
///         Every first scan runs through the activation's <see cref="MappedMemoryOverride" />: a named one with
///         <c>includeMapped</c> inside Cheat Engine's <c>MEM_MAPPED</c> scan override, which is Cheat Engine-wide and
///         which the same dispatch ends on every path, and any other outside it. A first scan that runs nested in
///         another scan's wait is refused as <c>busy</c> when the two disagree on <c>includeMapped</c>.
///     </para>
///     <para>
///         Arguments are checked by <see cref="ScanCriteria" /> and <see cref="NamedScanOptions" /> before any Cheat
///         Engine call, so a refused argument is always <c>invalid_argument</c> with <c>not_started</c>.
///     </para>
/// </remarks>
[McpServerToolType]
public sealed class ScanTools : IDisposable
{
	private const int MaximumScanners = 32;
	private const int MaximumScannerNameLength = 256;
	private const int MaximumResults = 1024;
	private const string MainScanner = "main";
	private const string MissingMessage = "No scan exists with that scannerName.";
	private const string MissingHint = "List the scanners with scan_list_scanners; scan_first creates a named one.";

	// The Client refuses the release after a target change and cannot begin it after a Lua runtime change, so
	// scan_delete reports partial_effect and the name stays taken until that is resolved.
	private const string ChangedReleaseHint =
		"Release it with scan_delete and follow its hint if the release is incomplete; a new scan can start at once " +
		"under another scannerName.";

	private const string NoResultsHint =
		"Run scan_first on it; when its state is Invalidated, call scan_reset first, or scan_delete when the reset " +
		"is refused.";

	internal const string MainScannerBusyMessage =
		"The main UI scanner is busy. Wait for it to finish or stop it with scan_stop before switching targets.";

	private readonly ToolDispatch _dispatch;
	private readonly MappedMemoryOverride _mappedMemory;
	private readonly TargetResources _resources;
	private readonly Dictionary<string, IValueScanSession> _sessions = new(StringComparer.Ordinal);
	private readonly Dictionary<string, ValueScanValueType> _valueTypes = new(StringComparer.Ordinal);
	private bool _disposed;

	/// <summary>Creates a scan container without opening a target or starting a scanner.</summary>
	/// <param name="dispatch">The activation's dispatch facade.</param>
	/// <param name="resources">The activation's target resources, which track the named sessions.</param>
	/// <param name="mappedMemory">
	///     The activation's <c>MEM_MAPPED</c> override owner, shared with the AOB scans; without one, this container
	///     keeps its own.
	/// </param>
	public ScanTools(ToolDispatch dispatch, TargetResources resources, MappedMemoryOverride? mappedMemory = null)
	{
		ArgumentNullException.ThrowIfNull(dispatch);
		ArgumentNullException.ThrowIfNull(resources);
		_dispatch = dispatch;
		_resources = resources;
		_mappedMemory = mappedMemory ?? new MappedMemoryOverride(dispatch);
	}

	/// <inheritdoc />
	public void Dispose()
	{
		if (_disposed)
		{
			return;
		}

		_disposed = true;
		foreach ((string name, IValueScanSession session) in _sessions.ToArray())
		{
			if (!session.Release().IsRetryable)
			{
				_resources.Forget(session);
				Remove(name);
			}
		}
	}

	/// <summary>Starts a first value scan.</summary>
	[McpServerTool(Name = CheatEngineToolNames.ScanFirst, Title = "Start value scan", ReadOnly = false,
		Destructive = true, Idempotent = false, OpenWorld = false, UseStructuredContent = true)]
	[McpMeta(McpDispatchClass.MetaKey, McpDispatchClass.HostScan)]
	[Description(
		"Start a first value scan. main drives Cheat Engine's visible scanner: it returns at once with state Scanning (poll scan_get_status), uses Cheat Engine's own range, protection, fast-scan and rounding settings, which scan_get_status reports, and needs scan_reset before another first scan. Any other scannerName is an independent session that never touches the UI; its scan runs synchronously and blocks Cheat Engine until it ends, which takes seconds over the whole address space or for unknown, so scope it with startAddress/endAddress, writable=required and alignment. Cheat Engine skips mapped memory, such as emulator guest RAM, unless its MEM_MAPPED setting is ticked; includeMapped adds it to one named scan. Integers are decimal; float and double values are written with floatDecimals decimals (default 6 and 12), which sets how close an exact match must be, so use between for rounded, displayed floats.")]
	public ScanState First(
		[Description("main (default) for the visible Cheat Engine scanner, or a unique independent name.")]
		string scannerName = MainScanner,
		[Description("byte, int16, int32 (default), int64, float, double, string, wstring or bytes.")]
		string valueType = "int32",
		[Description(
			"Value for exact, between, greater or less; omit for unknown. Integers are decimal, bytes are hex pairs such as 48 8B 05.")]
		string? value = null,
		[Description("exact, unknown, between, greater or less; string, wstring and bytes support exact only.")]
		string comparison = "exact",
		[Description("Inclusive upper value for between.")]
		string? upperValue = null,
		[Description(
			"Decimals written for float or double values, 0 to 15; default 6 for float and 12 for double. Each decimal fewer widens an exact match tenfold.")]
		int? floatDecimals = null,
		[Description(
			"Named scanners only: the first scanned address or expression, such as game.exe; requires endAddress. A match may start slightly before it.")]
		string? startAddress = null,
		[Description(
			"Named scanners only: the address or expression where the scan ends, exclusive; requires startAddress.")]
		string? endAddress = null,
		[Description(
			"Named scanners only: whether matches must be in writable memory: required, excluded or any (default). required skips code and read-only data.")]
		ProtectionRequirement writable = ProtectionRequirement.Any,
		[Description(
			"Named scanners only: whether matches must be in executable memory: required, excluded or any (default).")]
		ProtectionRequirement executable = ProtectionRequirement.Any,
		[Description(
			"Named scanners only: whether matches must be in copy-on-write memory: required, excluded or any (default).")]
		ProtectionRequirement copyOnWrite = ProtectionRequirement.Any,
		[Description(
			"Named scanners only: check only addresses divisible by this number, 1 to 65536, such as 4 for int32 (Cheat Engine's fast scan); not with lastDigits.")]
		int? alignment = null,
		[Description(
			"Named scanners only: check only addresses whose hexadecimal form ends with these 1 to 16 digits; not with alignment.")]
		string? lastDigits = null,
		[Description(
			"Named scanners only: also scan mapped memory (MEM_MAPPED: file views and shared sections, where emulators such as Dolphin, PCSX2 and PPSSPP keep guest RAM), which Cheat Engine skips unless ticked in its Scan Settings; default false. " +
			MappedMemoryOverride.IncludeMappedEffect + " scan_next keeps the regions of this first scan.")]
		bool includeMapped = false,
		CancellationToken cancellationToken = default)
	{
		CheckName(scannerName);
		ValueScanFirstRequest request = ScanCriteria.First(ScanCriteria.ParseValueType(valueType),
			ScanCriteria.ParseFirstComparison(comparison), value, upperValue, floatDecimals);
		NamedScanOptions options = NamedScanOptions.Create(startAddress, endAddress, writable, executable,
			copyOnWrite, alignment, lastDigits, includeMapped);
		if (scannerName == MainScanner)
		{
			options.RequireNone();

			// Main's scan lists its regions on Cheat Engine's scan thread, so it must not start inside an override.
			return _dispatch.Run(CheatEngineToolNames.ScanFirst, token => Map(_mappedMemory.Follow(
				CheatEngineToolNames.ScanFirst, () => _dispatch.ExecuteLua(CheatEngineToolNames.ScanFirst,
					ScanScripts.MainFirstScript, ScanJsonContext.Default.ScanUiStatus, token,
					ScanCriteria.Name(request.ValueType), FirstComparisonIndex(request.Comparison),
					request.Value?.Text, request.UpperValue?.Text))), cancellationToken);
		}

		return _dispatch.Run(CheatEngineToolNames.ScanFirst, token =>
		{
			if (_sessions.TryGetValue(scannerName, out IValueScanSession? existing) &&
				existing.State != ValueScanSessionState.Created)
			{
				throw CheatEngineToolException.InvalidState("The named scanner already holds a scan.",
					"Call scan_reset to clear it, or scan_delete to release it, then repeat scan_first.");
			}

			// Every refusal comes before the MEM_MAPPED override: the session limit, then the range, which resolves
			// before the session exists, so an unresolvable expression leaves nothing behind.
			CheckCapacity(scannerName);
			ValueScanFirstRequest scoped = options.Apply(_dispatch.Client, request, token);
			return options.IncludeMapped
				? _mappedMemory.Include(CheatEngineToolNames.ScanFirst, () => FirstScan(scannerName, scoped, token),
					status => new ScanMappedOverrideFailure(scannerName, status is not null, status),
					ScanJsonContext.Default.ScanMappedOverrideFailure, token)
				: _mappedMemory.Follow(CheatEngineToolNames.ScanFirst, () => FirstScan(scannerName, scoped, token));
		}, cancellationToken);
	}

	/// <summary>Narrows a completed scan.</summary>
	[McpServerTool(Name = CheatEngineToolNames.ScanNext, Title = "Narrow value scan", ReadOnly = false,
		Destructive = true, Idempotent = false, OpenWorld = false, UseStructuredContent = true)]
	[McpMeta(McpDispatchClass.MetaKey, McpDispatchClass.HostScan)]
	[Description(
		"Narrow a completed scan against its previous results, with the value type of its first scan. main returns at once with state Scanning (poll scan_get_status) and uses Cheat Engine's own settings; a named scanner scans synchronously and blocks Cheat Engine until the scan ends, which grows with its result count. increased, decreased, changed and unchanged take no value; string, wstring and bytes scans support exact only. Float and double values are written with floatDecimals decimals (default 6 and 12).")]
	public ScanState Next(
		[Description("main (default) or an existing independent scanner name.")]
		string scannerName = MainScanner,
		[Description(
			"Value for exact, between, greater, less, increasedBy or decreasedBy; omit for state comparisons. Integers are decimal.")]
		string? value = null,
		[Description(
			"exact, between, greater, less, increased, decreased, increasedBy, decreasedBy, changed or unchanged.")]
		string comparison = "exact",
		[Description("Inclusive upper value for between.")]
		string? upperValue = null,
		[Description(
			"Decimals written for float or double values, 0 to 15; default 6 for float and 12 for double. Only for float and double scans.")]
		int? floatDecimals = null,
		CancellationToken cancellationToken = default)
	{
		CheckName(scannerName);
		ValueScanComparison parsed = ScanCriteria.ParseNextComparison(comparison);
		ScanCriteria.CheckArguments(parsed, value, upperValue, floatDecimals);
		if (scannerName == MainScanner)
		{
			return _dispatch.Run(CheatEngineToolNames.ScanNext, token =>
			{
				// The first script only reads: a refused argument after it still reports not_started.
				ScanUiStatus current = _dispatch.ExecuteLua(CheatEngineToolNames.ScanNext,
					ScanScripts.MainNextCheckScript, ScanJsonContext.Default.ScanUiStatus, token);
				ValueScanNextRequest request = ScanCriteria.Next(MainValueType(current.ValueType), parsed, value,
					upperValue, floatDecimals);
				return Map(_dispatch.ExecuteLua(CheatEngineToolNames.ScanNext, ScanScripts.MainNextScript,
					ScanJsonContext.Default.ScanUiStatus, token, NextComparisonIndex(request.Comparison),
					request.Value?.Text, request.UpperValue?.Text));
			}, cancellationToken);
		}

		return _dispatch.Run(CheatEngineToolNames.ScanNext, token =>
		{
			IValueScanSession session = Find(scannerName);
			if (session.State != ValueScanSessionState.ResultsReady ||
				!_valueTypes.TryGetValue(scannerName, out ValueScanValueType type))
			{
				throw CheatEngineToolException.InvalidState("The named scanner has no completed results to narrow.",
					NoResultsHint);
			}

			session.NextScan(ScanCriteria.Next(type, parsed, value, upperValue, floatDecimals), token);
			return Describe(scannerName, session);
		}, cancellationToken);
	}

	/// <summary>Gets one scanner state.</summary>
	[McpServerTool(Name = CheatEngineToolNames.ScanGetStatus, Title = "Get scan status", ReadOnly = true,
		Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
	[McpMeta(McpDispatchClass.MetaKey, McpDispatchClass.Short)]
	[Description(
		"Read main's live UI scan state or the state of an independent Client session. Scanning means work is still in progress. For main it also returns settings, read-only: the Cheat Engine options that main's scans use (range, protection boxes, fast scan, active memory only, pause, Hex, rounding, simple values only, case sensitive, code page) and the MEM_PRIVATE, MEM_IMAGE and MEM_MAPPED settings, which apply to named scanners too. Check them when main finds nothing or far too much.")]
	public ScanStatusResult GetStatus(
		[Description("main (default) or an existing independent scanner name.")]
		string scannerName = MainScanner,
		CancellationToken cancellationToken = default)
	{
		CheckName(scannerName);
		if (scannerName == MainScanner)
		{
			ScanUiStatus main = _dispatch.RunLua(CheatEngineToolNames.ScanGetStatus,
				ScanScripts.MainStatusSettingsScript, ScanJsonContext.Default.ScanUiStatus, cancellationToken);
			return ScanStatusResult.From(Map(main), main.Settings);
		}

		return _dispatch.Run(CheatEngineToolNames.ScanGetStatus,
			_ => ScanStatusResult.From(Describe(scannerName, Find(scannerName))), cancellationToken);
	}

	/// <summary>Reads a bounded page of scan results.</summary>
	[McpServerTool(Name = CheatEngineToolNames.ScanListResults, Title = "List scan results", ReadOnly = true,
		Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
	[McpMeta(McpDispatchClass.MetaKey, McpDispatchClass.Short)]
	[Description(
		"Read one bounded page of results from main's visible found list or a named independent scan. Addresses are uppercase hexadecimal without 0x and values are Cheat Engine's display text; re-read an address with memory_read for a typed value. Unknown-initial scans need a next scan before they have rows.")]
	public ScanResultsResult ListResults(
		[Description("main (default) or an existing independent scanner name.")]
		string scannerName = MainScanner,
		[Description("Zero-based result index; a start at or past the end returns an empty last page.")]
		long startIndex = 0,
		[Description("Maximum copied rows, 1 to 1024.")]
		int maximumResults = 1000,
		CancellationToken cancellationToken = default)
	{
		CheckName(scannerName);
		if (maximumResults is < 1 or > MaximumResults)
		{
			throw CheatEngineToolException.InvalidArgument("maximumResults", "must be between 1 and 1024.");
		}

		if (startIndex < 0 || startIndex > long.MaxValue - maximumResults)
		{
			throw CheatEngineToolException.InvalidArgument("startIndex",
				"must be nonnegative and leave room for maximumResults.");
		}

		if (scannerName == MainScanner)
		{
			ScanUiResults ui = _dispatch.RunLua(CheatEngineToolNames.ScanListResults, ScanScripts.MainReadScript,
				ScanJsonContext.Default.ScanUiResults, cancellationToken, startIndex, maximumResults);
			return new ScanResultsResult(ui.ScannerName, ui.Mode, ToCount(ui.Count), ui.Results, ui.NextStartIndex,
				ui.HasMore);
		}

		return _dispatch.Run(CheatEngineToolNames.ScanListResults, token =>
		{
			IValueScanSession session = Find(scannerName);
			if (session.State != ValueScanSessionState.ResultsReady)
			{
				throw CheatEngineToolException.InvalidState("The named scanner has no results to read.",
					NoResultsHint);
			}

			// Like main, a start index at or past the end reads as an empty last page rather than a Client refusal.
			ulong count = session.GetResultCount(token);
			if ((ulong) startIndex >= count)
			{
				return new ScanResultsResult(scannerName, "independent", count, [], null, false);
			}

			ValueScanPage page = session.Read(new ValueScanReadRequest(startIndex, maximumResults), token);
			return new ScanResultsResult(scannerName, "independent", page.ResultCount,
				[
					.. page.Matches.Select(static match =>
						new ScanMatch(HexFormat.Address(match.Address), match.ValueText))
				],
				page.NextStartIndex, page.HasMore);
		}, cancellationToken);
	}

	/// <summary>Lists main and all independent scanners.</summary>
	[McpServerTool(Name = CheatEngineToolNames.ScanListScanners, Title = "List scanners", ReadOnly = true,
		Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
	[McpMeta(McpDispatchClass.MetaKey, McpDispatchClass.Short)]
	[Description(
		"List main, the visible Cheat Engine scanner, and up to 32 independent Client sessions in this activation.")]
	public ScanScannerList ListScanners(CancellationToken cancellationToken = default)
	{
		return _dispatch.Run(CheatEngineToolNames.ScanListScanners, token =>
		{
			List<ScanState> scanners =
			[
				Map(_dispatch.ExecuteLua(CheatEngineToolNames.ScanListScanners,
					ScanScripts.MainStatusScript, ScanJsonContext.Default.ScanUiStatus, token))
			];
			scanners.AddRange(_sessions.OrderBy(static item => item.Key, StringComparer.Ordinal)
				.Select(item => Describe(item.Key, item.Value)));
			return new ScanScannerList([.. scanners]);
		}, cancellationToken);
	}

	/// <summary>Clears a scanner's results so that it accepts a new first scan, keeping the scanner.</summary>
	[McpServerTool(Name = CheatEngineToolNames.ScanReset, Title = "Reset scan", ReadOnly = false,
		Destructive = true, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
	[McpMeta(McpDispatchClass.MetaKey, McpDispatchClass.Short)]
	[Description(
		"Clear a scanner's results so that it accepts a new first scan. main runs Cheat Engine's New Scan, showing its window first when hidden, and is refused while it runs. A named scanner keeps its name and its retained resource, which blocks attaching another process, and recovers from Invalidated unless its target or Lua runtime changed, which leaves only scan_delete; release it with scan_delete when done.")]
	public ScanReleaseResult Reset(
		[Description("main (default) or the independent scanner name to clear.")]
		string scannerName = MainScanner,
		CancellationToken cancellationToken = default)
	{
		CheckName(scannerName);
		if (scannerName == MainScanner)
		{
			ScanState status = Map(_dispatch.RunLua(CheatEngineToolNames.ScanReset,
				ScanScripts.MainResetScript, ScanJsonContext.Default.ScanUiStatus, cancellationToken));
			return new ScanReleaseResult(scannerName, false, false, false, status);
		}

		return _dispatch.Run(CheatEngineToolNames.ScanReset, token =>
		{
			IValueScanSession session = Find(scannerName);
			if (session.State is ValueScanSessionState.Scanning or ValueScanSessionState.Closed)
			{
				throw CheatEngineToolException.InvalidState(
					$"The named scanner is {session.State} and accepts only its release.",
					"Release it with scan_delete, then start a new one with scan_first.");
			}

			// The Client recovers an Invalidated session only while its target and Lua runtime are unchanged; after
			// either change the reset fails, and a re-attach would be refused because this very session is retained.
			if (session.State == ValueScanSessionState.Invalidated && session.Invalidation is
					ValueScanInvalidationKind.TargetChanged or ValueScanInvalidationKind.RuntimeChanged)
			{
				throw CheatEngineToolException.InvalidState(
					"The named scanner was invalidated by a target or Lua runtime change and accepts only its release.",
					ChangedReleaseHint);
			}

			// A session without results is already reset; skipping the Client call keeps the tool idempotent.
			if (session.State != ValueScanSessionState.Created)
			{
				session.Reset(token);
			}

			_valueTypes.Remove(scannerName);
			return new ScanReleaseResult(scannerName, false, false, false, Describe(scannerName, session));
		}, cancellationToken);
	}

	/// <summary>Deletes an independent scan session.</summary>
	[McpServerTool(Name = CheatEngineToolNames.ScanDelete, Title = "Delete independent scanner", ReadOnly = false,
		Destructive = true, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
	[McpMeta(McpDispatchClass.MetaKey, McpDispatchClass.Short)]
	[Description(
		"Release and delete one independent Client scan session with its results, ending the retained resource that blocks attaching another process. main is the visible Cheat Engine scanner; reset it instead.")]
	public ScanReleaseResult Delete(
		[Description("The independent scanner name to delete; main is not accepted.")]
		string scannerName,
		CancellationToken cancellationToken = default)
	{
		CheckName(scannerName);
		if (scannerName == MainScanner)
		{
			throw CheatEngineToolException.InvalidArgument("scannerName",
				"main is the visible scanner; use scan_reset.");
		}

		return _dispatch.Run(CheatEngineToolNames.ScanDelete, token => Release(scannerName, token), cancellationToken);
	}

	/// <summary>Requests a stop for a running UI scan or releases a named Client scan.</summary>
	[McpServerTool(Name = CheatEngineToolNames.ScanStop, Title = "Stop scan", ReadOnly = false,
		Destructive = true, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
	[McpMeta(McpDispatchClass.MetaKey, McpDispatchClass.Short)]
	[Description(
		"Ask main's running scan to stop without waiting for it, as Cheat Engine's Cancel button does, and untick its Repeat box so that it does not start again; poll scan_get_status, which reads Scanning until Cheat Engine has ended the scan. A stopped scan keeps only what it found before the stop, so run scan_reset and scan_first again for complete results. stopRequested is false when main was idle. A named scanner scans synchronously, so stopping it releases and deletes it like scan_delete.")]
	public ScanStopResult Stop(
		[Description("main (default) or an independent scanner name.")]
		string scannerName = MainScanner,
		CancellationToken cancellationToken = default)
	{
		CheckName(scannerName);
		if (scannerName == MainScanner)
		{
			ScanUiStop stop = _dispatch.RunLua(CheatEngineToolNames.ScanStop, ScanScripts.MainStopScript,
				ScanJsonContext.Default.ScanUiStop, cancellationToken);
			return new ScanStopResult(scannerName, stop.StopRequested, false, Map(stop.Status));
		}

		return _dispatch.Run(CheatEngineToolNames.ScanStop, token =>
		{
			ScanReleaseResult released = Release(scannerName, token);
			return new ScanStopResult(scannerName, true, released.Removed);
		}, cancellationToken);
	}

	/// <summary>Checks whether Cheat Engine's visible scanner blocks a target transition.</summary>
	/// <param name="dispatch">The activation's bounded Client dispatch, already admitted by the process tool.</param>
	/// <param name="cancellationToken">The target transition's request token.</param>
	/// <returns><see langword="true" /> while the visible scanner is running or repeating.</returns>
	internal static bool IsMainScannerBusy(ToolDispatch dispatch, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(dispatch);
		return dispatch.ExecuteLua("scan_main_transition_guard", ScanScripts.MainBusyScript,
			ScanJsonContext.Default.Boolean, cancellationToken);
	}

	private IValueScanSession Find(string name)
	{
		return _sessions.TryGetValue(name, out IValueScanSession? session)
			? session
			: throw CheatEngineToolException.NotFound(MissingMessage, MissingHint);
	}

	private IValueScanSession GetOrCreate(string name)
	{
		if (_sessions.TryGetValue(name, out IValueScanSession? existing))
		{
			return existing;
		}

		CheckCapacity(name);
		IValueScanSession session = _dispatch.Client.ValueScans.CreateSession();
		_sessions.Add(name, session);
		_resources.Track(session, "scan", () => Remove(name), name);
		return session;
	}

	/// <summary>Refuses a new named scanner when the activation already holds the most.</summary>
	/// <exception cref="CheatEngineToolException">The limit is reached (<c>limit_exceeded</c>).</exception>
	private void CheckCapacity(string name)
	{
		if (!_sessions.ContainsKey(name) && _sessions.Count >= MaximumScanners)
		{
			throw CheatEngineToolException.LimitExceeded("scannerName",
				"at most 32 named scan sessions may be active.");
		}
	}

	private ScanState FirstScan(string name, ValueScanFirstRequest request, CancellationToken cancellationToken)
	{
		IValueScanSession session = GetOrCreate(name);
		session.FirstScan(request, cancellationToken);
		_valueTypes[name] = request.ValueType;
		return Describe(name, session);
	}

	private ScanReleaseResult Release(string name, CancellationToken cancellationToken)
	{
		IValueScanSession session = Find(name);
		LeaseReleaseOutcome release = session.Release();
		if (release.IsComplete)
		{
			_resources.Forget(session);
			Remove(name);
			return new ScanReleaseResult(name, true, false, false);
		}

		ScanReleaseFailure details = new(name, release.IsRetryable, release.RequiresManualRecovery);
		throw CheatEngineToolException.PartialEffect(
			$"The scan session {name} was asked to release but did not complete.", ToolHostEffect.Unknown, details,
			ScanJsonContext.Default.ScanReleaseFailure, release.IsRetryable,
			release.IsRetryable
				? "Repeat the scan release shortly."
				: "Recover the scan session manually, then release its retained resource.");
	}

	private ScanState Describe(string name, IValueScanSession session)
	{
		return new ScanState(name, "independent", session.State.ToString(),
			session.State == ValueScanSessionState.Scanning, session.State == ValueScanSessionState.ResultsReady,
			session.State == ValueScanSessionState.ResultsReady ? session.GetResultCount() : null,
			_valueTypes.TryGetValue(name, out ValueScanValueType type) ? ScanCriteria.Name(type) : null);
	}

	private void Remove(string name)
	{
		_sessions.Remove(name);
		_valueTypes.Remove(name);
	}

	private static ScanState Map(ScanUiStatus value)
	{
		return new ScanState(value.ScannerName, value.Mode, value.State, value.IsScanning, value.ResultsReady,
			ToCount(value.Count), value.ValueType, value.ProcessId, value.Error);
	}

	private static ulong? ToCount(long? count)
	{
		return count is { } value && value >= 0 ? (ulong) value : null;
	}

	private static void CheckName(string? scannerName)
	{
		if (string.IsNullOrWhiteSpace(scannerName))
		{
			throw CheatEngineToolException.InvalidArgument("scannerName", "is required.");
		}

		if (scannerName.Length > MaximumScannerNameLength)
		{
			throw CheatEngineToolException.InvalidArgument("scannerName", "must not exceed 256 characters.");
		}
	}

	private static ValueScanValueType MainValueType(string? valueType)
	{
		return ScanCriteria.TryParseValueType(valueType, out ValueScanValueType type)
			? type
			: throw CheatEngineToolException.InvalidState(
				"Cheat Engine's main scanner holds a scan of a value type that MCP cannot narrow.",
				"Narrow it in Cheat Engine, or reset main with scan_reset and start again with scan_first.");
	}

	private static int FirstComparisonIndex(ValueScanComparison comparison)
	{
		return comparison switch
		{
			ValueScanComparison.Exact => 0,
			ValueScanComparison.BiggerThan => 1,
			ValueScanComparison.SmallerThan => 2,
			ValueScanComparison.Between => 3,
			ValueScanComparison.UnknownInitialValue => 4,
			_ => throw new ArgumentOutOfRangeException(nameof(comparison))
		};
	}

	private static int NextComparisonIndex(ValueScanComparison comparison)
	{
		return comparison switch
		{
			ValueScanComparison.Exact => 0,
			ValueScanComparison.BiggerThan => 1,
			ValueScanComparison.SmallerThan => 2,
			ValueScanComparison.Between => 3,
			ValueScanComparison.Increased => 4,
			ValueScanComparison.IncreasedBy => 5,
			ValueScanComparison.Decreased => 6,
			ValueScanComparison.DecreasedBy => 7,
			ValueScanComparison.Changed => 8,
			ValueScanComparison.Unchanged => 9,
			_ => throw new ArgumentOutOfRangeException(nameof(comparison))
		};
	}
}
