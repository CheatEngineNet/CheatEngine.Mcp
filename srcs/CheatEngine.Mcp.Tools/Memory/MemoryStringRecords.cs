using System.ComponentModel;
using System.Text.Json.Serialization;

using CheatEngine.Mcp.Core.Contract;

namespace CheatEngine.Mcp.Tools.Memory;

/// <summary>The byte encoding used by <c>memory_find_strings</c>.</summary>
[JsonConverter(typeof(ContractEnumConverter<MemoryStringSearchEncoding>))]
public enum MemoryStringSearchEncoding
{
	/// <summary>UTF-8 text.</summary>
	[JsonStringEnumMemberName("utf8")] Utf8,

	/// <summary>Little-endian UTF-16 text.</summary>
	[JsonStringEnumMemberName("utf16_le")] Utf16Le
}

/// <summary>One bounded page of printable strings found in a memory range.</summary>
/// <param name="Address">The resolved start address of the requested range.</param>
/// <param name="Size">The exact requested byte count.</param>
/// <param name="Encoding">The encoding used to decode the range.</param>
/// <param name="Total">The total matching runs in the requested range before paging.</param>
/// <param name="NextOffset">The matching-run offset for the next page, when this page did not include every match.</param>
/// <param name="Truncated">Whether paging omitted later matching runs.</param>
/// <param name="Strings">The matching runs in requested-range order.</param>
public sealed record MemoryStringFindResult(
	[property: Description("The resolved start address of the requested range, uppercase hexadecimal without 0x.")]
	string Address,
	[property: Description("The exact requested byte count that was read once from the selected target.")]
	int Size,
	[property: Description("The encoding used to decode the requested range.")]
	MemoryStringSearchEncoding Encoding,
	[property: Description("The total matching printable runs in the requested range before offset and limit paging.")]
	int Total,
	[property: Description("The matching-run offset for the next page, omitted when this page includes every match.")]
	int? NextOffset,
	[property: Description("Whether offset and limit omitted later matching runs; this does not describe bytes outside the requested range.")]
	bool Truncated,
	[property: Description("The matching printable runs in requested-range order. A run touching a range edge can continue outside the requested range.")]
	MemoryStringMatch[] Strings);

/// <summary>One printable Unicode run found in the requested byte range.</summary>
/// <param name="Address">The exact target address of the run's first byte.</param>
/// <param name="ByteLength">The full byte length of the run, before preview truncation.</param>
/// <param name="Text">The first at most 256 Unicode scalar values of the run, never split through a surrogate pair.</param>
/// <param name="PreviewTruncated">Whether <paramref name="Text" /> omits later characters of the run.</param>
/// <param name="StartsAtRangeBoundary">Whether the run begins at the requested range's first byte and might start earlier.</param>
/// <param name="EndsAtRangeBoundary">Whether the run ends at the requested range's last byte and might continue later.</param>
public sealed record MemoryStringMatch(
	[property: Description("The exact target address of the run's first byte, uppercase hexadecimal without 0x.")]
	string Address,
	[property: Description("The full byte length of the run before preview truncation.")]
	int ByteLength,
	[property: Description("The first at most 256 Unicode scalar values of the run, never split through a surrogate pair.")]
	string Text,
	[property: Description("Whether text omits later characters of the run.")]
	bool PreviewTruncated,
	[property: Description("Whether the run begins at the requested range's first byte and might start before that range.")]
	bool StartsAtRangeBoundary,
	[property: Description("Whether the run ends at the requested range's last byte and might continue after that range.")]
	bool EndsAtRangeBoundary);
