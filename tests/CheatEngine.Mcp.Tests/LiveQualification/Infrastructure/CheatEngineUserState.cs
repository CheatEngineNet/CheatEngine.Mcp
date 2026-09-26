// Adapted from CheatEngine.Client; see NOTICE.md and licenses/CheatEngine.Client.LICENSE.
namespace CheatEngine.Mcp.Tests.LiveQualification;

/// <summary>
///     Protects the Cheat Engine state of the workstation user (<c>HKCU\Software\Cheat Engine</c> and the Cheat Engine
///     files of <c>%APPDATA%</c>) around a session: <see cref="Begin" /> backs it up before Cheat Engine starts, and the
///     returned scope restores it, verified, afterwards.
/// </summary>
internal interface ICheatEngineUserStateGuard
{
	/// <summary>Backs up the user state of <paramref name="layout" />'s run and returns the scope that restores it.</summary>
	public ICheatEngineUserStateScope Begin(SandboxLayout layout);
}

/// <summary>A backed-up user state; disposing it restores and verifies the state if <see cref="Restore" /> did not.</summary>
internal interface ICheatEngineUserStateScope : IDisposable
{
	/// <summary>Whether the state was restored and verified equal to the backup.</summary>
	public bool Restored
	{
		get;
	}

	/// <summary>Restores the backup and verifies it; throws when the restored state differs.</summary>
	public void Restore();
}
