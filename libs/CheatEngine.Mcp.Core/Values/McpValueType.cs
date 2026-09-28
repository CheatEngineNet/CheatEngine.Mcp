using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

using CheatEngine.Mcp.Core.Contract;

namespace CheatEngine.Mcp.Core.Values;

/// <summary>The one vocabulary of memory value types used by every tool; values travel as strings.</summary>
[JsonConverter(typeof(ContractEnumConverter<McpValueType>))]
[SuppressMessage("Naming", "CA1720:Identifier contains type name",
	Justification = "Each member names the memory value type it describes; the wire values are the contract.")]
public enum McpValueType
{
	/// <summary>A signed 8-bit integer, in decimal.</summary>
	Int8,

	/// <summary>An unsigned 8-bit integer, in decimal.</summary>
	[JsonStringEnumMemberName("uint8")] UInt8,

	/// <summary>A signed 16-bit integer, in decimal.</summary>
	Int16,

	/// <summary>An unsigned 16-bit integer, in decimal.</summary>
	[JsonStringEnumMemberName("uint16")] UInt16,

	/// <summary>A signed 32-bit integer, in decimal.</summary>
	Int32,

	/// <summary>An unsigned 32-bit integer, in decimal.</summary>
	[JsonStringEnumMemberName("uint32")] UInt32,

	/// <summary>A signed 64-bit integer, in decimal.</summary>
	Int64,

	/// <summary>An unsigned 64-bit integer, in decimal.</summary>
	[JsonStringEnumMemberName("uint64")] UInt64,

	/// <summary>A 32-bit float, round-trippable; <c>NaN</c>, <c>Infinity</c> and <c>-Infinity</c> are names.</summary>
	Float,

	/// <summary>A 64-bit float, round-trippable; <c>NaN</c>, <c>Infinity</c> and <c>-Infinity</c> are names.</summary>
	Double,

	/// <summary>A target pointer at the target's pointer size, as an uppercase hexadecimal address.</summary>
	Pointer,

	/// <summary>UTF-8 text.</summary>
	String,

	/// <summary>UTF-16 text.</summary>
	[JsonStringEnumMemberName("wstring")] WString,

	/// <summary>Raw bytes as spaced uppercase hexadecimal, such as <c>48 8B 05</c>.</summary>
	Bytes
}
