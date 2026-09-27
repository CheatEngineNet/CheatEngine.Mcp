using CheatEngine.Client.Results;
using CheatEngine.Mcp.Core.Contract;
using CheatEngine.Mcp.Tests.Support;

using ModelContextProtocol;

namespace CheatEngine.Mcp.Tests.Core;

public sealed class CheatEngineToolExceptionTests
{
	public static TheoryData<string, ToolErrorKind, ToolHostEffect, bool> Factories => new()
	{
		{ "invalid_state", ToolErrorKind.InvalidState, ToolHostEffect.NotStarted, false },
		{ "not_found", ToolErrorKind.NotFound, ToolHostEffect.NotStarted, false },
		{ "not_attached", ToolErrorKind.NotAttached, ToolHostEffect.NotStarted, false },
		{ "busy", ToolErrorKind.Busy, ToolHostEffect.NotStarted, true },
		{ "capability_disabled", ToolErrorKind.CapabilityDisabled, ToolHostEffect.NotStarted, false },
		{ "unsupported", ToolErrorKind.Unsupported, ToolHostEffect.NotStarted, false },
		{ "timeout", ToolErrorKind.Timeout, ToolHostEffect.Unknown, false },
		{ "internal", ToolErrorKind.Internal, ToolHostEffect.Unknown, false }
	};

	[Fact]
	public void Exception_IsAnMcpExceptionButNeverAProtocolException()
	{
		CheatEngineToolException exception = CheatEngineToolException.NotAttached();

		Assert.IsAssignableFrom<McpException>(exception);
		Assert.False(typeof(McpProtocolException).IsAssignableFrom(exception.GetType()));
		Assert.Equal(exception.Error.Message, exception.Message);
	}

	[Fact]
	public void InvalidArgument_Parameter_IsNamedInMessageAndDetails()
	{
		ToolError error = CheatEngineToolException.InvalidArgument("address", "is empty.", "Pass game.exe+10.").Error;

		Assert.Equal(ToolErrorKind.InvalidArgument, error.Kind);
		Assert.Equal("address: is empty.", error.Message);
		Assert.Equal("Pass game.exe+10.", error.Hint);
		Assert.Equal(ToolHostEffect.NotStarted, error.HostEffect);
		Assert.False(error.Retryable);
		Assert.Equal("""{"parameter":"address"}""", error.Details!.Value.GetRawText());
	}

	[Fact]
	public void LimitExceeded_Parameter_IsNamedAndHinted()
	{
		ToolError error = CheatEngineToolException.LimitExceeded("limit", "must be at most 100.").Error;

		Assert.Equal(ToolErrorKind.LimitExceeded, error.Kind);
		Assert.Equal("limit: must be at most 100.", error.Message);
		Assert.Equal(ToolFailureMapping.LimitHint, error.Hint);
		Assert.Equal("""{"parameter":"limit"}""", error.Details!.Value.GetRawText());
	}

	[Theory]
	[MemberData(nameof(Factories))]
	public void Factory_Kind_SetsHostEffectAndRetryability(string factory, ToolErrorKind kind, ToolHostEffect effect,
		bool retryable)
	{
		ToolError error = Create(factory).Error;

		Assert.Equal(kind, error.Kind);
		Assert.Equal(effect, error.HostEffect);
		Assert.Equal(retryable, error.Retryable);
		Assert.False(string.IsNullOrWhiteSpace(error.Message));
	}

	[Fact]
	public void CapabilityDisabled_Setting_IsNamedInMessageAndHint()
	{
		ToolError error = CheatEngineToolException.CapabilityDisabled("EnableKernelAccess", "kernel_read_physical")
			.Error;

		Assert.Contains("kernel_read_physical", error.Message, StringComparison.Ordinal);
		Assert.Contains("Mcp:EnableKernelAccess", error.Message, StringComparison.Ordinal);
		Assert.Contains("Mcp:EnableKernelAccess", error.Hint, StringComparison.Ordinal);
	}

	[Fact]
	public void PartialEffect_Details_AreSerializedWithTheirTypeInfo()
	{
		ToolError error = CheatEngineToolException.PartialEffect("One of two steps completed.",
			ToolHostEffect.Started, new ContractProbeDetails(1, 1), TestJsonContext.Default.ContractProbeDetails,
			true, "Release the first step.").Error;

		Assert.Equal(ToolErrorKind.PartialEffect, error.Kind);
		Assert.Equal(ToolHostEffect.Started, error.HostEffect);
		Assert.True(error.Retryable);
		Assert.Equal("""{"completed":1,"failedIndex":1}""", error.Details!.Value.GetRawText());
	}

	[Fact]
	public void FromFailure_ClientFailure_KeepsTheUnderlyingException()
	{
		InvalidOperationException cause = new("native");
		CheatEngineFailure failure = new(CheatEngineFailureKind.LuaError, "Lua.Execute", "Script failed.", cause,
			CheatEngineHostEffect.Started);

		CheatEngineToolException exception = CheatEngineToolException.FromFailure(failure, false);

		Assert.Equal(ToolErrorKind.HostRefused, exception.Error.Kind);
		Assert.Equal(ToolHostEffect.Started, exception.Error.HostEffect);
		Assert.Same(cause, exception.InnerException);
	}

	[Fact]
	public void Constructor_EmptyMessage_IsRejected()
	{
		Assert.Throws<ArgumentException>(() => new CheatEngineToolException(new ToolError(ToolErrorKind.Internal, " ",
			null, ToolHostEffect.Unknown, false)));
	}

	private static CheatEngineToolException Create(string factory)
	{
		return factory switch
		{
			"invalid_state" => CheatEngineToolException.InvalidState("The debugger is attached."),
			"not_found" => CheatEngineToolException.NotFound("No such record."),
			"not_attached" => CheatEngineToolException.NotAttached("Memory.Read"),
			"busy" => CheatEngineToolException.Busy("A scan is running."),
			"capability_disabled" => CheatEngineToolException.CapabilityDisabled("EnableUnsafeLua", "lua_execute"),
			"unsupported" => CheatEngineToolException.Unsupported("DBVM is unavailable."),
			"timeout" => CheatEngineToolException.Timeout("The call did not return."),
			_ => CheatEngineToolException.Internal("Unexpected.")
		};
	}
}
