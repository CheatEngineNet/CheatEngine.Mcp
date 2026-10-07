using System.Runtime.Versioning;

namespace CheatEngine.Mcp.Tests.LiveQualification;

[SupportedOSPlatform("windows")]
public sealed class LiveCompilerInventoryTests
{
	[Fact]
	public void IsExpectedScanLease_ExactCeGuidLeaseAndDwordSize_Accepts()
	{
		Assert.True(LiveCompilerQualification.IsExpectedScanLease("Cheat Engine/{336E5A59-AAAB-426C-802D-3923A12D3EEE}/inuse.lock", 4));
	}

	[Theory]
	[InlineData("Cheat Engine/{336E5A59-AAAB-426C-802D-3923A12D3EEE}/inuse.lock", 0)]
	[InlineData("Cheat Engine/{336E5A59-AAAB-426C-802D-3923A12D3EEE}/inuse.lock", 5)]
	[InlineData("Cheat Engine/other/inuse.lock", 4)]
	[InlineData("Cheat Engine/{336E5A59-AAAB-426C-802D-3923A12D3EEE}/assembly.dll", 4)]
	[InlineData("Cheat Engine/extra/{336E5A59-AAAB-426C-802D-3923A12D3EEE}/inuse.lock", 4)]
	[InlineData("other/{336E5A59-AAAB-426C-802D-3923A12D3EEE}/inuse.lock", 4)]
	public void IsExpectedScanLease_AnyOtherFileOrSize_Refuses(string path, long length)
	{
		Assert.False(LiveCompilerQualification.IsExpectedScanLease(path, length));
	}
}
