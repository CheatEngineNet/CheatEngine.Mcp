namespace CheatEngine.Mcp.Tests.LiveQualification.Infrastructure;

/// <summary>One registry key, with its values and subkeys sorted by name.</summary>
internal sealed record RegistryKeySnapshot(
	string Name,
	IReadOnlyList<RegistryValueSnapshot> Values,
	IReadOnlyList<RegistryKeySnapshot> Keys);
