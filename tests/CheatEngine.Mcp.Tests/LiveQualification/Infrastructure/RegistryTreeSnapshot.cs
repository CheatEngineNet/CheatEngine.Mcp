namespace CheatEngine.Mcp.Tests.LiveQualification.Infrastructure;

/// <summary>
///     A recursive snapshot of <see cref="SubKey" /> below <c>HKEY_CURRENT_USER</c>; <see cref="Root" /> is null when
///     it is absent.
/// </summary>
internal sealed record RegistryTreeSnapshot(string SubKey, RegistryKeySnapshot? Root)
{
	/// <summary>Whether the key existed.</summary>
	internal bool Exists => Root is not null;
}
