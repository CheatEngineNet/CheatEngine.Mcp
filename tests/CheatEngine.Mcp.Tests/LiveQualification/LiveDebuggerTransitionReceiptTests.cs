namespace CheatEngine.Mcp.Tests.LiveQualification;

public sealed class LiveDebuggerTransitionReceiptTests
{
	[Fact]
	public void Parse_ExactEventPreservesRawAdvisoryStateWithoutUsingItAsTheStopOracle()
	{
		string[] lines = ["ok", "12", "sample", "true", "boolean", "true", "true", "boolean", "true",
			"true", "nil", "nil", "false", "string", "string", "1", "42", "1234ABC"];
		LiveDebuggerTransitionReceipt receipt = LiveDebuggerTransitionReceipt.Parse(lines, "12", "sample");
		Assert.Equal(1, receipt.EventCount);
		Assert.Equal(42, receipt.ThreadId);
		Assert.Equal(0x1234ABCUL, receipt.Rip);
		Assert.Equal(lines, receipt.Lines);
	}

	[Theory]
	[InlineData("2", "42", "1234")]
	[InlineData("0", "42", "1234")]
	[InlineData("1", "0", "1234")]
	[InlineData("1", "4294967295", "1234")]
	[InlineData("1", "42", "10000000000000000")]
	[InlineData("1", "42", "0")]
	public void Parse_UntrustedEventIdentityOrCount_IsRejected(string count, string threadId, string rip)
	{
		string[] lines = ["ok", "1", "sample", "true", "nil", "nil", "true", "nil", "nil",
			"true", "nil", "nil", "true", "nil", "nil", count, threadId, rip];
		Assert.Throws<InvalidDataException>(() => LiveDebuggerTransitionReceipt.Parse(lines, "1", "sample"));
	}

	[Theory]
	[InlineData("error", "1", "restore")]
	[InlineData("ok", "2", "restore")]
	[InlineData("ok", "1", "install")]
	public void Parse_FailedOrStaleAcknowledgement_CannotConfirmCleanup(string status, string nonce, string command)
	{
		Assert.Throws<InvalidDataException>(() => LiveDebuggerTransitionReceipt.Parse([status, nonce, command], "1", "restore"));
	}
}
