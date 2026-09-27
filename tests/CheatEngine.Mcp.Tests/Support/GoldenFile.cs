namespace CheatEngine.Mcp.Tests.Support;

/// <summary>Compares contract output with a reviewed snapshot; capture happens only when explicitly requested.</summary>
internal static class GoldenFile
{
	internal const string UpdateVariable = "CHEATENGINE_MCP_UPDATE_GOLDEN";

	internal static void AssertMatches(string name, string actual)
	{
		string path = Path.Combine(RepositoryPaths.Root, "tests", "CheatEngine.Mcp.Tests", "Contract", "Golden", name);
		string normalized = Normalize(actual);
		if (string.Equals(Environment.GetEnvironmentVariable(UpdateVariable), "1", StringComparison.Ordinal))
		{
			Directory.CreateDirectory(Path.GetDirectoryName(path)!);
			File.WriteAllText(path, normalized.Replace("\n", "\r\n", StringComparison.Ordinal));
			return;
		}

		Assert.True(File.Exists(path),
			$"Missing golden file '{name}'. Review the output and capture it once with {UpdateVariable}=1.");
		Assert.Equal(Normalize(File.ReadAllText(path)), normalized);
	}

	private static string Normalize(string text)
	{
		return text.Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd('\n') + "\n";
	}
}
