namespace CheatEngine.Mcp.Tests.LiveQualification;

public sealed class LiveDebuggerTransitionReceiptTests
{
	[Fact]
	public void Parse_ExactEventPreservesRawAdvisoryAndFirstCallbackState()
	{
		string[] lines = Sample("1", "42", "1234ABC", "2", "number", "integer", "43", "number", "integer", "4096",
			"nil", "not_number", "nil");
		LiveDebuggerTransitionReceipt receipt = LiveDebuggerTransitionReceipt.Parse(lines, "12", "sample");

		Assert.Equal((1, 42, 0x1234ABCUL, 2), (receipt.EventCount, receipt.ThreadId, receipt.Rip, receipt.CallbackCount));
		Assert.Equal(new LiveDebuggerTransitionRawValue("number", "integer", "43"), receipt.FirstCallback!.ThreadId);
		Assert.Equal(lines, receipt.Lines);
	}

	[Theory]
	[InlineData("2", "42", "1234")]
	[InlineData("0", "42", "1234")]
	[InlineData("1", "0", "1234")]
	[InlineData("1", "4294967295", "1234")]
	[InlineData("1", "42", "10000000000000000")]
	[InlineData("1", "42", "0")]
	public void Parse_UntrustedEventIdentityOrCount_IsRejected(string eventCount, string threadId, string rip)
	{
		Assert.Throws<InvalidDataException>(() => LiveDebuggerTransitionReceipt.Parse(
			Sample(eventCount, threadId, rip, "1", "number", "integer", "42", "number", "integer", "4096", "nil", "not_number", "nil"),
			"12", "sample"));
	}

	[Fact]
	public void Parse_ExactEventWithoutCallback_IsRejectedSeparately()
	{
		Assert.Throws<InvalidDataException>(() => LiveDebuggerTransitionReceipt.Parse(
			Sample("1", "42", "1234", "0", "none", "not_number", "", "none", "not_number", "", "none", "not_number", ""),
			"12", "sample"));
	}

	[Theory]
	[InlineData("65536", "number", "integer", "42")]
	[InlineData("1", "number", "float", "xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx")]
	[InlineData("1", "none", "integer", "")]
	public void Parse_UntrustedCallbackCountOrPrimitive_IsRejected(string callbackCount, string type, string mathType, string value)
	{
		Assert.Throws<InvalidDataException>(() => LiveDebuggerTransitionReceipt.Parse(
			Sample("0", "", "", callbackCount, type, mathType, value, "none", "not_number", "", "none", "not_number", ""),
			"12", "sample"));
	}

	[Theory]
	[InlineData("error", "12", "restore")]
	[InlineData("ok", "2", "restore")]
	[InlineData("ok", "12", "install")]
	public void Parse_FailedOrStaleAcknowledgement_CannotConfirmCleanup(string status, string nonce, string command)
	{
		Assert.Throws<InvalidDataException>(() => LiveDebuggerTransitionReceipt.Parse([status, nonce, command], "12", "restore"));
	}

	private static string[] Sample(string eventCount, string threadId, string rip, string callbackCount,
		string threadType, string threadMathType, string threadValue, string ripType, string ripMathType, string ripValue,
		string eipType, string eipMathType, string eipValue) =>
		["ok", "12", "sample", "true", "boolean", "true", "true", "boolean", "true", "true", "nil", "nil",
			"false", "string", "string", eventCount, threadId, rip, callbackCount, threadType, threadMathType, threadValue,
			ripType, ripMathType, ripValue, eipType, eipMathType, eipValue];
}
