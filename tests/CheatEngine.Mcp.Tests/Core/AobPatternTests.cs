using CheatEngine.Mcp.Core.Contract;
using CheatEngine.Mcp.Core.Values;

namespace CheatEngine.Mcp.Tests.Core;

public sealed class AobPatternTests
{
	[Theory]
	[InlineData("48 8b ?? 05", "48 8B ?? 05")]
	[InlineData("48 8B ? 05", "48 8B ?? 05")]
	[InlineData("48 8B * 05", "48 8B ?? 05")]
	[InlineData("488B??05", "48 8B ?? 05")]
	[InlineData("  48\t8B\r\n05 ", "48 8B 05")]
	[InlineData("?? ?? 90", "?? ?? 90")]
	public void Normalize_Pattern_IsCanonicalAndAcceptedByTheClient(string pattern, string expected)
	{
		string normalized = AobPattern.Normalize(pattern, "pattern");

		Assert.Equal(expected, normalized);
		Assert.True(Client.Scanning.AobPattern.TryParse(normalized, out _));
	}

	[Theory]
	[InlineData("4? 8B")]
	[InlineData("?8 8B")]
	public void Normalize_NibbleWildcard_IsRejected(string pattern)
	{
		ToolError error = Assert.Throws<CheatEngineToolException>(() => AobPattern.Normalize(pattern, "pattern"))
			.Error;

		Assert.Equal(ToolErrorKind.InvalidArgument, error.Kind);
		Assert.Contains("nibble", error.Message, StringComparison.Ordinal);
	}

	[Theory]
	[InlineData(null)]
	[InlineData("")]
	[InlineData("48 8")]
	[InlineData("48 GG")]
	[InlineData("48*8B")]
	[InlineData("0x48")]
	[InlineData("?? ?")]
	public void Normalize_InvalidPattern_IsInvalidArgument(string? pattern)
	{
		Assert.Equal(ToolErrorKind.InvalidArgument,
			Assert.Throws<CheatEngineToolException>(() => AobPattern.Normalize(pattern, "pattern")).Error.Kind);
	}

	[Fact]
	public void Normalize_TooManyPositions_IsLimitExceeded()
	{
		string pattern = string.Join(' ', Enumerable.Repeat("90", AobPattern.MaxBytes + 1));

		Assert.Equal(ToolErrorKind.LimitExceeded,
			Assert.Throws<CheatEngineToolException>(() => AobPattern.Normalize(pattern, "pattern")).Error.Kind);
	}
}
