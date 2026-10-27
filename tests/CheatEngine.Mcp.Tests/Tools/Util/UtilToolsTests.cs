using CheatEngine.Mcp.Core.Contract;
using CheatEngine.Mcp.Core.Values;
using CheatEngine.Mcp.Tools.Util;

namespace CheatEngine.Mcp.Tests.Tools.Util;

/// <summary>Pure conversion and expression behaviour of the <c>util_*</c> tools.</summary>
public sealed class UtilToolsTests
{
	[Fact]
	public void ConvertValue_Scalar_UsesTheRequestedByteOrderAndTargetInterpretation()
	{
		UtilConvertValueResult result = UtilTools.ConvertValue("0x12345678", McpValueType.UInt32,
			McpValueType.UInt32, ValueByteOrder.BigEndian);

		Assert.Equal((McpValueType.UInt32, McpValueType.UInt32, ValueByteOrder.BigEndian, 8, "12 34 56 78",
			"305419896"), (result.SourceType, result.TargetType, result.ByteOrder, result.PointerSize, result.Bytes,
			result.Value));
	}

	[Fact]
	public void ConvertValue_RawBytes_DecodesTheTargetBitPattern()
	{
		UtilConvertValueResult result = UtilTools.ConvertValue("FE FF", McpValueType.Bytes, McpValueType.Int16);

		Assert.Equal(("FE FF", "-2"), (result.Bytes, result.Value));
	}

	[Theory]
	[MemberData(nameof(FixedScalarDecodingCases))]
	public void ConvertValue_RawBytes_DecodesEachFixedScalarType(string bytes, McpValueType targetType,
		int pointerSize, string expected)
	{
		UtilConvertValueResult result = UtilTools.ConvertValue(bytes, McpValueType.Bytes, targetType,
			ValueByteOrder.LittleEndian, pointerSize);

		Assert.Equal((bytes, expected), (result.Bytes, result.Value));
	}

	[Fact]
	public void ConvertValue_BigEndianRawBytes_UsesTheRequestedPresentationOrder()
	{
		UtilConvertValueResult result = UtilTools.ConvertValue("00 80", McpValueType.Bytes, McpValueType.Int16,
			ValueByteOrder.BigEndian);

		Assert.Equal(("80 00", "-32768"), (result.Bytes, result.Value));
	}

	[Theory]
	[MemberData(nameof(TextConversionCases))]
	public void ConvertValue_TextEncoding_PreservesItsDefinedByteOrder(McpValueType sourceType, string value,
		McpValueType targetType, ValueByteOrder byteOrder, string expectedBytes, string expectedValue)
	{
		UtilConvertValueResult result = UtilTools.ConvertValue(value, sourceType, targetType,
			byteOrder);

		Assert.Equal((expectedBytes, expectedValue), (result.Bytes, result.Value));
	}

	[Fact]
	public void ConvertValue_MismatchedScalarWidths_IsInvalidArgument()
	{
		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() =>
			UtilTools.ConvertValue("1", McpValueType.Int8, McpValueType.Int32));

		Assert.Equal(ToolErrorKind.InvalidArgument, exception.Error.Kind);
		Assert.StartsWith("targetType", exception.Error.Message, StringComparison.Ordinal);
	}

	[Theory]
	[InlineData(0)]
	[InlineData(16)]
	public void ConvertValue_InvalidPointerWidth_IsRejectedBeforeConversion(int pointerSize)
	{
		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() =>
			UtilTools.ConvertValue("1", McpValueType.UInt8, McpValueType.UInt8, pointerSize: pointerSize));

		Assert.Equal(ToolErrorKind.InvalidArgument, exception.Error.Kind);
		Assert.Equal("""{"parameter":"pointerSize"}""", exception.Error.Details!.Value.GetRawText());
	}

	[Fact]
	public void ConvertValue_OddUtf16ByteCount_IsReportedAsATargetTypeError()
	{
		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() =>
			UtilTools.ConvertValue("41", McpValueType.Bytes, McpValueType.WString));

		Assert.Equal(ToolErrorKind.InvalidArgument, exception.Error.Kind);
		Assert.Equal("""{"parameter":"targetType"}""", exception.Error.Details!.Value.GetRawText());
		Assert.Contains("even number of bytes", exception.Error.Message, StringComparison.Ordinal);
	}

	[Theory]
	[InlineData("FF", McpValueType.String)]
	[InlineData("C2", McpValueType.String)]
	[InlineData("00 D8", McpValueType.WString)]
	[InlineData("00 DC", McpValueType.WString)]
	public void ConvertValue_MalformedEncodedBytes_IsAStructuredTargetTypeError(string bytes, McpValueType targetType)
	{
		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() =>
			UtilTools.ConvertValue(bytes, McpValueType.Bytes, targetType));

		Assert.Equal((ToolErrorKind.InvalidArgument, ToolHostEffect.NotStarted),
			(exception.Error.Kind, exception.Error.HostEffect));
		Assert.Equal("""{"parameter":"targetType"}""", exception.Error.Details!.Value.GetRawText());
		Assert.Contains("valid text", exception.Error.Message, StringComparison.Ordinal);
	}

	[Theory]
	[InlineData("0xFFFFFFFFFFFFFFFF + 2", "1", "1", "1")]
	[InlineData("0x8000000000000000 >> 1", "4611686018427387904", "4611686018427387904", "4000000000000000")]
	[InlineData("(1 << 63) + (1 << 63)", "0", "0", "0")]
	[InlineData("~0", "18446744073709551615", "-1", "FFFFFFFFFFFFFFFF")]
	[InlineData("7 * 6", "42", "42", "2A")]
	[InlineData("7 - 9", "18446744073709551614", "-2", "FFFFFFFFFFFFFFFE")]
	public void Calculate_UsesUnsigned64BitWraparound(string expression, string unsignedValue, string signedValue,
		string hex)
	{
		UtilCalculateResult result = UtilTools.Calculate(expression);

		Assert.Equal((unsignedValue, signedValue, hex), (result.UnsignedValue, result.SignedValue, result.Hex));
	}

	[Theory]
	[MemberData(nameof(OperatorPrecedenceCases))]
	public void Calculate_UsesDocumentedOperatorPrecedenceAndShiftMasking(string expression, string expected)
	{
		UtilCalculateResult result = UtilTools.Calculate(expression);

		Assert.Equal(expected, result.UnsignedValue);
	}

	[Theory]
	[InlineData("1 / 0")]
	[InlineData("0x")]
	[InlineData("1 +")]
	public void Calculate_InvalidExpression_IsInvalidArgument(string expression)
	{
		CheatEngineToolException exception =
			Assert.Throws<CheatEngineToolException>(() => UtilTools.Calculate(expression));

		Assert.Equal(ToolErrorKind.InvalidArgument, exception.Error.Kind);
		Assert.StartsWith("expression", exception.Error.Message, StringComparison.Ordinal);
	}

	[Theory]
	[MemberData(nameof(InvalidExpressionCases))]
	public void Calculate_InvalidExpression_ReportsTheSpecificRejectedConstruct(string expression, string detail)
	{
		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() => UtilTools.Calculate(expression));

		Assert.Equal(ToolErrorKind.InvalidArgument, exception.Error.Kind);
		Assert.Equal("""{"parameter":"expression"}""", exception.Error.Details!.Value.GetRawText());
		Assert.Contains(detail, exception.Error.Message, StringComparison.Ordinal);
	}

	[Fact]
	public void Calculate_ExcessiveInputAndNesting_AreRefusedWithTheirOwnErrors()
	{
		CheatEngineToolException length = Assert.Throws<CheatEngineToolException>(() =>
			UtilTools.Calculate(new string('0', 4097)));
		CheatEngineToolException depth = Assert.Throws<CheatEngineToolException>(() =>
			UtilTools.Calculate(new string('(', 65) + "1" + new string(')', 65)));

		Assert.Equal(ToolErrorKind.LimitExceeded, length.Error.Kind);
		Assert.Contains("at most 4096", length.Error.Message, StringComparison.Ordinal);
		Assert.Equal(ToolErrorKind.InvalidArgument, depth.Error.Kind);
		Assert.Contains("more than 64 nested parentheses", depth.Error.Message, StringComparison.Ordinal);
	}

	public static TheoryData<string, McpValueType, int, string> FixedScalarDecodingCases()
	{
		return new TheoryData<string, McpValueType, int, string>
		{
			{ "80", McpValueType.Int8, 8, "-128" },
			{ "FF", McpValueType.UInt8, 8, "255" },
			{ "CD AB", McpValueType.UInt16, 8, "43981" },
			{ "00 00 00 80", McpValueType.Int32, 8, "-2147483648" },
			{ "78 56 34 12", McpValueType.UInt32, 8, "305419896" },
			{ "FF FF FF FF FF FF FF 7F", McpValueType.Int64, 8, "9223372036854775807" },
			{ "FF FF FF FF FF FF FF FF", McpValueType.UInt64, 8, "18446744073709551615" },
			{ "00 00 A0 3F", McpValueType.Float, 8, "1.25" },
			{ "00 00 00 00 00 00 F0 3F", McpValueType.Double, 8, "1" },
			{ "78 56 34 12", McpValueType.Pointer, 4, "12345678" },
			{ "D0 C3 B2 A1 F6 7F 00 00", McpValueType.Pointer, 8, "7FF6A1B2C3D0" }
		};
	}

	public static TheoryData<McpValueType, string, McpValueType, ValueByteOrder, string, string> TextConversionCases()
	{
		return new TheoryData<McpValueType, string, McpValueType, ValueByteOrder, string, string>
		{
			{ McpValueType.String, "hé", McpValueType.Bytes, ValueByteOrder.BigEndian, "68 C3 A9", "68 C3 A9" },
			{ McpValueType.Bytes, "68 C3 A9", McpValueType.String, ValueByteOrder.LittleEndian, "68 C3 A9", "hé" },
			{ McpValueType.WString, "hé", McpValueType.Bytes, ValueByteOrder.BigEndian, "68 00 E9 00", "68 00 E9 00" },
			{ McpValueType.Bytes, "68 00 E9 00", McpValueType.WString, ValueByteOrder.LittleEndian, "68 00 E9 00", "hé" }
		};
	}

	public static TheoryData<string, string> OperatorPrecedenceCases()
	{
		return new TheoryData<string, string>
		{
			{ "1 | 2 & 4", "1" },
			{ "1 ^ 3 & 1", "0" },
			{ "1 + 2 << 3", "24" },
			{ "20 / 3 % 4", "2" },
			{ "-1 >> 63", "1" },
			{ "1 << 64", "1" }
		};
	}

	public static TheoryData<string, string> InvalidExpressionCases()
	{
		return new TheoryData<string, string>
		{
			{ " ", "must contain an integer expression" },
			{ "(1", "unclosed parenthesis" },
			{ "1 # 2", "unsupported token" },
			{ "1 % 0", "cannot take a remainder by zero" },
			{ "18446744073709551616", "invalid 64-bit decimal literal" }
		};
	}
}
