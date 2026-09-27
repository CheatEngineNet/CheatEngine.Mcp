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

	[Fact]
	public void ConvertValue_MismatchedScalarWidths_IsInvalidArgument()
	{
		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() =>
			UtilTools.ConvertValue("1", McpValueType.Int8, McpValueType.Int32));

		Assert.Equal(ToolErrorKind.InvalidArgument, exception.Error.Kind);
		Assert.StartsWith("targetType", exception.Error.Message, StringComparison.Ordinal);
	}

	[Theory]
	[InlineData("0xFFFFFFFFFFFFFFFF + 2", "1", "1", "1")]
	[InlineData("0x8000000000000000 >> 1", "4611686018427387904", "4611686018427387904", "4000000000000000")]
	[InlineData("(1 << 63) + (1 << 63)", "0", "0", "0")]
	[InlineData("~0", "18446744073709551615", "-1", "FFFFFFFFFFFFFFFF")]
	public void Calculate_UsesUnsigned64BitWraparound(string expression, string unsignedValue, string signedValue,
		string hex)
	{
		UtilCalculateResult result = UtilTools.Calculate(expression);

		Assert.Equal((unsignedValue, signedValue, hex), (result.UnsignedValue, result.SignedValue, result.Hex));
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
}
