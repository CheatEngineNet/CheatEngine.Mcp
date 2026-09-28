using System.Reflection;

namespace CheatEngine.Mcp.Core.Runtime;

/// <summary>Identifies the loaded plugin independently of the assembly that asks for it.</summary>
/// <param name="Version">The plugin assembly version.</param>
/// <param name="Location">The plugin file Cheat Engine loaded.</param>
/// <param name="RuntimeLocation">The assembly that hosts the MCP runtime; the plugin file itself.</param>
/// <param name="ApplicationName">The plugin assembly name, used as the web host application name.</param>
public sealed record McpRuntimeInfo(string? Version, string Location, string RuntimeLocation, string ApplicationName)
{
	/// <summary>Describes the plugin file that Cheat Engine loaded from its plugin folder.</summary>
	/// <param name="plugin">The plugin assembly name.</param>
	/// <param name="pluginDirectory">The absolute folder Cheat Engine loaded the plugin from.</param>
	/// <returns>The runtime identity reported by diagnostics and the backend host.</returns>
	/// <remarks>
	///     The file is derived from the folder and the assembly name, never read from the loaded assembly, so the identity
	///     is the same for a file, in-memory or Native AOT load and needs no single-file-unsafe API.
	/// </remarks>
	/// <exception cref="ArgumentException">
	///     The assembly name has no simple name, or the folder is empty or not fully qualified.
	/// </exception>
	public static McpRuntimeInfo ForPlugin(AssemblyName plugin, string pluginDirectory)
	{
		ArgumentNullException.ThrowIfNull(plugin);
		ArgumentException.ThrowIfNullOrWhiteSpace(pluginDirectory);
		if (string.IsNullOrWhiteSpace(plugin.Name))
		{
			throw new ArgumentException("The plugin assembly name has no simple name.", nameof(plugin));
		}

		if (!Path.IsPathFullyQualified(pluginDirectory))
		{
			throw new ArgumentException("The plugin directory must be absolute.", nameof(pluginDirectory));
		}

		string location = Path.Combine(Path.GetFullPath(pluginDirectory), plugin.Name + ".dll");
		return new McpRuntimeInfo(plugin.Version?.ToString(), location, location, plugin.Name);
	}
}
