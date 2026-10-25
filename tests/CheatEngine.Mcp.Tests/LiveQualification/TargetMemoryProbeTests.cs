using System.Runtime.Versioning;

namespace CheatEngine.Mcp.Tests.LiveQualification;

[SupportedOSPlatform("windows")]
public sealed class TargetMemoryProbeTests
{
	[Fact]
	public void ValidateInspectionRequest_AllowsZeroLengthButRejectsOversizedLength()
	{
		TargetMemoryProbe.ValidateInspectionRequest(0x1000, 0);
		Assert.Throws<ArgumentOutOfRangeException>(() =>
			TargetMemoryProbe.ValidateInspectionRequest(0x1000, TargetMemoryProbe.MaximumDiagnosticReadLength + 1));
	}

	[Theory]
	[InlineData(0UL, 1)]
	[InlineData(0x8000000000000000UL, 1)]
	[InlineData(0x7FFFFFFFFFFFFFFFUL, 2)]
	public void ValidateInspectionRequest_RejectsInvalidOrOverflowingNativeRanges(ulong address, int readLength)
	{
		Assert.Throws<ArgumentOutOfRangeException>(() => TargetMemoryProbe.ValidateInspectionRequest(address, readLength));
	}

	[Theory]
	[InlineData(0x1000UL, 0x1000UL, 0x1FFCUL, 4, true)]
	[InlineData(0x1000UL, 0x1000UL, 0x1FFCUL, 5, false)]
	[InlineData(0x1000UL, 0x1000UL, 0x2000UL, 0, true)]
	[InlineData(0x1000UL, 0x1000UL, 0x2000UL, 1, false)]
	public void CoversReadRange_RequiresTheEntireReadToFitInTheQueriedRegion(ulong baseAddress, ulong regionSize,
		ulong address, int readLength, bool expected)
	{
		Assert.Equal(expected, TargetMemoryProbe.CoversReadRange(baseAddress, checked((nuint) regionSize), address, readLength));
	}

	[Theory]
	[InlineData(0x1000U, 0x20000U, 0x1000UL, 0x1000UL, 0x1000UL, 0x1800UL, true)]
	[InlineData(0x1000U, 0x20000U, 0x1000UL, 0x1000UL, 0x1000UL, 0x2000UL, false)]
	[InlineData(0x10000U, 0x20000U, 0x1000UL, 0x1000UL, 0x1000UL, 0x1800UL, false)]
	[InlineData(0x1000U, 0x10000U, 0x1000UL, 0x1000UL, 0x1000UL, 0x1800UL, false)]
	[InlineData(0x1000U, 0x20000U, 0x1000UL, 0UL, 0x1000UL, 0x1800UL, false)]
	public void Covers_RequiresCommittedPrivateRegionAndAllocationBaseCoveringTheExactAddress(uint state, uint type,
		ulong baseAddress, ulong allocationBase, ulong regionSize,
		ulong address, bool expected)
	{
		Assert.Equal(expected, TargetMemoryProbe.Covers(state, type, baseAddress, allocationBase, checked((nuint) regionSize), address));
	}
}
