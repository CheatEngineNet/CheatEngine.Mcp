namespace CheatEngine.Mcp.Tests.LiveQualification.Infrastructure;

/// <summary>
///     The files and folders of a directory: <c>relative/path length SHA-256</c> per file and <c>relative/</c> per
///     folder.
/// </summary>
internal sealed record FileTreeSnapshot(bool Exists, IReadOnlyList<string> Entries);
