using Microsoft.Extensions.Configuration;

namespace CheatEngine.Mcp.Hosting.Configuration;

/// <summary>Adds the MCP settings sources to a configuration.</summary>
public static class CheatEngineMcpConfigurationBuilderExtensions
{
	extension(IConfigurationBuilder configuration)
	{
		/// <summary>
		///     Adds <c>MCP_HOST</c>, <c>MCP_PORT</c>, <c>MCP_INSTANCE_NAME</c> and <c>MCP_INSTANCE_DIRECTORY</c> as
		///     <c>Mcp</c> settings. Add it after the JSON files so the variables take precedence.
		/// </summary>
		/// <param name="environment">Reads an environment variable.</param>
		/// <returns>The configuration builder.</returns>
		/// <exception cref="FormatException"><c>MCP_PORT</c> is not an integer.</exception>
		public IConfigurationBuilder AddCheatEngineMcpEnvironment(Func<string, string?> environment)
		{
			ArgumentNullException.ThrowIfNull(configuration);
			ArgumentNullException.ThrowIfNull(environment);
			return configuration.Add(new McpEnvironmentConfigurationSource(environment));
		}
	}
}
