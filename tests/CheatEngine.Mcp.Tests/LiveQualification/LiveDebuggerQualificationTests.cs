using System.Runtime.Versioning;

namespace CheatEngine.Mcp.Tests.LiveQualification;

[SupportedOSPlatform("windows")]
public sealed class LiveDebuggerQualificationTests
{
	[Fact]
	public void SelectOwnedThreadId_UsesTheSharedPositiveThreadId()
	{
		long selected = LiveDebuggerQualification.SelectOwnedThreadId([53, 17, 53], [17, 91], 17);

		Assert.Equal(17, selected);
	}

	[Fact]
	public void SelectOwnedThreadId_WithoutAnExternalMatch_Refuses()
	{
		InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
			LiveDebuggerQualification.SelectOwnedThreadId([53, 17], [91, 92], 17));

		Assert.Contains("independently observed", exception.Message, StringComparison.Ordinal);
	}

	[Fact]
	public void SelectOwnedThreadId_RejectsAnotherSharedThreadWhenPublishedThreadIsMissing()
	{
		Assert.Throws<InvalidOperationException>(() =>
			LiveDebuggerQualification.SelectOwnedThreadId([17, 53], [17, 53, 91], 91));
	}

	[Theory]
	[InlineData(0x1000UL, true)]
	[InlineData(0x10FFUL, true)]
	[InlineData(0x1100UL, false)]
	[InlineData(0x0FFFUL, false)]
	public void IsAddressInModules_UsesAnExclusiveUpperBoundary(ulong address, bool expected)
	{
		bool actual = LiveDebuggerQualification.IsAddressInModules(address,
			[new LiveDebuggerQualification.ModuleRange(0x1000, 0x1100)]);

		Assert.Equal(expected, actual);
	}
}
