using System.Runtime.Versioning;
using System.Security.Principal;

namespace CheatEngine.Mcp.Tests.LiveQualification;

/// <summary>Admission boundary for the separately authorized Windows symbol-source qualification.</summary>
[SupportedOSPlatform("windows")]
internal static class LiveSymbolSourceScenario
{
	internal static async Task RunAsync(LiveQualificationInputs inputs)
	{
		Assert.Equal(LiveQualificationScenario.SymbolSources, inputs.Scenario);
		Assert.Equal("x64", inputs.TargetArchitecture);
		// This is deliberately before the sandbox lock, filesystem work, registry guard, CE copies, and WFP preparation.
		if (!IsAdministrator())
		{
			throw new UnauthorizedAccessException("Symbol-source qualification requires an elevated Windows administrator token before any live sandbox work starts.");
		}

		await using LiveSandboxSession sandbox = await LiveSandboxSession.StartAsync(inputs, LiveSandboxOptions.Smoke with
		{
			SymbolSourcesQualification = true,
			EnableTargetCodeExecution = true
		});
		await LiveSymbolSourceQualification.RunAsync(sandbox, TestContext.Current.CancellationToken);
		sandbox.MarkPassed();
	}

	private static bool IsAdministrator()
	{
		using WindowsIdentity identity = WindowsIdentity.GetCurrent();
		return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
	}
}
