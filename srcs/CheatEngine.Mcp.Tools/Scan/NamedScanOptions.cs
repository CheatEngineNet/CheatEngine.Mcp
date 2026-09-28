using CheatEngine.Client;
using CheatEngine.Client.Scanning;
using CheatEngine.Mcp.Core.Contract;
using CheatEngine.Mcp.Tools.Memory;
using CheatEngine.SDK.Engine.Values;

namespace CheatEngine.Mcp.Tools.Scan;

/// <summary>
///     The region filters of a named first scan: an optional address range, a protection filter, an alignment rule
///     and whether mapped memory is included. Without them a named scan covers the whole address space, unaligned,
///     with no protection filter and with the region kinds of Cheat Engine's settings. The main scanner uses Cheat
///     Engine's own scan settings instead and refuses them.
/// </summary>
/// <param name="StartAddress">The checked start expression, or <see langword="null" /> for no range.</param>
/// <param name="EndAddress">The checked exclusive end expression, or <see langword="null" /> for no range.</param>
/// <param name="Protection">The protection filter; unspecified flags accept either state.</param>
/// <param name="Alignment">The address-alignment rule; <see cref="ScanAlignment.None" /> checks every address.</param>
/// <param name="IncludeMapped">
///     Whether the scan also covers <c>MEM_MAPPED</c> regions, through a Cheat Engine-wide override that lasts only
///     while the scan runs.
/// </param>
internal sealed record NamedScanOptions(
	string? StartAddress,
	string? EndAddress,
	ScanProtectionFilter Protection,
	ScanAlignment Alignment,
	bool IncludeMapped = false)
{
	/// <summary>The largest accepted <c>alignment</c> divisor.</summary>
	internal const int MaximumAlignment = 65536;

	private const string MainHint =
		"Set the range, protection and fast-scan options in Cheat Engine's scan panel, or pass another scannerName for an independent scan.";

	private const string MainMappedHint =
		"Ask the user to tick MEM_MAPPED under Edit > Settings > Scan Settings, or pass another scannerName for an independent scan with includeMapped.";

	/// <summary>Checks the caller's region filters before any dispatch.</summary>
	/// <param name="startAddress">
	///     The first scanned address or expression; requires <paramref name="endAddress" />.
	/// </param>
	/// <param name="endAddress">
	///     The exclusive end address or expression; requires <paramref name="startAddress" />.
	/// </param>
	/// <param name="writable">The requirement on writable memory.</param>
	/// <param name="executable">The requirement on executable memory.</param>
	/// <param name="copyOnWrite">The requirement on copy-on-write memory.</param>
	/// <param name="alignment">The address divisor, or <see langword="null" />.</param>
	/// <param name="lastDigits">The required trailing hexadecimal digits, or <see langword="null" />.</param>
	/// <param name="includeMapped">Whether the scan also covers <c>MEM_MAPPED</c> regions.</param>
	/// <returns>The checked filters.</returns>
	/// <exception cref="CheatEngineToolException">
	///     A filter is refused (<c>invalid_argument</c> or <c>limit_exceeded</c>).
	/// </exception>
	internal static NamedScanOptions Create(string? startAddress, string? endAddress,
		ProtectionRequirement writable, ProtectionRequirement executable, ProtectionRequirement copyOnWrite,
		int? alignment, string? lastDigits, bool includeMapped = false)
	{
		if ((startAddress is null) != (endAddress is null))
		{
			throw CheatEngineToolException.InvalidArgument(startAddress is null ? "startAddress" : "endAddress",
				"startAddress and endAddress must be given together.");
		}

		string? start = startAddress is null ? null : MemoryTargets.RequireExpression(startAddress, "startAddress");
		string? end = endAddress is null ? null : MemoryTargets.RequireExpression(endAddress, "endAddress");
		ScanProtectionFilter protection = new(Requirement(executable, "executable"),
			Requirement(copyOnWrite, "copyOnWrite"), Requirement(writable, "writable"));
		return new NamedScanOptions(start, end, protection, Rule(alignment, lastDigits), includeMapped);
	}

	/// <summary>Refuses every filter for the main scanner, which uses Cheat Engine's own scan settings.</summary>
	/// <remarks>
	///     <c>includeMapped</c> is refused too: main scans run asynchronously, so an override could not be removed when
	///     the call returns without also changing the scans the user starts afterwards.
	/// </remarks>
	/// <exception cref="CheatEngineToolException">
	///     A filter was given (<c>invalid_argument</c> on that parameter).
	/// </exception>
	internal void RequireNone()
	{
		string? parameter = StartAddress is not null ? "startAddress"
			: Protection.Writable != ScanProtectionRequirement.Unspecified ? "writable"
			: Protection.Executable != ScanProtectionRequirement.Unspecified ? "executable"
			: Protection.CopyOnWrite != ScanProtectionRequirement.Unspecified ? "copyOnWrite"
			: Alignment.Mode == ScanAlignmentMode.AlignedTo ? "alignment"
			: Alignment.Mode == ScanAlignmentMode.LastDigits ? "lastDigits"
			: IncludeMapped ? "includeMapped"
			: null;
		if (parameter is not null)
		{
			throw CheatEngineToolException.InvalidArgument(parameter,
				"applies only to named scanners; main uses Cheat Engine's own scan settings.",
				parameter == "includeMapped" ? MainMappedHint : MainHint);
		}
	}

	/// <summary>
	///     Narrows a first-scan request with the filters, resolving the range inside the current dispatch before any
	///     Cheat Engine scan object exists.
	/// </summary>
	/// <param name="client">The activation's Client.</param>
	/// <param name="request">The unfiltered request.</param>
	/// <param name="cancellationToken">The token of the enclosing dispatch body.</param>
	/// <returns>The filtered request.</returns>
	/// <exception cref="CheatEngineToolException">
	///     An expression does not resolve (<c>not_found</c>) or the end is not above the start
	///     (<c>invalid_argument</c>).
	/// </exception>
	internal ValueScanFirstRequest Apply(ICheatEngineClient client, ValueScanFirstRequest request,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(client);
		ValueScanFirstRequest filtered = request.WithProtection(Protection).WithAlignment(Alignment);
		if (StartAddress is null || EndAddress is null)
		{
			return filtered;
		}

		Address start = MemoryTargets.Resolve(client, StartAddress, "startAddress", cancellationToken);
		Address end = MemoryTargets.Resolve(client, EndAddress, "endAddress", cancellationToken);
		return end > start
			? filtered.WithRange(start, end)
			: throw CheatEngineToolException.InvalidArgument("endAddress",
				"must be above startAddress; the range ends before endAddress.");
	}

	private static ScanProtectionRequirement Requirement(ProtectionRequirement requirement, string parameter)
	{
		return requirement switch
		{
			ProtectionRequirement.Required => ScanProtectionRequirement.Required,
			ProtectionRequirement.Excluded => ScanProtectionRequirement.Excluded,
			// Any leaves the flag out of the request, which Cheat Engine documents as either.
			ProtectionRequirement.Any => ScanProtectionRequirement.Unspecified,
			_ => throw CheatEngineToolException.InvalidArgument(parameter, "must be required, excluded or any.")
		};
	}

	private static ScanAlignment Rule(int? alignment, string? lastDigits)
	{
		if (alignment is not null && lastDigits is not null)
		{
			throw CheatEngineToolException.InvalidArgument("lastDigits", "cannot be combined with alignment.");
		}

		if (alignment is { } divisor)
		{
			return ScanAlignment.AlignedTo((int) MemoryTargets.RequireRange(divisor, "alignment", 1,
				MaximumAlignment));
		}

		if (lastDigits is null)
		{
			return ScanAlignment.None;
		}

		string digits = lastDigits.Trim();
		return digits.Length is >= 1 and <= 16 && digits.All(char.IsAsciiHexDigit)
			? ScanAlignment.LastDigits(digits)
			: throw CheatEngineToolException.InvalidArgument("lastDigits", "must be 1 to 16 hexadecimal digits.");
	}
}
