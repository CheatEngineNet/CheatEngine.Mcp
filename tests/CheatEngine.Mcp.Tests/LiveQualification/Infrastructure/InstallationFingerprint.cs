namespace CheatEngine.Mcp.Tests.LiveQualification.Infrastructure;

/// <summary>What must not change in the source installation: the host executable and the autorun folder.</summary>
/// <param name="HostSha256">The SHA-256 of the host executable.</param>
/// <param name="Autorun">One <c>relative/path length SHA-256</c> line per autorun file, ordinally sorted.</param>
internal sealed record InstallationFingerprint(string HostSha256, IReadOnlyList<string> Autorun);
