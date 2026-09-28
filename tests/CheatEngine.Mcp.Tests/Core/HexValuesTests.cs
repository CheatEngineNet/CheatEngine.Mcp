using CheatEngine.Mcp.Core.Contract;
using CheatEngine.Mcp.Core.Values;
using CheatEngine.SDK.Engine.Values;

namespace CheatEngine.Mcp.Tests.Core;

/// <summary><see cref="HexFormat" /> and <see cref="HexParse" />.</summary>
public sealed class HexValuesTests
{
	[Theory]
	[InlineData(0UL, "0")]
	[InlineData(0x7FF6A1B2C3D0UL, "7FF6A1B2C3D0")]
	[InlineData(ulong.MaxValue, "FFFFFFFFFFFFFFFF")]
	public void Address_Value_IsUppercaseHexWithoutPrefixOrPadding(ulong value, string expected)
	{
		Assert.Equal(expected, HexFormat.Address(value));
		Assert.Equal(expected, HexFormat.Address(new Address(value)));
	}

	[Fact]
	public void Bytes_Values_AreSpacedUppercasePairs()
	{
		Assert.Equal("48 8B 05 00 FF", HexFormat.Bytes([0x48, 0x8B, 0x05, 0x00, 0xFF]));
		Assert.Equal("0A", HexFormat.Bytes([0x0A]));
		Assert.Equal(string.Empty, HexFormat.Bytes([]));
		Assert.Equal((4096 * 3) - 1, HexFormat.Bytes(new byte[4096]).Length);
	}

	[Theory]
	[InlineData(0L, "0")]
	[InlineData(0x1CL, "1C")]
	[InlineData(-8L, "-8")]
	[InlineData(long.MinValue, "-8000000000000000")]
	[InlineData(long.MaxValue, "7FFFFFFFFFFFFFFF")]
	public void Offset_SignedValue_RoundTripsThroughTheParser(long value, string expected)
	{
		Assert.Equal(expected, HexFormat.Offset(value));
		Assert.Equal(value, HexParse.Offset(expected, "offset"));
	}

	[Fact]
	public void Decimal_Int64AndUInt64_AreInvariant()
	{
		Assert.Equal("-9223372036854775808", HexFormat.Int64(long.MinValue));
		Assert.Equal("18446744073709551615", HexFormat.UInt64(ulong.MaxValue));
	}

	[Theory]
	[InlineData("48 8B 05", new byte[] { 0x48, 0x8B, 0x05 })]
	[InlineData("488b05", new byte[] { 0x48, 0x8B, 0x05 })]
	[InlineData("0x48,0x8B 0X05", new byte[] { 0x48, 0x8B, 0x05 })]
	[InlineData(" 5\t0a\r\n", new byte[] { 0x05, 0x0A })]
	public void Bytes_TolerantInput_IsParsed(string text, byte[] expected)
	{
		Assert.Equal(expected, HexParse.Bytes(text, "bytes", 16));
	}

	[Theory]
	[InlineData("")]
	[InlineData("  ")]
	[InlineData("48B")]
	[InlineData("GG")]
	[InlineData("0x")]
	[InlineData("48 ?? 05")]
	public void Bytes_InvalidInput_IsInvalidArgumentForTheParameter(string text)
	{
		ToolError error = Assert.Throws<CheatEngineToolException>(() => HexParse.Bytes(text, "bytes", 16)).Error;

		Assert.Equal(ToolErrorKind.InvalidArgument, error.Kind);
		Assert.Equal("""{"parameter":"bytes"}""", error.Details!.Value.GetRawText());
	}

	[Fact]
	public void Bytes_TooMany_IsLimitExceeded()
	{
		Assert.Equal(ToolErrorKind.LimitExceeded,
			Assert.Throws<CheatEngineToolException>(() => HexParse.Bytes("01 02 03", "bytes", 2)).Error.Kind);
	}

	[Theory]
	[InlineData("+0x10", 0x10L)]
	[InlineData("-0x10", -0x10L)]
	[InlineData(" 7ff ", 0x7FFL)]
	public void Offset_SignAndPrefix_AreAccepted(string text, long expected)
	{
		Assert.Equal(expected, HexParse.Offset(text, "offset"));
	}

	[Theory]
	[InlineData(null)]
	[InlineData("")]
	[InlineData("-")]
	[InlineData("12G")]
	[InlineData("8000000000000000")]
	[InlineData("-8000000000000001")]
	[InlineData("10000000000000000")]
	public void Offset_InvalidText_IsInvalidArgument(string? text)
	{
		Assert.Equal(ToolErrorKind.InvalidArgument,
			Assert.Throws<CheatEngineToolException>(() => HexParse.Offset(text, "offset")).Error.Kind);
	}

	[Fact]
	public void Offsets_List_KeepsDereferenceOrderAndNamesTheBadItem()
	{
		Assert.Equal([0x10L, -0x8L, 0x1CL], HexParse.Offsets(["10", "-8", "0x1C"], "offsets", 8));
		Assert.Empty(HexParse.Offsets(null, "offsets", 8));

		ToolError error = Assert.Throws<CheatEngineToolException>(() =>
			HexParse.Offsets(["10", "zz"], "offsets", 8)).Error;
		Assert.StartsWith("offsets[1]:", error.Message, StringComparison.Ordinal);
		Assert.Equal(ToolErrorKind.LimitExceeded, Assert.Throws<CheatEngineToolException>(() =>
			HexParse.Offsets(["1", "2", "3"], "offsets", 2)).Error.Kind);
	}

	[Theory]
	[InlineData("7FF6A1B2C3D0", 0x7FF6A1B2C3D0UL)]
	[InlineData("0x10", 0x10UL)]
	[InlineData(" ffffffffffffffff ", ulong.MaxValue)]
	public void TryAddress_HexText_IsParsed(string text, ulong expected)
	{
		Assert.True(HexParse.TryAddress(text, out ulong value));
		Assert.Equal(expected, value);
	}

	[Theory]
	[InlineData(null)]
	[InlineData("")]
	[InlineData("game.exe+10")]
	[InlineData("-10")]
	[InlineData("10000000000000000")]
	public void TryAddress_ExpressionOrOverflow_IsRefused(string? text)
	{
		Assert.False(HexParse.TryAddress(text, out _));
	}
}
