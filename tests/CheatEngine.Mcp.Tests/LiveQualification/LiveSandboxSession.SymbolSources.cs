using System.Runtime.Versioning;

using CheatEngine.Mcp.Tests.LiveQualification.Infrastructure.NetworkIsolation;

namespace CheatEngine.Mcp.Tests.LiveQualification;

internal sealed partial class LiveSandboxSession
{
	internal LiveSymbolSourceContainment SymbolSourceContainment => _symbolSourceContainment ??
		throw new InvalidOperationException("The admitted symbol-source containment has not been prepared.");

	[SupportedOSPlatform("windows")]
	private void PrepareSymbolSourceFixture(LiveQualificationInputs inputs)
	{
		if (_nativeSymbolFixture is null || inputs.WfpIsolationAcknowledgement != LiveQualificationOptIn.WfpIsolationAcknowledgement)
		{
			throw new UnauthorizedAccessException("The separately admitted private symbol-source fixture is unavailable.");
		}

		_symbolSourceContainment = LiveSymbolSourceContainment.Prepare(_nativeSymbolFixture, _layout.RunDirectory,
			inputs.WfpIsolationAcknowledgement);
		Record("symbol_source_fixture", _symbolSourceContainment.Fixture.Manifest);
	}

	[SupportedOSPlatform("windows")]
	private void ActivateSymbolSourceContainment(LiveQualificationInputs inputs)
	{
		LiveSymbolSourceContainment containment = SymbolSourceContainment;
		containment.Activate(
			Path.Combine(HostInstallationDirectory("A"), _profile!.HostExecutable),
			Path.Combine(HostInstallationDirectory("B"), _profile.HostExecutable),
			_profile.HostSha256,
			inputs.WfpIsolationAcknowledgement);
		Record("symbol_source_containment", containment.ActivationReceipt ??
			throw new InvalidOperationException("The admitted symbol-source containment did not record its activation."));
	}
}
