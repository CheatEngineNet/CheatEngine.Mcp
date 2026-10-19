using System.Runtime.Versioning;

namespace CheatEngine.Mcp.Tests.LiveQualification;

[SupportedOSPlatform("windows")]
public sealed class LiveApiHookQualificationOracleTests
{
	[Fact]
	public void AssertHookContract_X64SourceDerivedFixture_AcceptsOnlyTheOwnedDestination()
	{
		const string script = """
		[ENABLE]
		alloc(originalcallmcpliveapihook,1024,7FFA)
		alloc(jumptrampolinemcpliveapihook,64,7FFA); // special jump trampoline in the current region (64-bit)
		label(jumptrampolinemcpliveapihookaddress)
		label(returnheremcpliveapihook)
		originalcallmcpliveapihook:
		nop
		nop
		nop
		nop
		nop
		jmp returnheremcpliveapihook
		jumptrampolinemcpliveapihook:
		jmp [jumptrampolinemcpliveapihookaddress]
		jumptrampolinemcpliveapihookaddress:
		dq 7FBA
		7FFA:
		jmp jumptrampolinemcpliveapihook
		returnheremcpliveapihook:
		[DISABLE]
		7FFA:
		db 90 90 90 90 90
		dealloc(originalcallmcpliveapihook)
		dealloc(jumptrampolinemcpliveapihook)
		""";

		LiveApiHookQualification.AssertHookContract(script, "7fFa", "7fBa", true);
		Assert.Throws<Xunit.Sdk.EqualException>(() =>
			LiveApiHookQualification.AssertHookContract(script.Replace("dq 7FBA", "dq 7FBB", StringComparison.Ordinal), "7fFa", "7fBa", true));
	}

	[Fact]
	public void AssertHookContract_X86SourceDerivedFixture_RejectsExtraRollbackSemantics()
	{
		const string script = """
		[ENABLE]
		alloc(originalcallmcpliveapihook,1024)
		label(returnheremcpliveapihook)
		originalcallmcpliveapihook:
		nop
		nop
		nop
		nop
		nop
		jmp returnheremcpliveapihook
		401A:
		jmp 402A
		returnheremcpliveapihook:
		[DISABLE]
		401A:
		db 90 90 90 90 90
		dealloc(originalcallmcpliveapihook)
		""";
		LiveApiHookQualification.AssertHookContract(script, "401a", "402a", false);
		string mutated = script.Replace("dealloc(originalcallmcpliveapihook)", "dealloc(originalcallmcpliveapihook)\nlabel(unreviewed)", StringComparison.Ordinal);

		Assert.Throws<Xunit.Sdk.EqualException>(() =>
			LiveApiHookQualification.AssertHookContract(mutated, "401a", "402a", false));
	}
}
