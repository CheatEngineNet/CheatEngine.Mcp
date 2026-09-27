using CheatEngine.Mcp.Hosting.Configuration;

using Microsoft.Extensions.Configuration;

namespace CheatEngine.Mcp.Plugin;

/// <summary>Layers the plugin's settings sources in their documented precedence.</summary>
internal static class McpPluginConfigurationBuilderExtensions
{
	internal const string SettingsFileName = "appsettings.json";

	extension(IConfigurationBuilder configuration)
	{
		/// <summary>
		///     Adds the plugin folder's settings, then the user's, then the <c>MCP_*</c> variables; later sources win. None
		///     reloads: an activation reads its configuration once.
		/// </summary>
		/// <param name="pluginDirectory">The folder Cheat Engine loaded the plugin from.</param>
		/// <param name="environment">The process inputs.</param>
		/// <returns>The configuration builder.</returns>
		internal IConfigurationBuilder AddCheatEngineMcpSettings(string pluginDirectory,
			McpPluginEnvironment environment)
		{
			ArgumentNullException.ThrowIfNull(configuration);
			ArgumentException.ThrowIfNullOrWhiteSpace(pluginDirectory);
			ArgumentNullException.ThrowIfNull(environment);
			return configuration
				.AddJsonFile(Path.Combine(pluginDirectory, SettingsFileName), true, false)
				.AddJsonFile(Path.Combine(environment.DataDirectory, SettingsFileName), true, false)
				.AddCheatEngineMcpEnvironment(environment.GetVariable);
		}
	}
}
