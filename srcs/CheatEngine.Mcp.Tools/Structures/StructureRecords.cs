using System.ComponentModel;

using CheatEngine.Mcp.Core.Values;

namespace CheatEngine.Mcp.Tools.Structures;

/// <summary>A structure's name and size.</summary>
/// <param name="Name">The structure's case-sensitive name.</param>
/// <param name="Size">The bytes from the structure's start to the end of its last element.</param>
/// <param name="ElementCount">The number of elements.</param>
public sealed record StructureSummary(
	[property: Description("The structure's case-sensitive name.")]
	string Name,
	[property: Description("The bytes from the structure's start to the end of its last element.")]
	int Size,
	[property: Description("The number of elements.")]
	int ElementCount);

/// <summary>One page of Cheat Engine's global structures.</summary>
/// <param name="Structures">The structures of this page.</param>
/// <param name="Total">The number of structures that match the filter.</param>
/// <param name="NextOffset">The offset of the next page; omitted when the listing is complete.</param>
/// <param name="Truncated">Whether a host-side cap stopped the enumeration of global structures.</param>
public sealed record StructurePage(
	[property: Description("The structures of this page.")]
	StructureSummary[] Structures,
	[property: Description("The number of structures that match the filter.")]
	int Total,
	[property: Description("The offset of the next page; omitted when the listing is complete.")]
	int? NextOffset,
	[property: Description("Whether a host-side cap stopped the enumeration of global structures.")]
	bool Truncated);

/// <summary>One element of a structure definition.</summary>
/// <param name="Index">The element's zero-based index; elements are ordered by offset.</param>
/// <param name="Offset">The element's offset from the structure's start, as signed hexadecimal.</param>
/// <param name="Name">The element's name; may be empty.</param>
/// <param name="ValueType">The element's value type.</param>
/// <param name="Display">How Cheat Engine displays an integer element; omitted for other types.</param>
/// <param name="ByteSize">The bytes the element occupies.</param>
/// <param name="ChildStructure">The structure a pointer element points to; omitted when none.</param>
/// <param name="ChildStructureStart">Where the pointer lands inside the child structure; detailed format only.</param>
/// <param name="CustomType">The custom type's name of a custom element; detailed format only.</param>
/// <param name="BitStart">The first bit of a binary element; detailed format only.</param>
/// <param name="BitSize">The bit count of a binary element; detailed format only.</param>
public sealed record StructureElement(
	[property: Description("The element's zero-based index; elements are ordered by offset.")]
	int Index,
	[property: Description("The element's offset from the structure's start, as signed hexadecimal.")]
	string Offset,
	[property: Description("The element's name; may be empty.")]
	string Name,
	[property: Description("The element's value type.")]
	StructureElementType ValueType,
	[property: Description("How Cheat Engine displays an integer element; omitted for other types.")]
	StructureDisplay? Display,
	[property: Description("The bytes the element occupies.")]
	int ByteSize,
	[property: Description("The structure a pointer element points to; omitted when none.")]
	string? ChildStructure = null,
	[property: Description("Where the pointer lands inside the child structure, as signed hexadecimal; detailed format only.")]
	string? ChildStructureStart = null,
	[property: Description("The name of a custom element's custom type; detailed format only.")]
	string? CustomType = null,
	[property: Description("The first bit of a binary element; detailed format only.")]
	int? BitStart = null,
	[property: Description("The bit count of a binary element; detailed format only.")]
	int? BitSize = null);

/// <summary>A structure definition with one page of its elements.</summary>
/// <param name="Name">The structure's case-sensitive name.</param>
/// <param name="Size">The bytes from the structure's start to the end of its last element.</param>
/// <param name="ElementCount">The number of elements.</param>
/// <param name="Internal">Whether the structure is internal: selectable as a child but hidden and never saved.</param>
/// <param name="Elements">The elements of this page.</param>
/// <param name="Total">The number of elements in the whole structure.</param>
/// <param name="NextOffset">The offset of the next page; omitted when the listing is complete.</param>
public sealed record StructureDefinition(
	[property: Description("The structure's case-sensitive name.")]
	string Name,
	[property: Description("The bytes from the structure's start to the end of its last element.")]
	int Size,
	[property: Description("The number of elements.")]
	int ElementCount,
	[property: Description("Whether the structure is internal: selectable as a child but hidden and never saved.")]
	bool Internal,
	[property: Description("The elements of this page.")]
	StructureElement[] Elements,
	[property: Description("The number of elements in the whole structure.")]
	int Total,
	[property: Description("The offset of the next page; omitted when the listing is complete.")]
	int? NextOffset);

/// <summary>A deleted structure.</summary>
/// <param name="Name">The deleted structure's name.</param>
public sealed record StructureDeleted(
	[property: Description("The deleted structure's name.")]
	string Name);

/// <summary>A structure after its elements changed.</summary>
/// <param name="Name">The structure's case-sensitive name.</param>
/// <param name="Size">The bytes from the structure's start to the end of its last element.</param>
/// <param name="ElementCount">The number of elements after the change.</param>
/// <param name="Indices">The current indices of the added or updated elements, or the removed indices.</param>
public sealed record StructureChange(
	[property: Description("The structure's case-sensitive name.")]
	string Name,
	[property: Description("The bytes from the structure's start to the end of its last element.")]
	int Size,
	[property: Description("The number of elements after the change.")]
	int ElementCount,
	[property: Description(
		"The indices of the added or updated elements after Cheat Engine reordered them by offset, or the removed indices.")]
	int[] Indices);

/// <summary>What a structure batch completed before it stopped at a failure.</summary>
/// <param name="Applied">The number of items applied before the failure.</param>
/// <param name="FailedIndex">The zero-based position in the batch of the item that failed; it may be partly applied.</param>
/// <param name="Failure">Why Cheat Engine refused the item.</param>
public sealed record StructureBatchFailure(
	[property: Description("The number of items applied before the failure.")]
	int Applied,
	[property: Description("The zero-based position in the batch of the item that failed; it may be partly applied.")]
	int FailedIndex,
	[property: Description("Why Cheat Engine refused the item.")]
	string Failure);

/// <summary>One field of a PDB type.</summary>
/// <param name="Offset">The field's offset, as signed hexadecimal.</param>
/// <param name="Name">The field's name.</param>
/// <param name="ValueType">The field's value type, when Cheat Engine reports one.</param>
public sealed record PdbLayoutElement(
	[property: Description("The field's offset, as signed hexadecimal.")]
	string Offset,
	[property: Description("The field's name.")]
	string Name,
	[property: Description("The field's value type; omitted when Cheat Engine reports none.")]
	StructureElementType? ValueType);

/// <summary>The field layout of a type from the loaded PDB symbols.</summary>
/// <param name="TypeName">The requested type name.</param>
/// <param name="Found">Whether the loaded symbols describe the type.</param>
/// <param name="Elements">The type's fields, in Cheat Engine's order.</param>
/// <param name="Truncated">Whether the type has more fields than the limit.</param>
public sealed record PdbLayout(
	[property: Description("The requested type name.")]
	string TypeName,
	[property: Description("Whether the loaded symbols describe the type.")]
	bool Found,
	[property: Description("The type's fields, in Cheat Engine's order.")]
	PdbLayoutElement[] Elements,
	[property: Description("Whether the type has more fields than the limit.")]
	bool Truncated);

/// <summary>One base address that <c>structure_read</c> read.</summary>
/// <param name="Address">The resolved base address.</param>
/// <param name="ConfirmedLength">The bytes confirmed readable from the page's first element.</param>
public sealed record StructureReadColumn(
	[property: Description("The resolved base address.")]
	string Address,
	[property: Description(
		"The bytes Cheat Engine confirmed readable from the lowest element offset of the page; elements past it read as null.")]
	int ConfirmedLength);

/// <summary>One element read at every base address.</summary>
/// <param name="Index">The element's zero-based index.</param>
/// <param name="Offset">The element's offset, as signed hexadecimal.</param>
/// <param name="Name">The element's name.</param>
/// <param name="ValueType">The element's value type.</param>
/// <param name="Values">The value at each column's address, in column order; null when unreadable.</param>
/// <param name="ByteSize">The bytes the element occupies; detailed format only.</param>
/// <param name="Display">How Cheat Engine displays an integer element; detailed format only.</param>
/// <param name="Raw">The element's bytes at each column's address; detailed format only.</param>
public sealed record StructureReadRow(
	[property: Description("The element's zero-based index.")]
	int Index,
	[property: Description("The element's offset, as signed hexadecimal.")]
	string Offset,
	[property: Description("The element's name.")]
	string Name,
	[property: Description("The element's value type.")]
	StructureElementType ValueType,
	[property: Description(
		"The value at each column's address, in column order; integers in decimal, pointers in hexadecimal, null when unreadable.")]
	string?[] Values,
	[property: Description("The bytes the element occupies; detailed format only.")]
	int? ByteSize = null,
	[property: Description("How Cheat Engine displays an integer element; detailed format only.")]
	StructureDisplay? Display = null,
	[property: Description("The element's bytes at each column's address, null when unreadable; detailed format only.")]
	string?[]? Raw = null);

/// <summary>Structure values read at several base addresses, like Cheat Engine's dissect window.</summary>
/// <param name="Name">The structure's name.</param>
/// <param name="Columns">One column per base address, in request order.</param>
/// <param name="Elements">One row per element of this page.</param>
/// <param name="Total">The number of elements in the offset range.</param>
/// <param name="NextOffset">The offset of the next page; omitted when the listing is complete.</param>
public sealed record StructureReadResult(
	[property: Description("The structure's name.")]
	string Name,
	[property: Description("One column per base address, in request order.")]
	StructureReadColumn[] Columns,
	[property: Description("One row per element of this page.")]
	StructureReadRow[] Elements,
	[property: Description("The number of elements in the offset range.")]
	int Total,
	[property: Description("The offset of the next page; omitted when the listing is complete.")]
	int? NextOffset);

/// <summary>A structure element written and read back.</summary>
/// <param name="Address">The element's address.</param>
/// <param name="Size">The bytes written.</param>
/// <param name="Value">The value read back; omitted when the read-back failed.</param>
public sealed record StructureWriteResult(
	[property: Description("The element's address: the base address plus the element's offset.")]
	string Address,
	[property: Description("The bytes written.")]
	int Size,
	[property: Description("The value read back after the write; omitted when the read-back failed.")]
	string? Value);

/// <summary>One compared field.</summary>
/// <param name="Offset">The field's offset, as signed hexadecimal.</param>
/// <param name="Size">The field's size in bytes.</param>
/// <param name="Classification">What the field shows across the groups.</param>
/// <param name="ValuesA">The field in each instance of group A; null when unreadable.</param>
/// <param name="ValuesB">The field in each instance of group B; null when unreadable.</param>
/// <param name="Name">The element's name when comparing along a structure; omitted otherwise.</param>
public sealed record StructureCompareRow(
	[property: Description("The field's offset, as signed hexadecimal.")]
	string Offset,
	[property: Description("The field's size in bytes.")]
	int Size,
	[property: Description("What the field shows across the groups.")]
	StructureFieldClassification Classification,
	[property: Description("The field in each instance of group A, in request order; null when unreadable.")]
	string?[] ValuesA,
	[property: Description("The field in each instance of group B, in request order; null when unreadable.")]
	string?[] ValuesB,
	[property: Description("The element's name when comparing along a structure; omitted otherwise.")]
	string? Name = null);

/// <summary>The fields that tell two groups of structure instances apart.</summary>
/// <param name="GroupA">The resolved addresses of group A.</param>
/// <param name="GroupB">The resolved addresses of group B.</param>
/// <param name="Rows">The rows of this page.</param>
/// <param name="Total">The number of rows the mode selects.</param>
/// <param name="NextOffset">The offset of the next page; omitted when the listing is complete.</param>
public sealed record StructureComparison(
	[property: Description("The resolved addresses of group A.")]
	string[] GroupA,
	[property: Description("The resolved addresses of group B.")]
	string[] GroupB,
	[property: Description("The rows of this page.")]
	StructureCompareRow[] Rows,
	[property: Description("The number of rows the mode selects.")]
	int Total,
	[property: Description("The offset of the next page; omitted when the listing is complete.")]
	int? NextOffset);

/// <summary>A new structure element.</summary>
/// <param name="Offset">The offset from the structure's start, as signed hexadecimal.</param>
/// <param name="Name">The element's name, up to 256 characters; may be empty.</param>
/// <param name="ValueType">The element's value type.</param>
/// <param name="Display">How to display an integer element; defaults to its signedness.</param>
/// <param name="ByteSize">The byte size of a string, wstring or bytes element.</param>
/// <param name="ChildStructure">The structure a pointer element points to.</param>
/// <param name="ChildStructureStart">Where the pointer lands inside the child structure.</param>
public sealed record StructureElementSpec(
	[property: Description("The offset from the structure's start, as signed hexadecimal such as 4C8 or -8.")]
	string Offset,
	[property: Description("The element's name, up to 256 characters; may be empty.")]
	string Name,
	[property: Description("The element's value type.")]
	McpValueType ValueType,
	[property: Description(
		"How to display an integer element: signed for intN, unsigned or hex for uintN; defaults to the type's signedness.")]
	StructureDisplay? Display = null,
	[property: Description("The byte size of a string, wstring or bytes element (1-65536); required for them, refused otherwise.")]
	int? ByteSize = null,
	[property: Description("The name of the structure a pointer element points to; pointer elements only.")]
	string? ChildStructure = null,
	[property: Description("Where the pointer lands inside the child structure, as signed hexadecimal; default 0.")]
	string? ChildStructureStart = null);

/// <summary>Changes to one existing structure element.</summary>
/// <param name="Index">The element's zero-based index before this call.</param>
/// <param name="Offset">A new offset, as signed hexadecimal.</param>
/// <param name="Name">A new name, up to 256 characters.</param>
/// <param name="ValueType">A new value type.</param>
/// <param name="Display">A new display for an integer element.</param>
/// <param name="ByteSize">A new byte size for a string, wstring or bytes element.</param>
public sealed record StructureElementUpdate(
	[property: Description("The element's zero-based index before this call, from structure_get.")]
	int Index,
	[property: Description("A new offset, as signed hexadecimal; omit to keep it.")]
	string? Offset = null,
	[property: Description("A new name, up to 256 characters; omit to keep it.")]
	string? Name = null,
	[property: Description("A new value type; omit to keep it. String, wstring and bytes also need byteSize.")]
	McpValueType? ValueType = null,
	[property: Description("A new display for an integer element; omit to keep it or to follow a new valueType.")]
	StructureDisplay? Display = null,
	[property: Description("A new byte size (1-65536) for a string, wstring or bytes element; omit to keep it.")]
	int? ByteSize = null);
