namespace CheatEngine.Mcp.Plugin;

/// <summary>The process inputs the plugin composition reads, injectable so tests never depend on the real profile.</summary>
/// <param name="DataDirectory">The user data directory for settings and logs.</param>
/// <param name="GetVariable">Reads an environment variable.</param>
internal sealed record McpPluginEnvironment(string DataDirectory, Func<string, string?> GetVariable)
{
	/// <summary>The inputs of the running Cheat Engine process.</summary>
	internal static McpPluginEnvironment Current => From(Environment.GetEnvironmentVariable);

	/// <summary>Resolves the data directory from <c>MCP_DATA_DIRECTORY</c>, else the roaming profile.</summary>
	/// <param name="getVariable">Reads an environment variable.</param>
	/// <returns>The process inputs.</returns>
	internal static McpPluginEnvironment From(Func<string, string?> getVariable)
	{
		ArgumentNullException.ThrowIfNull(getVariable);
		return new McpPluginEnvironment(ResolveDataDirectory(getVariable("MCP_DATA_DIRECTORY")), getVariable);
	}

	internal static string ResolveDataDirectory(string? directory)
	{
		if (directory is null)
		{
			return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
				"CheatEngine.Mcp");
		}

		if (!Path.IsPathFullyQualified(directory))
		{
			throw new ArgumentException("MCP_DATA_DIRECTORY must be an absolute directory.", nameof(directory));
		}

		return Path.GetFullPath(directory);
	}
}
