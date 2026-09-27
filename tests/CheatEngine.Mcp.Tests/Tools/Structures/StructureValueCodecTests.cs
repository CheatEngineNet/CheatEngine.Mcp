using CheatEngine.Mcp.Core.Contract;
using CheatEngine.Mcp.Core.Features;
using CheatEngine.Mcp.Core.Values;
using CheatEngine.Mcp.Tests.Support;
using CheatEngine.Mcp.Tools.Structures;

namespace CheatEngine.Mcp.Tests.Tools.Structures;

/// <summary>The mapping between Cheat Engine's variable types and the contract, and the managed element codec.</summary>
public sealed class StructureValueCodecTests
{
	[Theory]
	[InlineData(McpValueType.Int8, 0, "dtSignedInteger", StructureElementType.Int8)]
	[InlineData(McpValueType.UInt8, 0, "dtUnsignedInteger", StructureElementType.UInt8)]
	[InlineData(McpValueType.Int16, 1, "dtSignedInteger", StructureElementType.Int16)]
	[InlineData(McpValueType.UInt16, 1, "dtUnsignedInteger", StructureElementType.UInt16)]
	[InlineData(McpValueType.Int32, 2, "dtSignedInteger", StructureElementType.Int32)]
	[InlineData(McpValueType.UInt32, 2, "dtUnsignedInteger", StructureElementType.UInt32)]
	[InlineData(McpValueType.Int64, 3, "dtSignedInteger", StructureElementType.Int64)]
	[InlineData(McpValueType.UInt64, 3, "dtUnsignedInteger", StructureElementType.UInt64)]
	[InlineData(McpValueType.Float, 4, null, StructureElementType.Float)]
	[InlineData(McpValueType.Double, 5, null, StructureElementType.Double)]
	[InlineData(McpValueType.String, 6, null, StructureElementType.String)]
	[InlineData(McpValueType.WString, 7, null, StructureElementType.WString)]
	[InlineData(McpValueType.Bytes, 8, null, StructureElementType.Bytes)]
	[InlineData(McpValueType.Pointer, 12, null, StructureElementType.Pointer)]
	public void ValueType_StoredAsVartypeAndDisplay_ReadsBackAsTheSameType(McpValueType type, int vartype,
		string? display, StructureElementType expected)
	{
		Assert.Equal(vartype, StructureValueCodec.Vartype(type));
		Assert.Equal(display, StructureValueCodec.DisplayMethodFor(type, null, "display"));
		Assert.Equal(expected, StructureValueCodec.ElementType(vartype, display));
		Assert.Equal(type.ToString(), expected.ToString());
	}

	[Theory]
	[InlineData(9, StructureElementType.Binary)]
	[InlineData(13, StructureElementType.Custom)]
	[InlineData(10, StructureElementType.Other)]
	[InlineData(14, StructureElementType.Other)]
	public void ElementType_CheatEngineOnlyVartypes_AreFormattedByCheatEngine(int vartype,
		StructureElementType expected)
	{
		Assert.Equal(expected, StructureValueCodec.ElementType(vartype, "dtUnsignedInteger"));
		Assert.False(StructureValueCodec.IsManaged(expected));
		Assert.Null(StructureValueCodec.Display(vartype, "dtHexadecimal"));
	}

	[Fact]
	public void HexDisplay_OfAnIntegerElement_ReadsAsTheUnsignedTypeWithHexDisplay()
	{
		Assert.Equal(StructureElementType.UInt32, StructureValueCodec.ElementType(2, "dtHexadecimal"));
		Assert.Equal(StructureDisplay.Hex, StructureValueCodec.Display(2, "dtHexadecimal"));
		Assert.Equal("dtHexadecimal",
			StructureValueCodec.DisplayMethodFor(McpValueType.UInt32, StructureDisplay.Hex, "display"));
	}

	[Theory]
	[InlineData(McpValueType.Int32, StructureDisplay.Hex)]
	[InlineData(McpValueType.Int32, StructureDisplay.Unsigned)]
	[InlineData(McpValueType.UInt16, StructureDisplay.Signed)]
	[InlineData(McpValueType.Float, StructureDisplay.Hex)]
	[InlineData(McpValueType.Pointer, StructureDisplay.Unsigned)]
	public void DisplayMethodFor_DisplayThatContradictsTheType_IsInvalidArgument(McpValueType type,
		StructureDisplay display)
	{
		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() =>
			StructureValueCodec.DisplayMethodFor(type, display, "elements[0].display"));

		Assert.Equal(ToolErrorKind.InvalidArgument, exception.Error.Kind);
		Assert.StartsWith("elements[0].display:", exception.Error.Message, StringComparison.Ordinal);
	}

	[Theory]
	[InlineData(StructureElementType.Int8, "-5", 1, "FB")]
	[InlineData(StructureElementType.UInt8, "250", 1, "FA")]
	[InlineData(StructureElementType.Int16, "-2", 2, "FE FF")]
	[InlineData(StructureElementType.UInt16, "0x1234", 2, "34 12")]
	[InlineData(StructureElementType.Int32, "-100", 4, "9C FF FF FF")]
	[InlineData(StructureElementType.UInt32, "4294967295", 4, "FF FF FF FF")]
	[InlineData(StructureElementType.Int64, "-9223372036854775808", 8, "00 00 00 00 00 00 00 80")]
	[InlineData(StructureElementType.UInt64, "18446744073709551615", 8, "FF FF FF FF FF FF FF FF")]
	[InlineData(StructureElementType.Float, "1.5", 4, "00 00 C0 3F")]
	[InlineData(StructureElementType.Double, "-0.25", 8, "00 00 00 00 00 00 D0 BF")]
	[InlineData(StructureElementType.Pointer, "7FF6A1B2C3D0", 8, "D0 C3 B2 A1 F6 7F 00 00")]
	[InlineData(StructureElementType.Pointer, "401000", 4, "00 10 40 00")]
	[InlineData(StructureElementType.Bytes, "48 8B 05", 3, "48 8B 05")]
	public void EncodeThenDecode_EveryManagedType_RoundTrips(StructureElementType type, string value, int byteSize,
		string bytes)
	{
		byte[] encoded = StructureValueCodec.Encode(type, value, byteSize);

		Assert.Equal(bytes, HexFormat.Bytes(encoded));
		string expected = type is StructureElementType.UInt16 ? "4660" : value;
		Assert.Equal(expected, StructureValueCodec.Decode(type, encoded));
	}

	[Fact]
	public void Encode_TextShorterThanTheElement_GetsATerminatorAndDecodesUpToIt()
	{
		byte[] text = StructureValueCodec.Encode(StructureElementType.String, "Hero", 8);
		byte[] wide = StructureValueCodec.Encode(StructureElementType.WString, "Hi", 8);

		Assert.Equal("48 65 72 6F 00", HexFormat.Bytes(text));
		Assert.Equal("48 00 69 00 00 00", HexFormat.Bytes(wide));
		Assert.Equal("Hero", StructureValueCodec.Decode(StructureElementType.String, [.. text, 0x41, 0x41, 0x41]));
		Assert.Equal("Hi", StructureValueCodec.Decode(StructureElementType.WString, [.. wide, 0x41, 0x00]));
		Assert.Equal("Hero", StructureValueCodec.Decode(StructureElementType.String, "Hero"u8));
	}

	[Fact]
	public void Encode_ValueLargerThanTheElement_IsInvalidOrLimitExceeded()
	{
		Assert.Equal(ToolErrorKind.InvalidArgument, Assert.Throws<CheatEngineToolException>(() =>
			StructureValueCodec.Encode(StructureElementType.String, "longer than four", 4)).Error.Kind);
		Assert.Equal(ToolErrorKind.LimitExceeded, Assert.Throws<CheatEngineToolException>(() =>
			StructureValueCodec.Encode(StructureElementType.Bytes, "01 02 03", 2)).Error.Kind);
		Assert.Equal(ToolErrorKind.InvalidArgument, Assert.Throws<CheatEngineToolException>(() =>
			StructureValueCodec.Encode(StructureElementType.UInt8, "256", 1)).Error.Kind);
		Assert.Equal(ToolErrorKind.InvalidArgument, Assert.Throws<CheatEngineToolException>(() =>
			StructureValueCodec.Encode(StructureElementType.Float, "fast", 4)).Error.Kind);
	}

	[Fact]
	public void Decode_FewerBytesThanTheType_IsNull()
	{
		Assert.Null(StructureValueCodec.Decode(StructureElementType.Int32, [1, 2]));
		Assert.Null(StructureValueCodec.Decode(StructureElementType.Pointer, [1, 2, 3]));
		Assert.Equal("NaN", StructureValueCodec.Decode(StructureElementType.Float, [0x00, 0x00, 0xC0, 0x7F]));
	}

	[Fact]
	public void Scripts_NameNoGatedApiAndLoadNoCode()
	{
		System.Reflection.FieldInfo[] fields = [.. StructureToolHarness.ScriptFields()];
		Assert.Equal(16, fields.Length);
		foreach (System.Reflection.FieldInfo field in fields)
		{
			string script = (string) field.GetRawConstantValue()!;
			Assert.True(LuaFeatureScan.Scan(script).Length == 0, $"{field.Name} names a gated Cheat Engine API.");
			LuaFixedScriptAssert.NeverLoadsCode(script);
			Assert.DoesNotContain("createStructureFromName", script, StringComparison.Ordinal);
		}
	}
}
