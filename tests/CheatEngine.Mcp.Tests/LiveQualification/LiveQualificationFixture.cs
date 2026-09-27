using System.Runtime.Versioning;

namespace CheatEngine.Mcp.Tests.LiveQualification;

/// <summary>Checks authorization before any native process, settings backup, or run directory is created.</summary>
[SupportedOSPlatform("windows")]
public sealed class LiveQualificationFixture : IAsyncLifetime
{
	internal LiveQualificationDecision Decision
	{
		get;
		private set;
	} = new(null, "The fixture has not initialized.");

	public ValueTask InitializeAsync()
	{
		Decision = LiveQualificationOptIn.ResolveFromEnvironment(LiveSandboxSession.FindRepository());
		return ValueTask.CompletedTask;
	}

	public ValueTask DisposeAsync()
	{
		return ValueTask.CompletedTask;
	}

	internal LiveQualificationInputs RequireAuthorization()
	{
		Assert.True(Decision.IsAuthorized, Decision.Refusal);
		return Decision.Inputs!;
	}
}
