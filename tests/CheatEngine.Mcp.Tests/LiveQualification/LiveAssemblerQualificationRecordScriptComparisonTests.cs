using System.Runtime.Versioning;

namespace CheatEngine.Mcp.Tests.LiveQualification;

[SupportedOSPlatform("windows")]
public sealed class LiveAssemblerQualificationRecordScriptComparisonTests
{
	[Fact]
	public void AssertScriptEqual_AcceptsCeCrLfForIdenticalScript()
	{
		const string expected = "[ENABLE]\ndefine(mcp_live_record,0)\n\n[DISABLE]\ndefine(mcp_live_record,0)";
		const string ceActual = "[ENABLE]\r\ndefine(mcp_live_record,0)\r\n\r\n[DISABLE]\r\ndefine(mcp_live_record,0)";

		LiveAssemblerQualification.AssertScriptEqual(expected, ceActual);
	}

	[Fact]
	public void AssertScriptEqual_RejectsChangedContentDespiteMatchingLineEndings()
	{
		const string expected = "[ENABLE]\ndefine(mcp_live_record,0)\n\n[DISABLE]\ndefine(mcp_live_record,0)";
		const string changed = "[ENABLE]\r\ndefine(mcp_live_record,1)\r\n\r\n[DISABLE]\r\ndefine(mcp_live_record,1)";

		Assert.Throws<Xunit.Sdk.EqualException>(() => LiveAssemblerQualification.AssertScriptEqual(expected, changed));
	}
}
