using System.Runtime.Versioning;

namespace CheatEngine.Mcp.Tests.LiveQualification;

[SupportedOSPlatform("windows")]
public sealed class LiveAssemblerQualificationScriptTests
{
	[Fact]
	public void ApplyScript_ContainsAnExactOwnedByteRollback()
	{
		string script = LiveAssemblerQualification.ApplyScript("1234");

		Assert.Equal("[ENABLE]\nassert(1234,90 90 90 90 90)\n1234:\ndb CC CC CC CC CC\n\n[DISABLE]\n1234:\ndb 90 90 90 90 90", script);
	}

	[Fact]
	public void AssertGeneratedInjection_RequiresTheWholeRollbackTemplate()
	{
		string valid = "[ENABLE]\naobscanmodule(mcpLiveInjection,test.exe,90 90 90 90 90)\nassert(mcpLiveInjection,90 90 90 90 90)\nalloc(newmem,2048,mcpLiveInjection)\nlabel(code)\nlabel(return)\nregistersymbol(mcpLiveInjection)\n\nnewmem:\n// Add reviewed custom code above the original bytes.\ncode:\ndb 90 90 90 90 90\njmp return\n\nmcpLiveInjection:\njmp newmem\nreturn:\n\n[DISABLE]\nmcpLiveInjection:\ndb 90 90 90 90 90\nunregistersymbol(mcpLiveInjection)\ndealloc(newmem)";

		LiveAssemblerQualification.AssertGeneratedInjection(valid, "test.exe");
		Assert.Throws<Xunit.Sdk.EqualException>(() =>
			LiveAssemblerQualification.AssertGeneratedInjection(valid.Replace("unregistersymbol(mcpLiveInjection)\n", string.Empty, StringComparison.Ordinal), "test.exe"));
	}
}
