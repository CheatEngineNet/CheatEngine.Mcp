// Adapted from CheatEngine.Client (MIT); see NOTICE.md.

namespace CheatEngine.Mcp.Tests.LiveQualification.Infrastructure;

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
