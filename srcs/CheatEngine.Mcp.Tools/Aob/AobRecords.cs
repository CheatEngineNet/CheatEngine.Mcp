using System.ComponentModel;
using System.Text.Json.Serialization;

using CheatEngine.Mcp.Core.Contract;
using CheatEngine.Mcp.Core.Values;

namespace CheatEngine.Mcp.Tools.Aob;

/// <summary>What <c>aob_find</c> found, one entry per pattern.</summary>
/// <param name="Results">One entry per requested pattern, in request order.</param>
public sealed record AobFindResult(
	[property: Description("One entry per requested pattern, in request order.")]
	AobPatternResult[] Results);

/// <summary>The matches of one pattern.</summary>
/// <param name="Pattern">The normalized pattern.</param>
/// <param name="Count">How many matches are listed.</param>
/// <param name="Exact">Whether the count is proven to be every match in scope.</param>
/// <param name="Unique">Whether exactly one match exists in scope.</param>
/// <param name="Scope">What Cheat Engine actually scanned.</param>
/// <param name="TargetVerified">Whether the matches belong to one confirmed target incarnation.</param>
/// <param name="ElapsedMs">How long the scan and its copy took.</param>
/// <param name="Matches">The match addresses.</param>
public sealed record AobPatternResult(
	[property: Description("The pattern as scanned: uppercase byte pairs and ?? wildcards separated by single spaces.")]
	string Pattern,
	[property: Description("How many matches are listed.")]
	int Count,
	[property: Description(
		"Whether count is proven to be every match in scope; false when the limit cut the list or Cheat Engine cannot tell zero matches from a failure.")]
	bool Exact,
	[property: Description("Whether exactly one match exists in scope: exact and count 1. Use a limit of at least 2.")]
	bool Unique,
	[property: Description(
		"What Cheat Engine scanned: bounded_scan (only the module or range), global_scan, filtered_global_scan (whole target, then filtered) or unknown.")]
	AobScanScope Scope,
	[property: Description("Whether the matches belong to one confirmed target incarnation for the whole scan.")]
	bool TargetVerified,
	[property: Description("How long Cheat Engine's scan and the copy of its result took, in milliseconds.")]
	int ElapsedMs,
	[property: Description("The match addresses in Cheat Engine's order, uppercase hexadecimal without 0x.")]
	string[] Matches);

/// <summary>What <c>aob_find_value</c> found: the encoded value's pattern and its matches.</summary>
/// <param name="ValueType">The value type the value was encoded as.</param>
/// <param name="Value">The value text as given.</param>
/// <param name="Result">The pattern the value encoded to and its matches.</param>
public sealed record AobValueResult(
	[property: Description("The value type the value was encoded as.")]
	McpValueType ValueType,
	[property: Description("The value text as given.")]
	string Value,
	[property: Description(
		"The scan: pattern holds the encoded little-endian bytes as scanned, then the matches as aob_find reports them.")]
	AobPatternResult Result);

/// <summary>What part of the target an AOB scan covered; the wire value is the <c>snake_case</c> member name.</summary>
[JsonConverter(typeof(ContractEnumConverter<AobScanScope>))]
public enum AobScanScope
{
	/// <summary>Cheat Engine scanned the whole target.</summary>
	GlobalScan,

	/// <summary>Cheat Engine scanned only the requested module or range.</summary>
	BoundedScan,

	/// <summary>Cheat Engine scanned the whole target and the module or range filtered the copied matches.</summary>
	FilteredGlobalScan,

	/// <summary>The scope was not reported.</summary>
	Unknown
}

/// <summary>A generated AOB signature for one address.</summary>
/// <param name="Address">The resolved address.</param>
/// <param name="Module">The module that was scanned.</param>
/// <param name="Generator">Which generator built the signature.</param>
/// <param name="Unique">Whether the signature matches only once.</param>
/// <param name="Verified">Whether a bounded scan checked the signature.</param>
/// <param name="Pattern">The signature.</param>
/// <param name="PatternStart">Where a match of the signature begins.</param>
/// <param name="Offset">The distance from the match start to the address.</param>
/// <param name="Length">The signature's length in bytes.</param>
/// <param name="MatchCount">How many matches the verification scan found.</param>
/// <param name="TriedPattern">The last pattern tried when no signature was generated.</param>
public sealed record AobSignature(
	[property: Description("The resolved address, uppercase hexadecimal without 0x.")]
	string Address,
	[property: Description("The module that contains the address and was scanned.")]
	string Module,
	[property: Description(
		"Which generator ran: cheat_engine (Cheat Engine's getUniqueAOB, for a module of at most 64 MiB) or managed (whole instructions decoded forward from the address and extended until a module scan proves the pattern unique, for a larger module).")]
	AobSignatureGenerator Generator,
	[property: Description(
		"Whether the signature matches only once in the module: proven by a bounded module scan when verified is true.")]
	bool Unique,
	[property: Description(
		"Whether a bounded scan of the module checked the signature; always true for a managed signature, whose scans build it, and false when verify is false or no signature was generated.")]
	bool Verified,
	[property: Description(
		"The signature, uppercase byte pairs and ?? wildcards; omitted when no signature was generated.")]
	string? Pattern = null,
	[property: Description("Where a match of the signature begins, uppercase hexadecimal: the address minus offset.")]
	string? PatternStart = null,
	[property: Description(
		"The distance in bytes from patternStart to the address; inject at match+offset. Always 0 for a managed signature, which starts at the address.")]
	int? Offset = null,
	[property: Description("The signature's length in bytes.")]
	int? Length = null,
	[property:
		Description("How many matches the verification scan found; 2 means at least 2. Omitted when not verified.")]
	int? MatchCount = null,
	[property: Description(
		"The last pattern tried when no signature was generated (unique is false and pattern is omitted), which matches more than once: the instruction bytes at the address that Cheat Engine reported, or the managed generator's longest pattern, at most 64 bytes.")]
	string? TriedPattern = null);

/// <summary>
///     Which generator built an <c>aob_generate_signature</c> result; the wire value is the <c>snake_case</c>
///     member name.
/// </summary>
[JsonConverter(typeof(ContractEnumConverter<AobSignatureGenerator>))]
public enum AobSignatureGenerator
{
	/// <summary>Cheat Engine's <c>getUniqueAOB</c>, for a module of at most 64 MiB.</summary>
	CheatEngine,

	/// <summary>The server's generator, which decodes whole instructions forward from the address.</summary>
	Managed
}

/// <summary>What the fixed <c>getUniqueAOB</c> script reports.</summary>
/// <param name="Found">Whether Cheat Engine returned a signature.</param>
/// <param name="Pattern">Cheat Engine's signature, with its own wildcards.</param>
/// <param name="Offset">The distance from the signature's start to the address.</param>
/// <param name="Tried">Cheat Engine's last attempt, when its error names one.</param>
public sealed record UniqueAobProbe(bool Found, string? Pattern = null, long? Offset = null, string? Tried = null);

/// <summary>
///     The details of an <c>aob_find</c> or <c>aob_find_value</c> call with <c>includeMapped</c> whose MEM_MAPPED
///     scan override could not be removed.
/// </summary>
/// <param name="ScanCompleted">Whether every scan of the call completed.</param>
/// <param name="Results">What the scans found, when they completed.</param>
public sealed record AobMappedOverrideFailure(
	[property: Description(
		"Whether every scan of the call completed; results then holds what they found.")]
	bool ScanCompleted,
	[property: Description(
		"What the scans found when they completed, as the tool reports it: one entry per pattern for aob_find, in request order, and the one entry of aob_find_value.")]
	AobPatternResult[]? Results = null);
