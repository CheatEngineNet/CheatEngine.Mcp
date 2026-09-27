namespace CheatEngine.Mcp.Tools.Structures;

/// <summary>One element as the fixed scripts copy it: Cheat Engine's raw fields, mapped to the contract in managed code.</summary>
/// <param name="Index">The zero-based index.</param>
/// <param name="Offset">The offset from the structure's start.</param>
/// <param name="Name">The name; <see langword="null" /> reads as empty.</param>
/// <param name="Vartype">Cheat Engine's variable type (<c>vtByte</c> 0 ... <c>vtPointer</c> 12, <c>vtCustom</c> 13).</param>
/// <param name="Display">Cheat Engine's display method, such as <c>dtSignedInteger</c>.</param>
/// <param name="ByteSize">The bytes the element occupies.</param>
/// <param name="ChildStructure">The name of the child structure of a pointer element.</param>
/// <param name="ChildStructureStart">Where the pointer lands inside the child structure.</param>
/// <param name="CustomType">The custom type's name of a custom element.</param>
/// <param name="BitStart">The first bit of a binary element.</param>
/// <param name="BitSize">The bit count of a binary element.</param>
internal sealed record StructureLuaElement(
	int Index,
	long Offset,
	string? Name,
	int Vartype,
	string? Display,
	int ByteSize,
	string? ChildStructure = null,
	long? ChildStructureStart = null,
	string? CustomType = null,
	int? BitStart = null,
	int? BitSize = null);

/// <summary>A structure with one page of its elements, as the fixed scripts copy it.</summary>
/// <param name="Name">The structure's name.</param>
/// <param name="Size">The structure's size.</param>
/// <param name="ElementCount">The structure's element count.</param>
/// <param name="Internal">Whether the structure is internal.</param>
/// <param name="Elements">The elements of this page.</param>
/// <param name="Total">The number of elements the page was cut from.</param>
/// <param name="NextOffset">The offset of the next page, or <see langword="null" /> when complete.</param>
internal sealed record StructureLuaDefinition(
	string Name,
	int Size,
	int ElementCount,
	bool Internal,
	StructureLuaElement[] Elements,
	int Total,
	int? NextOffset);

/// <summary>One element of a structure, looked up by index.</summary>
/// <param name="Name">The structure's name.</param>
/// <param name="Element">The element.</param>
internal sealed record StructureLuaElementRef(string Name, StructureLuaElement Element);

/// <summary>What an element batch did, or where it stopped.</summary>
/// <param name="Name">The structure's name.</param>
/// <param name="Size">The structure's size afterwards.</param>
/// <param name="ElementCount">The element count afterwards.</param>
/// <param name="Indices">The indices of the affected elements.</param>
/// <param name="Applied">The number of batch items applied.</param>
/// <param name="FailedIndex">The batch position that failed, or <see langword="null" /> when every item applied.</param>
/// <param name="Failure">Cheat Engine's error for the failed item.</param>
internal sealed record StructureLuaBatch(
	string Name,
	int Size,
	int ElementCount,
	int[] Indices,
	int Applied,
	int? FailedIndex = null,
	string? Failure = null);

/// <summary>One field of a PDB type as <c>getStructureElementsFromName</c> reports it.</summary>
/// <param name="Offset">The field's offset.</param>
/// <param name="Name">The field's name.</param>
/// <param name="Vartype">Cheat Engine's variable type, when reported.</param>
internal sealed record StructureLuaPdbElement(long Offset, string? Name, int? Vartype);

/// <summary>The PDB layout copy.</summary>
/// <param name="Found">Whether the loaded symbols describe the type.</param>
/// <param name="Elements">The copied fields.</param>
/// <param name="Truncated">Whether more fields exist than were copied.</param>
internal sealed record StructureLuaPdbLayout(bool Found, StructureLuaPdbElement[] Elements, bool Truncated);

/// <summary>Values that Cheat Engine formatted, one row per element and one entry per base address.</summary>
/// <param name="Values">The values; <see langword="null" /> when unreadable.</param>
internal sealed record StructureLuaValues(string?[][] Values);

/// <summary>An element value written by Cheat Engine and read back.</summary>
/// <param name="Value">The value read back, or <see langword="null" /> when unreadable.</param>
internal sealed record StructureLuaWritten(string? Value);
