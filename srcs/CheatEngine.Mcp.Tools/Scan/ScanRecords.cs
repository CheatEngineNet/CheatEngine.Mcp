using System.ComponentModel;

namespace CheatEngine.Mcp.Tools.Scan;

/// <summary>The live state of one UI or independent value scanner.</summary>
public sealed record ScanStatusResult(
	[property: Description("main for Cheat Engine's visible scanner, or the independent scanner name.")]
	string ScannerName,
	[property: Description("ui for Cheat Engine's visible scanner or independent for a Client scanner.")]
	string Mode,
	[property: Description("The scanner state reported by Cheat Engine or Client.")]
	string State,
	[property: Description("Whether the scanner is currently working.")]
	bool IsScanning,
	[property: Description("Whether result rows can be read or narrowed.")]
	bool ResultsReady,
	[property: Description("The number of result rows when known.")]
	ulong? Count = null,
	[property: Description("The scanned value type when known.")]
	string? ValueType = null,
	[property: Description("The UI target process id, when the scanner is main.")]
	int? ProcessId = null,
	[property: Description("The host error reported by a completed UI scan, when any.")]
	string? Error = null);

/// <summary>One bounded value-scan match.</summary>
public sealed record ScanMatch(
	[property: Description("The matched address, hexadecimal with a 0x prefix.")]
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
	ScanStatusResult[] Scanners);

/// <summary>What an independent scanner reset or deletion did.</summary>
public sealed record ScanReleaseResult(
	[property: Description("The independent scanner name.")]
	string ScannerName,
	[property: Description("Whether the Client session was completely released and removed.")]
	bool Removed,
	[property: Description("Whether a failed release may succeed when retried.")]
	bool Retryable,
	[property: Description("Whether the host needs manual recovery before the session can be forgotten.")]
	bool RequiresManualRecovery,
	[property: Description("The UI scanner's state after reset, when scannerName is main.")]
	ScanStatusResult? Status = null);

/// <summary>What a stop request did to a scanner.</summary>
public sealed record ScanStopResult(
	[property: Description("The scanner name.")]
	string ScannerName,
	[property: Description("Whether cancellation or release was requested from Cheat Engine.")]
	bool StopRequested,
	[property: Description("Whether an independent session was removed.")]
	bool Removed,
	[property: Description("The state observed immediately after a UI cancellation request, when scannerName is main.")]
	ScanStatusResult? Status = null);

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
	string? Error = null);

/// <summary>The bounded result of a fixed Lua page-read script for Cheat Engine's visible scanner.</summary>
public sealed record ScanUiResults(
	string ScannerName,
	string Mode,
	long? Count,
	ScanMatch[] Results,
	long? NextStartIndex,
	bool HasMore);
