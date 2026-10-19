using System.Runtime.Versioning;

namespace CheatEngine.Mcp.Tests.LiveQualification;

[SupportedOSPlatform("windows")]
public sealed class LiveExecutionAddressTests
{
	[Theory]
	[InlineData("100000", "0x100000")]
	[InlineData("0x100000", "0x100000")]
	[InlineData("aBcDef", "0xABCDEF")]
	[InlineData("FFFFFFFFFFFFFFFF", "0xFFFFFFFFFFFFFFFF")]
	public void PointerExpression_PreservesHexadecimalAllocationBits(string address, string expected)
	{
		Assert.Equal(expected, LiveExecutionQualification.PointerExpression(address));
	}
}
