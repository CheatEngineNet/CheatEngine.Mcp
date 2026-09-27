using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

using CheatEngine.Mcp.Core.Contract;

namespace CheatEngine.Mcp.Tools.Structures;

/// <summary>
///     The value type of a structure element as Cheat Engine stores it: the contract's value types, plus the element
///     kinds that only Cheat Engine's own editors create.
/// </summary>
[JsonConverter(typeof(ContractEnumConverter<StructureElementType>))]
[SuppressMessage("Naming", "CA1720:Identifier contains type name",
	Justification = "Each member names the memory value type it describes; the wire values are the contract.")]
public enum StructureElementType
{
	/// <summary>A signed 8-bit integer.</summary>
	Int8,

	/// <summary>An unsigned 8-bit integer.</summary>
	[JsonStringEnumMemberName("uint8")] UInt8,

	/// <summary>A signed 16-bit integer.</summary>
	Int16,

	/// <summary>An unsigned 16-bit integer.</summary>
	[JsonStringEnumMemberName("uint16")] UInt16,

	/// <summary>A signed 32-bit integer.</summary>
	Int32,

	/// <summary>An unsigned 32-bit integer.</summary>
	[JsonStringEnumMemberName("uint32")] UInt32,

	/// <summary>A signed 64-bit integer.</summary>
	Int64,

	/// <summary>An unsigned 64-bit integer.</summary>
	[JsonStringEnumMemberName("uint64")] UInt64,

	/// <summary>A 32-bit float.</summary>
	Float,

	/// <summary>A 64-bit float.</summary>
	Double,

	/// <summary>A target pointer.</summary>
	Pointer,

	/// <summary>UTF-8 text of the element's byte size.</summary>
	String,

	/// <summary>UTF-16 text of the element's byte size.</summary>
	[JsonStringEnumMemberName("wstring")] WString,

	/// <summary>Raw bytes of the element's byte size.</summary>
	Bytes,

	/// <summary>A bit field; Cheat Engine formats its value.</summary>
	Binary,

	/// <summary>A custom type registered in Cheat Engine; Cheat Engine formats its value.</summary>
	Custom,

	/// <summary>Any other Cheat Engine variable type; Cheat Engine formats its value.</summary>
	Other
}

/// <summary>How Cheat Engine displays an integer element; it also carries the element's signedness.</summary>
[JsonConverter(typeof(ContractEnumConverter<StructureDisplay>))]
[SuppressMessage("Naming", "CA1720:Identifier contains type name",
	Justification = "Each member names the value format it describes; the wire values are the contract.")]
public enum StructureDisplay
{
	/// <summary>Unsigned decimal.</summary>
	Unsigned,

	/// <summary>Signed decimal.</summary>
	Signed,

	/// <summary>Hexadecimal; the element reads as its unsigned type.</summary>
	Hex
}

/// <summary>Which rows <c>structure_compare</c> returns.</summary>
[JsonConverter(typeof(ContractEnumConverter<StructureCompareMode>))]
public enum StructureCompareMode
{
	/// <summary>Rows constant inside each group but different between the groups.</summary>
	Discriminate,

	/// <summary>Rows equal in every instance.</summary>
	Constant,

	/// <summary>Rows that are not equal in every instance.</summary>
	Differs,

	/// <summary>Every row.</summary>
	All
}

/// <summary>How <c>structure_compare</c> formats the cells of a raw byte range.</summary>
[JsonConverter(typeof(ContractEnumConverter<StructureCompareFormat>))]
[SuppressMessage("Naming", "CA1720:Identifier contains type name",
	Justification = "Each member names the value format it describes; the wire values are the contract.")]
public enum StructureCompareFormat
{
	/// <summary>Pick per row: pointers and floats when every value looks like one, integers otherwise.</summary>
	Auto,

	/// <summary>Signed decimal integers.</summary>
	Signed,

	/// <summary>Unsigned decimal integers.</summary>
	Unsigned,

	/// <summary>Floats for a granularity of 4 and doubles for 8.</summary>
	Float,

	/// <summary>Raw bytes as spaced hexadecimal.</summary>
	Hex
}

/// <summary>What one compared row shows across the two groups.</summary>
[JsonConverter(typeof(ContractEnumConverter<StructureFieldClassification>))]
public enum StructureFieldClassification
{
	/// <summary>Equal in every instance of both groups.</summary>
	Constant,

	/// <summary>Constant inside each group, different between the groups.</summary>
	Discriminator,

	/// <summary>Varies inside group A, constant inside group B.</summary>
	VariesA,

	/// <summary>Constant inside group A, varies inside group B.</summary>
	VariesB,

	/// <summary>Varies inside both groups.</summary>
	VariesBoth,

	/// <summary>At least one instance could not be read.</summary>
	Unreadable
}
