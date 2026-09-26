namespace CheatEngine.Client.Tests.LiveQualification;

// The reused registry guard only needs this path predicate from the Client's
// package-specific opt-in policy. MCP owns its own opt-in and host-version report.
internal static class LiveQualificationOptIn
{
	internal static bool IsSameOrBelow(string path, string directory)
	{
		string fullPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
		string fullDirectory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));
		return string.Equals(fullPath, fullDirectory, StringComparison.OrdinalIgnoreCase)
			|| fullPath.StartsWith(fullDirectory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
	}
}
