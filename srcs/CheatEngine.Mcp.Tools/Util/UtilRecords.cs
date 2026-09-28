using System.ComponentModel;
using System.Text.Json.Serialization;

using CheatEngine.Mcp.Core.Contract;
using CheatEngine.Mcp.Core.Values;

namespace CheatEngine.Mcp.Tools.Util;

/// <summary>The byte order applied when presenting a scalar conversion.</summary>
[JsonConverter(typeof(ContractEnumConverter<ValueByteOrder>))]
public enum ValueByteOrder
{
	/// <summary>The least significant byte comes first, as target memory normally stores scalar values on Windows.</summary>
	LittleEndian,

	/// <summary>The most significant byte comes first.</summary>
	BigEndian
}

/// <summary>The deterministic conversion of a value through its byte representation.</summary>
/// <param name="SourceType">The type used to parse the input value.</param>
/// <param name="TargetType">The type used to decode the converted bytes.</param>
/// <param name="ByteOrder">The requested order used to present scalar bytes.</param>
/// <param name="PointerSize">The pointer width used for pointer inputs or outputs.</param>
/// <param name="Bytes">The converted bytes, as uppercase hexadecimal pairs separated by spaces.</param>
/// <param name="Value">The target value represented by those bytes.</param>
public sealed record UtilConvertValueResult(
	[property: Description("The value type used to parse the input.")]
	McpValueType SourceType,
	[property: Description("The value type used to decode the converted bytes.")]
	McpValueType TargetType,
	[property:
		Description(
			"The requested order of scalar bytes in bytes; string encodings keep their defined UTF byte order.")]
	ValueByteOrder ByteOrder,
	[property: Description("The pointer width used for pointer inputs or outputs, 4 or 8.")]
	int PointerSize,
	[property: Description("The converted bytes, uppercase hexadecimal pairs separated by spaces.")]
	string Bytes,
	[property: Description("The target value represented by bytes.")]
	string Value);

/// <summary>The result of evaluating one unsigned 64-bit expression.</summary>
/// <param name="UnsignedValue">The resulting 64-bit bit pattern in invariant unsigned decimal.</param>
/// <param name="SignedValue">The same 64-bit bit pattern interpreted as signed two's-complement decimal.</param>
/// <param name="Hex">The same bit pattern as uppercase hexadecimal without <c>0x</c>.</param>
public sealed record UtilCalculateResult(
	[property: Description("The resulting 64-bit bit pattern in invariant unsigned decimal.")]
	string UnsignedValue,
	[property: Description("The same 64-bit bit pattern interpreted as signed two's-complement decimal.")]
	string SignedValue,
	[property: Description("The same bit pattern as uppercase hexadecimal without 0x.")]
	string Hex);
