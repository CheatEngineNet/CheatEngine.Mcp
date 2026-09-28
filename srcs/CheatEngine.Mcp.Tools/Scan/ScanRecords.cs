using System.ComponentModel;
using System.Text.Json.Serialization;

using CheatEngine.Mcp.Core.Contract;
using CheatEngine.Mcp.Tools.Memory;

namespace CheatEngine.Mcp.Tools.Scan;

/// <summary>
///     The live state of one UI or independent value scanner, as <c>scan_first</c>, <c>scan_next</c>,
///     <c>scan_list_scanners</c>, <c>scan_reset</c> and <c>scan_stop</c> report it.
/// </summary>
public sealed record ScanState(
	[property: Description(ScanStateText.ScannerName)]
	string ScannerName,
	[property: Description(ScanStateText.Mode)]
	string Mode,
	[property: Description(ScanStateText.State)]
	string State,
	[property: Description(ScanStateText.IsScanning)]
	bool IsScanning,
	[property: Description(ScanStateText.ResultsReady)]
	bool ResultsReady,
	[property: Description(ScanStateText.Count)]
	ulong? Count = null,
	[property: Description(ScanStateText.ValueType)]
	string? ValueType = null,
	[property: Description(ScanStateText.ProcessId)]
	int? ProcessId = null,
	[property: Description(ScanStateText.Error)]
	string? Error = null);

/// <summary>
///     The result of <c>scan_get_status</c>: the fields of <see cref="ScanState" />, and for main the Cheat Engine
///     settings that its scans use. Only this result carries the settings, so the other scan outputs stay small.
/// </summary>
public sealed record ScanStatusResult(
	[property: Description(ScanStateText.ScannerName)]
	string ScannerName,
	[property: Description(ScanStateText.Mode)]
	string Mode,
	[property: Description(ScanStateText.State)]
	string State,
	[property: Description(ScanStateText.IsScanning)]
	bool IsScanning,
	[property: Description(ScanStateText.ResultsReady)]
	bool ResultsReady,
	[property: Description(ScanStateText.Count)]
	ulong? Count = null,
	[property: Description(ScanStateText.ValueType)]
	string? ValueType = null,
	[property: Description(ScanStateText.ProcessId)]
	int? ProcessId = null,
	[property: Description(ScanStateText.Error)]
	string? Error = null,
	[property: Description(
		"main only: the Cheat Engine settings that main's scans use, read from its scan panel and Settings > Scan Settings; omitted for a named scanner.")]
	ScanMainSettings? Settings = null)
{
	/// <summary>Adds main's settings, if any, to a scanner state.</summary>
	/// <param name="state">The scanner state.</param>
	/// <param name="settings">Main's settings, or <see langword="null" /> for a named scanner.</param>
	/// <returns>The <c>scan_get_status</c> result.</returns>
	internal static ScanStatusResult From(ScanState state, ScanMainSettings? settings = null)
	{
		ArgumentNullException.ThrowIfNull(state);
		return new ScanStatusResult(state.ScannerName, state.Mode, state.State, state.IsScanning,
			state.ResultsReady, state.Count, state.ValueType, state.ProcessId, state.Error, settings);
	}
}

/// <summary>The descriptions shared by <see cref="ScanState" /> and <see cref="ScanStatusResult" />.</summary>
internal static class ScanStateText
{
	internal const string ScannerName = "main for Cheat Engine's visible scanner, or the independent scanner name.";
	internal const string Mode = "ui for Cheat Engine's visible scanner or independent for a Client scanner.";

	internal const string State =
		"main reports NoTarget, Created, Scanning, BaselineReady (an unknown-initial scan that needs scan_next before it has rows), ResultsReady or Failed; a named scanner reports Created, Scanning, ResultsReady, Invalidated or Closed.";

	internal const string IsScanning = "Whether the scanner is currently working.";
	internal const string ResultsReady = "Whether result rows can be read or narrowed.";
	internal const string Count = "The number of result rows when known.";
	internal const string ValueType = "The scanned value type when known.";
	internal const string ProcessId = "The UI target process id, when the scanner is main.";
	internal const string Error = "The host error reported by a completed UI scan, when any.";
}

/// <summary>One bounded value-scan match.</summary>
public sealed record ScanMatch(
	[property: Description("The matched address, uppercase hexadecimal without 0x.")]
	string Address,
	[property: Description("The value text reported by Cheat Engine.")]
	string Value);

/// <summary>A page of value-scan matches.</summary>
public sealed record ScanResultsResult(
	[property: Description("The scanner that owns these results.")]
	string ScannerName,
	[property: Description("ui for Cheat Engine's visible scanner or independent for a Client scanner.")]
	string Mode,
	[property: Description("The total result count when known.")]
	ulong? Count,
	[property: Description("The copied result rows.")]
	ScanMatch[] Results,
	[property: Description("The startIndex for the next page, omitted when this page ends the listing.")]
	long? NextStartIndex,
	[property: Description("Whether more rows remain after this page.")]
	bool HasMore);

/// <summary>Every visible scanner in the activation.</summary>
public sealed record ScanScannerList(
	[property: Description("main followed by independent scanners in name order.")]
	ScanState[] Scanners);

/// <summary>What a scanner reset or an independent scanner deletion did.</summary>
public sealed record ScanReleaseResult(
	[property: Description("The scanner name.")]
	string ScannerName,
	[property: Description(
		"Whether the named session was released and removed: true after scan_delete, false after scan_reset, which keeps the scanner.")]
	bool Removed,
	[property: Description("Whether a failed release may succeed when retried; false after a completed call.")]
	bool Retryable,
	[property: Description(
		"Whether the host needs manual recovery before the session can be forgotten; false after a completed call.")]
	bool RequiresManualRecovery,
	[property: Description("The scanner's state after scan_reset, normally Created; omitted by scan_delete.")]
	ScanState? Status = null);

/// <summary>What a stop request did to a scanner.</summary>
public sealed record ScanStopResult(
	[property: Description("The scanner name.")]
	string ScannerName,
	[property: Description(
		"Whether a running or repeating main scan was asked to stop, or a named session was released; false when main was idle.")]
	bool StopRequested,
	[property: Description("Whether an independent session was removed.")]
	bool Removed,
	[property: Description(
		"The state observed immediately after the stop request, when scannerName is main; a running scan still reads Scanning until Cheat Engine has ended it, then ResultsReady or BaselineReady with only what it found before the stop.")]
	ScanState? Status = null);

/// <summary>The details of a scan session cleanup that did not complete.</summary>
public sealed record ScanReleaseFailure(
	[property: Description("The scanner whose Client session was not fully released.")]
	string ScannerName,
	[property: Description("Whether the release can be retried.")]
	bool Retryable,
	[property: Description("Whether the host needs manual recovery.")]
	bool RequiresManualRecovery);

/// <summary>The bounded result of a fixed Lua status script for Cheat Engine's visible scanner.</summary>
public sealed record ScanUiStatus(
	string ScannerName,
	string Mode,
	string State,
	bool IsScanning,
	bool ResultsReady,
	long? Count = null,
	string? ValueType = null,
	int? ProcessId = null,
	string? Error = null,
	ScanMainSettings? Settings = null);

/// <summary>The bounded result of the fixed Lua stop script for Cheat Engine's visible scanner.</summary>
public sealed record ScanUiStop(
	bool StopRequested,
	ScanUiStatus Status);

/// <summary>The bounded result of a fixed Lua page-read script for Cheat Engine's visible scanner.</summary>
public sealed record ScanUiResults(
	string ScannerName,
	string Mode,
	long? Count,
	ScanMatch[] Results,
	long? NextStartIndex,
	bool HasMore);

/// <summary>
///     The Cheat Engine settings that main's first and next scans use, as its scan panel and its Settings > Scan
///     Settings page show them; every field is omitted when its control is unavailable.
/// </summary>
/// <remarks>
///     MCP's main scripts set the value type, comparison, value and UTF-16 box themselves and untick Lua formula,
///     Not, Repeat, Percent and Compare to first/saved scan, so those are not reported. Hex is reported as it stands,
///     although each MCP scan sets it again, and so is the Fast Scan alignment, which Cheat Engine resets to the
///     value type's size whenever the type is set (<c>MainUnit.pas</c>, <c>VarTypeChange</c>) unless the user edited
///     it; the remaining options keep what the user chose and change which addresses main finds.
/// </remarks>
public sealed record ScanMainSettings(
	[property: Description(
		"The Start field of main's scan range as Cheat Engine shows it: hexadecimal without 0x, or an address expression.")]
	string? StartAddress = null,
	[property: Description(
		"The Stop field of main's scan range, where a first scan ends; a named scanner takes the same text as endAddress.")]
	string? StopAddress = null,
	[property: Description("The Writable box: required (ticked), excluded (unticked) or any (grey).")]
	ProtectionRequirement? Writable = null,
	[property: Description("The Executable box: required (ticked), excluded (unticked) or any (grey).")]
	ProtectionRequirement? Executable = null,
	[property: Description("The CopyOnWrite box: required (ticked), excluded (unticked) or any (grey).")]
	ProtectionRequirement? CopyOnWrite = null,
	[property: Description(
		"Whether Fast Scan is ticked: a first scan then checks only aligned addresses, or only addresses that end with lastDigits.")]
	bool? FastScan = null,
	[property: Description(
		"The Fast Scan divisor in decimal when fastScan is on in Alignment mode; Cheat Engine shows it in hexadecimal. Cheat Engine resets it to the value type's size (1 for byte, string and bytes, 2 for int16, otherwise 4) whenever the type is set, scan_first on main included, unless the user edited it: read it after scan_first to see what that scan used.")]
	int? Alignment = null,
	[property: Description(
		"The hexadecimal digits that a first scan's addresses must end with, when fastScan is on in Last Digits mode.")]
	string? LastDigits = null,
	[property: Description(
		"Whether Active memory only is ticked: a first scan then reads only the target's working set and skips paged-out memory.")]
	bool? ActiveMemoryOnly = null,
	[property: Description(
		"Whether Pause the game while scanning is ticked: Cheat Engine then suspends the target during each main scan.")]
	bool? PauseWhileScanning = null,
	[property: Description(
		"Whether the Hex box is ticked now; scan_first and scan_next on main set it themselves, ticked only for bytes.")]
	bool? Hexadecimal = null,
	[property: Description(
		"How main compares float and double values with the written value: rounded (Rounded (default)), rounded_extreme (Rounded (extreme), within one unit of its last decimal) or truncated (Truncated, up to one unit above it).")]
	ScanRounding? Rounding = null,
	[property: Description(
		"Whether Simple values only is ticked: float and double scans then drop nonzero values outside about 0.001 to 2048 in magnitude.")]
	bool? SimpleValuesOnly = null,
	[property: Description("Whether Case sensitive is ticked, for string and wstring scans.")]
	bool? CaseSensitive = null,
	[property: Description(
		"Whether Codepage is ticked: string scans then encode the text with the system code page.")]
	bool? CodePage = null,
	[property: Description(
		"The MEM_PRIVATE box of Settings > Scan Settings: whether scans cover memory private to the target, such as heaps. It applies to named scanners too.")]
	bool? MemPrivate = null,
	[property: Description(
		"The MEM_IMAGE box of Settings > Scan Settings: whether scans cover the target's loaded modules. It applies to named scanners too.")]
	bool? MemImage = null,
	[property: Description(
		"The MEM_MAPPED box of Settings > Scan Settings: whether scans cover mapped files and shared memory, where emulators keep guest RAM; unticked by default. It applies to named scanners too, and a Lua script's setSpecialScanOptionsOverride is not shown.")]
	bool? MemMapped = null);

/// <summary>
///     How Cheat Engine compares float and double values; the wire value is the <c>snake_case</c> member name.
/// </summary>
[JsonConverter(typeof(ContractEnumConverter<ScanRounding>))]
public enum ScanRounding
{
	/// <summary>Rounded (default): the memory value, rounded to the decimals of the typed value, equals it.</summary>
	Rounded,

	/// <summary>Rounded (extreme): the memory value is within one unit of the typed value's last decimal.</summary>
	RoundedExtreme,

	/// <summary>
	///     Truncated: the memory value is at least the typed value and less than one unit of its last decimal above.
	/// </summary>
	Truncated
}

/// <summary>The details of a named first scan whose MEM_MAPPED override could not be removed.</summary>
public sealed record ScanMappedOverrideFailure(
	[property: Description("The named scanner that the scan was for.")]
	string ScannerName,
	[property: Description(
		"Whether the scan itself completed; its results can then be read and narrowed as usual.")]
	bool ScanCompleted,
	[property: Description("The scanner's state after the scan, when the scan completed.")]
	ScanState? Status = null);
