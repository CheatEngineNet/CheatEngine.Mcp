using System.ComponentModel;
using System.Globalization;

using CheatEngine.Client.Results;
using CheatEngine.Client.Scanning;
using CheatEngine.Mcp.Core.Contract;

using ModelContextProtocol.Server;

namespace CheatEngine.Mcp.Tools.Scan;

/// <summary>UI and independent Client value scans with bounded result pages and explicit lifecycle operations.</summary>
[McpServerToolType]
public sealed class ScanTools : IDisposable
{
	private const int MaximumScanners = 32;
	private const int MaximumScannerNameLength = 256;
	private const int MaximumScanValueCharacters = 1024 * 1024;
	private const int MaximumResults = 1024;

	internal const string MainScannerBusyMessage =
		"The main UI scanner is busy. Wait for it to finish or cancel it in Cheat Engine before switching targets.";

	private const string MainContext = """
	                                   local f = assert(getMainForm(), 'Cheat Engine main form is unavailable')
	                                   local ms = assert(getCurrentMemscan(), 'Cheat Engine main scanner is unavailable')
	                                   assert(f.btnNewScan and f.btnNextScan and f.VarType and f.ScanType, 'Unsupported Cheat Engine scan controls')
	                                   local function valueType()
	                                     local types = {[1]='byte',[2]='int16',[3]='int32',[4]='int64',[5]='float',[6]='double',[7]='string',[8]='bytes'}
	                                     local result = types[f.VarType.ItemIndex] or 'unsupported'
	                                     if result == 'string' and f.cbUnicode.Checked then result = 'wstring' end
	                                     return result
	                                   end
	                                   local function isBusy()
	                                     local repeating = f.findComponentByName('cbRepeatUntilStopped')
	                                     return getOpenedProcessID() ~= 0 and (not f.btnNewScan.Enabled or
	                                       (repeating ~= nil and repeating.Visible and repeating.Checked and ms.LastScanType ~= 'stNewScan'))
	                                   end
	                                   local function summary()
	                                     local attached = getOpenedProcessID() ~= 0
	                                     local busy = isBusy()
	                                     local started = ms.LastScanType ~= 'stNewScan'
	                                     local baseline = started and ms.LastScanWasRegionScan
	                                     local failure = not busy and ms.ErrorString or ''
	                                     local state
	                                     if not attached then
	                                       state = 'NoTarget'
	                                     elseif busy then
	                                       state = 'Scanning'
	                                     elseif failure ~= '' then
	                                       state = 'Failed'
	                                     elseif not started then
	                                       state = 'Created'
	                                     elseif baseline then
	                                       state = 'BaselineReady'
	                                     else
	                                       state = 'ResultsReady'
	                                     end
	                                     local count = state == 'ResultsReady' and ms.FoundList.Count or nil
	                                     return {scannerName='main',mode='ui',state=state,isScanning=busy,resultsReady=state=='ResultsReady',
	                                       count=count,valueType=valueType(),processId=getOpenedProcessID(),error=failure ~= '' and failure or nil}
	                                   end
	                                   local function idle()
	                                     assert(not isBusy(), 'The main UI scanner is busy; poll scan_get_status or stop it before changing the scan')
	                                   end
	                                   """ + "\n";

	private const string MainStatusScript = MainContext + "return summary()";

	private const string MainFirstScript = MainContext + """
	                                                     idle()
	                                                     assert(getOpenedProcessID() ~= 0, 'Open a target process before scanning')
	                                                     assert(ms.LastScanType == 'stNewScan', 'Reset the main scanner explicitly before starting another first scan')
	                                                     local indices = {byte=1,int16=2,int32=3,int64=4,float=5,double=6,string=7,wstring=7,bytes=8}
	                                                     f.VarType.ItemIndex = assert(indices[a[1]], 'Unsupported main scan value type')
	                                                     f.VarType.OnChange(f.VarType)
	                                                     f.cbUnicode.Checked = a[1] == 'wstring'
	                                                     assert(a[2] < f.ScanType.Items.Count, 'This comparison is unavailable in the main scanner')
	                                                     f.ScanType.ItemIndex = a[2]
	                                                     f.ScanType.OnChange(f.ScanType)
	                                                     for _, name in ipairs({'cbLuaFormula','cbNot','cbRepeatUntilStopped','cbPercentage','cbCompareToSavedScan'}) do
	                                                       local control = f.findComponentByName(name)
	                                                       if control then control.Checked = false end
	                                                     end
	                                                     f.cbHexadecimal.Checked = valueType() == 'bytes'
	                                                     f.Scanvalue.Text = a[3] or ''
	                                                     local second = f.findComponentByName('scanvalue2')
	                                                     if a[4] ~= nil then assert(second, 'The upper-value control is unavailable').Text = a[4] end
	                                                     f.btnNewScan.doClick()
	                                                     return summary()
	                                                     """;

	private const string MainNextScript = MainContext + """
	                                                    idle()
	                                                    assert(getOpenedProcessID() ~= 0, 'Open a target process before scanning')
	                                                    assert(ms.LastScanType ~= 'stNewScan' and f.btnNextScan.Enabled, 'The main scanner has no completed scan to narrow')
	                                                    assert(ms.ErrorString == '', 'The main scanner failed; reset it before scanning again')
	                                                    assert(a[1] < f.ScanType.Items.Count, 'This comparison is unavailable in the main scanner')
	                                                    f.ScanType.ItemIndex = a[1]
	                                                    f.ScanType.OnChange(f.ScanType)
	                                                    for _, name in ipairs({'cbLuaFormula','cbNot','cbRepeatUntilStopped','cbPercentage','cbCompareToSavedScan'}) do
	                                                      local control = f.findComponentByName(name)
	                                                      if control then control.Checked = false end
	                                                    end
	                                                    f.Scanvalue.Text = a[2] or ''
	                                                    local second = f.findComponentByName('scanvalue2')
	                                                    if a[3] ~= nil then assert(second, 'The upper-value control is unavailable').Text = a[3] end
	                                                    f.btnNextScan.doClick()
	                                                    return summary()
	                                                    """;

	private const string MainReadScript = MainContext + """
	                                                    idle()
	                                                    assert(ms.LastScanType ~= 'stNewScan', 'The main scanner has no results')
	                                                    assert(ms.ErrorString == '', 'The main scanner failed; reset it before reading results')
	                                                    assert(not ms.LastScanWasRegionScan, 'Unknown-initial baseline: run scan_next before reading results')
	                                                    local found = assert(ms.FoundList, 'The UI found list is unavailable')
	                                                    local count = found.Count
	                                                    local finish = math.min(count, a[1] + a[2])
	                                                    local rows = {}
	                                                    for i = a[1], finish - 1 do
	                                                      local value = found.Value[i] or ''
	                                                      assert(#value <= 4096, 'A scan value exceeds the 4096-byte per-result limit; narrow the scan before reading')
	                                                      local address = found.Address[i]:gsub('^0[xX]', ''):gsub('^0+', ''):upper()
	                                                      rows[#rows + 1] = {address='0x' .. (address == '' and '0' or address), value=value}
	                                                    end
	                                                    return {scannerName='main',mode='ui',count=count,results=rows,
	                                                      hasMore=finish<count,nextStartIndex=finish<count and finish or nil}
	                                                    """;

	private const string MainResetScript = MainContext + """
	                                                     idle()
	                                                     if ms.LastScanType ~= 'stNewScan' then
	                                                       if not f.Visible then f.show() end
	                                                       f.btnNewScan.doClick()
	                                                     end
	                                                     assert(ms.LastScanType == 'stNewScan', 'Cheat Engine did not reset the main scanner')
	                                                     return summary()
	                                                     """;

	private const string MainStopScript = MainContext + """
	                                                    if isBusy() then
	                                                      assert(type(ms.terminate) == 'function', 'This Cheat Engine scanner cannot be cancelled through Lua; cancel it in Cheat Engine')
	                                                      ms.terminate()
	                                                    end
	                                                    return summary()
	                                                    """;

	private readonly ToolDispatch _dispatch;
	private readonly TargetResources _resources;
	private readonly Dictionary<string, IValueScanSession> _sessions = new(StringComparer.Ordinal);
	private readonly Dictionary<string, ValueScanValueType> _valueTypes = new(StringComparer.Ordinal);
	private bool _disposed;

	/// <summary>Creates a scan container without opening a target or starting a scanner.</summary>
	public ScanTools(ToolDispatch dispatch, TargetResources resources)
	{
		ArgumentNullException.ThrowIfNull(dispatch);
		ArgumentNullException.ThrowIfNull(resources);
		_dispatch = dispatch;
		_resources = resources;
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
	[McpMeta(McpDispatchClass.MetaKey, McpDispatchClass.Short)]
	[Description(
		"Start main, Cheat Engine's visible scanner, or an independent named Client session. Main changes the visible scan controls; named sessions do not. Reset explicitly before another first scan.")]
	public ScanStatusResult First(
		[Description("main (default) for the visible Cheat Engine scanner, or a unique independent name.")]
		string scannerName = "main",
		[Description("byte, int16, int32 (default), int64, float, double, string, wstring or bytes.")]
		string valueType = "int32",
		[Description("Value for exact, between, greater or less; omit for unknown.")]
		string? value = null,
		[Description("exact, unknown, between, greater or less.")]
		string comparison = "exact",
		[Description("Inclusive upper value for between.")]
		string? upperValue = null,
		CancellationToken cancellationToken = default)
	{
		CheckName(scannerName);
		CheckValue(value, "value");
		CheckValue(upperValue, "upperValue");
		ValueScanFirstRequest request = CreateFirstRequest(valueType, comparison, value, upperValue);
		if (scannerName == "main")
		{
			return Map(_dispatch.RunLua(CheatEngineToolNames.ScanFirst, MainFirstScript,
				ScanJsonContext.Default.ScanUiStatus, cancellationToken, MainScannerType(request.ValueType),
				FirstComparisonIndex(request.Comparison), request.Value?.Text, request.UpperValue?.Text));
		}

		return _dispatch.Run(CheatEngineToolNames.ScanFirst, token =>
		{
			IValueScanSession session = GetOrCreate(scannerName);
			if (session.State != ValueScanSessionState.Created)
			{
				throw CheatEngineToolException.InvalidState("Reset the named scan before starting another first scan.");
			}

			session.FirstScan(request, token);
			_valueTypes[scannerName] = request.ValueType;
			return Describe(scannerName, session);
		}, cancellationToken);
	}

	/// <summary>Narrows a completed scan.</summary>
	[McpServerTool(Name = CheatEngineToolNames.ScanNext, Title = "Narrow value scan", ReadOnly = false,
		Destructive = true, Idempotent = false, OpenWorld = false, UseStructuredContent = true)]
	[McpMeta(McpDispatchClass.MetaKey, McpDispatchClass.Short)]
	[Description(
		"Narrow main's visible results or a named independent scan. A scan must have completed before it can be narrowed.")]
	public ScanStatusResult Next(
		[Description("main (default) or an existing independent scanner name.")]
		string scannerName = "main",
		[Description(
			"Value for exact, between, greater, less, increasedBy or decreasedBy; omit for state comparisons.")]
		string? value = null,
		[Description(
			"exact, between, greater, less, increased, decreased, increasedBy, decreasedBy, changed or unchanged.")]
		string comparison = "exact",
		[Description("Inclusive upper value for between.")]
		string? upperValue = null,
		CancellationToken cancellationToken = default)
	{
		CheckName(scannerName);
		CheckValue(value, "value");
		CheckValue(upperValue, "upperValue");
		if (scannerName == "main")
		{
			return _dispatch.Run(CheatEngineToolNames.ScanNext, token =>
			{
				ScanUiStatus current = _dispatch.ExecuteLua(CheatEngineToolNames.ScanNext, MainStatusScript,
					ScanJsonContext.Default.ScanUiStatus, token);
				ValueScanNextRequest request = CreateNextRequest(ParseValueType(current.ValueType ?? ""), comparison,
					value,
					upperValue);
				return Map(_dispatch.ExecuteLua(CheatEngineToolNames.ScanNext, MainNextScript,
					ScanJsonContext.Default.ScanUiStatus, token, NextComparisonIndex(request.Comparison),
					request.Value?.Text,
					request.UpperValue?.Text));
			}, cancellationToken);
		}

		return _dispatch.Run(CheatEngineToolNames.ScanNext, token =>
		{
			if (!_sessions.TryGetValue(scannerName, out IValueScanSession? session) ||
				!_valueTypes.TryGetValue(scannerName, out ValueScanValueType type))
			{
				throw CheatEngineToolException.NotFound("No scan exists with that scannerName.");
			}

			if (session.State != ValueScanSessionState.ResultsReady)
			{
				throw CheatEngineToolException.InvalidState("The named scan has no completed results to narrow.");
			}

			session.NextScan(CreateNextRequest(type, comparison, value, upperValue), token);
			return Describe(scannerName, session);
		}, cancellationToken);
	}

	/// <summary>Gets one scanner state.</summary>
	[McpServerTool(Name = CheatEngineToolNames.ScanGetStatus, Title = "Get scan status", ReadOnly = true,
		Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
	[McpMeta(McpDispatchClass.MetaKey, McpDispatchClass.Short)]
	[Description(
		"Read main's live UI scan state or the state of an independent Client session. Scanning means work is still in progress.")]
	public ScanStatusResult GetStatus(
		[Description("main (default) or an existing independent scanner name.")]
		string scannerName = "main",
		CancellationToken cancellationToken = default)
	{
		CheckName(scannerName);
		if (scannerName == "main")
		{
			return Map(_dispatch.RunLua(CheatEngineToolNames.ScanGetStatus, MainStatusScript,
				ScanJsonContext.Default.ScanUiStatus, cancellationToken));
		}

		return _dispatch.Run(CheatEngineToolNames.ScanGetStatus, _ => _sessions.TryGetValue(scannerName,
			out IValueScanSession? session)
			? Describe(scannerName, session)
			: throw CheatEngineToolException.NotFound("No scan exists with that scannerName."), cancellationToken);
	}

	/// <summary>Reads a bounded page of scan results.</summary>
	[McpServerTool(Name = CheatEngineToolNames.ScanListResults, Title = "List scan results", ReadOnly = true,
		Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
	[McpMeta(McpDispatchClass.MetaKey, McpDispatchClass.Short)]
	[Description(
		"Read one bounded page of results from main's visible found list or a named independent scan. Unknown-initial scans need a next scan before they have rows.")]
	public ScanResultsResult ListResults(
		[Description("main (default) or an existing independent scanner name.")]
		string scannerName = "main",
		[Description("Zero-based result index.")]
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

		if (scannerName == "main")
		{
			ScanUiResults ui = _dispatch.RunLua(CheatEngineToolNames.ScanListResults, MainReadScript,
				ScanJsonContext.Default.ScanUiResults, cancellationToken, startIndex, maximumResults);
			return new ScanResultsResult(ui.ScannerName, ui.Mode, ToCount(ui.Count), ui.Results, ui.NextStartIndex,
				ui.HasMore);
		}

		return _dispatch.Run(CheatEngineToolNames.ScanListResults, token =>
		{
			if (!_sessions.TryGetValue(scannerName, out IValueScanSession? session))
			{
				throw CheatEngineToolException.NotFound("No scan exists with that scannerName.");
			}

			ValueScanPage page = session.Read(new ValueScanReadRequest(startIndex, maximumResults), token);
			return new ScanResultsResult(scannerName, "independent", page.ResultCount,
				[.. page.Matches.Select(static match => new ScanMatch($"0x{match.Address.Value:X}", match.ValueText))],
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
			List<ScanStatusResult> scanners =
			[
				Map(_dispatch.ExecuteLua(CheatEngineToolNames.ScanListScanners,
					MainStatusScript, ScanJsonContext.Default.ScanUiStatus, token))
			];
			scanners.AddRange(_sessions.OrderBy(static item => item.Key, StringComparer.Ordinal)
				.Select(item => Describe(item.Key, item.Value)));
			return new ScanScannerList([.. scanners]);
		}, cancellationToken);
	}

	/// <summary>Resets main or releases an independent scanner session.</summary>
	[McpServerTool(Name = CheatEngineToolNames.ScanReset, Title = "Reset scan", ReadOnly = false,
		Destructive = true, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
	[McpMeta(McpDispatchClass.MetaKey, McpDispatchClass.Short)]
	[Description(
		"Reset main through Cheat Engine's New Scan action, or release and remove a named independent session. Main cannot be reset while it is running.")]
	public ScanReleaseResult Reset(
		[Description("main (default) or the independent scanner name to release.")]
		string scannerName = "main",
		CancellationToken cancellationToken = default)
	{
		CheckName(scannerName);
		if (scannerName == "main")
		{
			ScanStatusResult status = Map(_dispatch.RunLua(CheatEngineToolNames.ScanReset, MainResetScript,
				ScanJsonContext.Default.ScanUiStatus, cancellationToken));
			return new ScanReleaseResult(scannerName, false, false, false, status);
		}

		return _dispatch.Run(CheatEngineToolNames.ScanReset, token => Release(scannerName, token), cancellationToken);
	}

	/// <summary>Deletes an independent scan session.</summary>
	[McpServerTool(Name = CheatEngineToolNames.ScanDelete, Title = "Delete independent scanner", ReadOnly = false,
		Destructive = true, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
	[McpMeta(McpDispatchClass.MetaKey, McpDispatchClass.Short)]
	[Description(
		"Release and delete one independent Client scan session. main is the visible Cheat Engine scanner; reset it instead.")]
	public ScanReleaseResult Delete(
		[Description("The independent scanner name to delete; main is not accepted.")]
		string scannerName,
		CancellationToken cancellationToken = default)
	{
		CheckName(scannerName);
		if (scannerName == "main")
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
		"Request cancellation of main without waiting for the UI scan to finish, or release an independent Client session. Poll status after stopping main.")]
	public ScanStopResult Stop(
		[Description("main (default) or an independent scanner name.")]
		string scannerName = "main",
		CancellationToken cancellationToken = default)
	{
		CheckName(scannerName);
		if (scannerName == "main")
		{
			return new ScanStopResult(scannerName, true, false, Map(_dispatch.RunLua(CheatEngineToolNames.ScanStop,
				MainStopScript, ScanJsonContext.Default.ScanUiStatus, cancellationToken)));
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
		return dispatch.ExecuteLua("scan_main_transition_guard", MainContext + "return isBusy()",
			ScanJsonContext.Default.Boolean, cancellationToken);
	}

	private IValueScanSession GetOrCreate(string name)
	{
		if (_sessions.TryGetValue(name, out IValueScanSession? existing))
		{
			return existing;
		}

		if (_sessions.Count >= MaximumScanners)
		{
			throw CheatEngineToolException.LimitExceeded("scannerName",
				"at most 32 named scan sessions may be active.");
		}

		IValueScanSession session = _dispatch.Client.ValueScans.CreateSession();
		_sessions.Add(name, session);
		_resources.Track(session, "scan", () => Remove(name), name);
		return session;
	}

	private ScanReleaseResult Release(string name, CancellationToken cancellationToken)
	{
		if (!_sessions.TryGetValue(name, out IValueScanSession? session))
		{
			throw CheatEngineToolException.NotFound("No scan exists with that scannerName.");
		}

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

	private ScanStatusResult Describe(string name, IValueScanSession session)
	{
		return new ScanStatusResult(name, "independent", session.State.ToString(),
			session.State == ValueScanSessionState.Scanning, session.State == ValueScanSessionState.ResultsReady,
			session.State == ValueScanSessionState.ResultsReady ? session.GetResultCount() : null,
			_valueTypes.TryGetValue(name, out ValueScanValueType type) ? MainScannerType(type) : null);
	}

	private void Remove(string name)
	{
		_sessions.Remove(name);
		_valueTypes.Remove(name);
	}

	private static ScanStatusResult Map(ScanUiStatus value)
	{
		return new ScanStatusResult(value.ScannerName, value.Mode, value.State, value.IsScanning, value.ResultsReady,
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

	private static void CheckValue(string? value, string parameter)
	{
		if (value is { Length: > MaximumScanValueCharacters })
		{
			throw CheatEngineToolException.LimitExceeded(parameter, "must not exceed 1048576 characters.");
		}
	}

	private static ValueScanFirstRequest CreateFirstRequest(string valueType, string comparison, string? value,
		string? upperValue)
	{
		ValueScanValueType type = ParseValueType(valueType);
		return NormalizeComparison(comparison) switch
		{
			"exact" => ValueScanFirstRequest.Exact(ParseRequiredValue(type, value, "value")),
			"unknown" => RequireNoValues(value, upperValue, "unknown") is { } error
				? throw CheatEngineToolException.InvalidArgument("comparison", error)
				: ValueScanFirstRequest.UnknownInitialValue(type),
			"between" => ValueScanFirstRequest.Between(ParseRequiredValue(type, value, "value"),
				ParseRequiredValue(type, upperValue, "upperValue")),
			"greater" => ValueScanFirstRequest.BiggerThan(ParseRequiredValue(type, value, "value")),
			"less" => ValueScanFirstRequest.SmallerThan(ParseRequiredValue(type, value, "value")),
			_ => throw CheatEngineToolException.InvalidArgument("comparison",
				"must be exact, unknown, between, greater or less.")
		};
	}

	private static ValueScanNextRequest CreateNextRequest(ValueScanValueType type, string comparison, string? value,
		string? upperValue)
	{
		return NormalizeComparison(comparison) switch
		{
			"exact" => ValueScanNextRequest.Exact(ParseRequiredValue(type, value, "value")),
			"between" => ValueScanNextRequest.Between(ParseRequiredValue(type, value, "value"),
				ParseRequiredValue(type, upperValue, "upperValue")),
			"greater" => ValueScanNextRequest.BiggerThan(ParseRequiredValue(type, value, "value")),
			"less" => ValueScanNextRequest.SmallerThan(ParseRequiredValue(type, value, "value")),
			"increased" => RequireNoValues(value, upperValue, "increased") is { } error
				? throw CheatEngineToolException.InvalidArgument("comparison", error)
				: ValueScanNextRequest.Increased(),
			"decreased" => RequireNoValues(value, upperValue, "decreased") is { } error
				? throw CheatEngineToolException.InvalidArgument("comparison", error)
				: ValueScanNextRequest.Decreased(),
			"increasedby" => ValueScanNextRequest.IncreasedBy(ParseRequiredValue(type, value, "value")),
			"decreasedby" => ValueScanNextRequest.DecreasedBy(ParseRequiredValue(type, value, "value")),
			"changed" => RequireNoValues(value, upperValue, "changed") is { } error
				? throw CheatEngineToolException.InvalidArgument("comparison", error)
				: ValueScanNextRequest.Changed(),
			"unchanged" => RequireNoValues(value, upperValue, "unchanged") is { } error
				? throw CheatEngineToolException.InvalidArgument("comparison", error)
				: ValueScanNextRequest.Unchanged(),
			_ => throw CheatEngineToolException.InvalidArgument("comparison",
				"must be exact, between, greater, less, increased, decreased, increasedBy, decreasedBy, changed or unchanged.")
		};
	}

	private static string NormalizeComparison(string? comparison)
	{
		return comparison?.Trim().ToLowerInvariant().Replace("_", string.Empty, StringComparison.Ordinal) switch
		{
			null => string.Empty,
			"unknowninitial" or "unknowninitialvalue" => "unknown",
			"greaterthan" or "bigger" or "biggerthan" => "greater",
			"lessthan" or "smaller" or "smallerthan" => "less",
			string normalized => normalized
		};
	}

	private static string? RequireNoValues(string? value, string? upperValue, string comparison)
	{
		return value is null && upperValue is null
			? null
			: $"comparison '{comparison}' does not accept value or upperValue.";
	}

	private static ValueScanValue ParseRequiredValue(ValueScanValueType type, string? value, string parameter)
	{
		return value is null
			? throw CheatEngineToolException.InvalidArgument(parameter, "is required for this comparison.")
			: ParseValue(type, value, parameter);
	}

	private static ValueScanValueType ParseValueType(string valueType)
	{
		return valueType.ToLowerInvariant() switch
		{
			"byte" or "integer8" => ValueScanValueType.Integer8,
			"int16" or "integer16" => ValueScanValueType.Integer16,
			"int32" or "integer32" or "int" => ValueScanValueType.Integer32,
			"int64" or "integer64" or "long" => ValueScanValueType.Integer64,
			"float" or "singlefloat" => ValueScanValueType.SingleFloat,
			"double" or "doublefloat" => ValueScanValueType.DoubleFloat,
			"string" or "utf8string" => ValueScanValueType.Utf8String,
			"wstring" or "utf16string" => ValueScanValueType.Utf16String,
			"bytes" or "bytearray" => ValueScanValueType.ByteArray,
			_ => throw CheatEngineToolException.InvalidArgument("valueType",
				"must be byte, int16, int32, int64, float, double, string, wstring or bytes.")
		};
	}

	private static ValueScanValue ParseValue(ValueScanValueType type, string value, string parameter)
	{
		try
		{
			return type switch
			{
				ValueScanValueType.Integer8 => ValueScanValue.FromByte(byte.Parse(value, CultureInfo.InvariantCulture)),
				ValueScanValueType.Integer16 => ValueScanValue.FromInt16(short.Parse(value,
					CultureInfo.InvariantCulture)),
				ValueScanValueType.Integer32 =>
					ValueScanValue.FromInt32(int.Parse(value, CultureInfo.InvariantCulture)),
				ValueScanValueType.Integer64 => ValueScanValue.FromInt64(
					long.Parse(value, CultureInfo.InvariantCulture)),
				ValueScanValueType.SingleFloat => ValueScanValue.FromSingle(
					float.Parse(value, CultureInfo.InvariantCulture), 6),
				ValueScanValueType.DoubleFloat => ValueScanValue.FromDouble(
					double.Parse(value, CultureInfo.InvariantCulture), 12),
				ValueScanValueType.Utf8String => ValueScanValue.FromUtf8String(value),
				ValueScanValueType.Utf16String => ValueScanValue.FromUtf16String(value),
				ValueScanValueType.ByteArray => ValueScanValue.FromBytes(ParseBytes(value)),
				_ => throw new ArgumentOutOfRangeException(nameof(type))
			};
		}
		catch (FormatException)
		{
			throw CheatEngineToolException.InvalidArgument(parameter, $"is not a valid {type} value.");
		}
		catch (OverflowException)
		{
			throw CheatEngineToolException.InvalidArgument(parameter, $"is outside the range of {type}.");
		}
	}

	private static byte[] ParseBytes(string value)
	{
		return value.Split([' ', ','], StringSplitOptions.RemoveEmptyEntries)
			.Select(token => byte.Parse(token, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture)).ToArray();
	}

	private static string MainScannerType(ValueScanValueType type)
	{
		return type switch
		{
			ValueScanValueType.Integer8 => "byte",
			ValueScanValueType.Integer16 => "int16",
			ValueScanValueType.Integer32 => "int32",
			ValueScanValueType.Integer64 => "int64",
			ValueScanValueType.SingleFloat => "float",
			ValueScanValueType.DoubleFloat => "double",
			ValueScanValueType.Utf8String => "string",
			ValueScanValueType.Utf16String => "wstring",
			ValueScanValueType.ByteArray => "bytes",
			_ => throw new ArgumentOutOfRangeException(nameof(type))
		};
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
