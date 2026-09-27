using System.Reflection;

namespace CheatEngine.Mcp.Core.Runtime;

/// <summary>Identifies the loaded plugin independently of the assembly that asks for it.</summary>
/// <param name="Version">The plugin assembly version.</param>
/// <param name="Location">The plugin file Cheat Engine loaded.</param>
/// <param name="RuntimeLocation">The assembly that hosts the MCP runtime; the plugin file itself.</param>
/// <param name="ApplicationName">The plugin assembly name, used as the web host application name.</param>
public sealed record McpRuntimeInfo(string? Version, string Location, string RuntimeLocation, string ApplicationName)
{
	/// <summary>Describes the plugin assembly that Cheat Engine loaded.</summary>
	/// <param name="plugin">The plugin assembly.</param>
	/// <returns>The runtime identity reported by diagnostics and the backend host.</returns>
	public static McpRuntimeInfo FromAssembly(Assembly plugin)
	{
		ArgumentNullException.ThrowIfNull(plugin);
		AssemblyName name = plugin.GetName();
		return new McpRuntimeInfo(name.Version?.ToString(), plugin.Location, plugin.Location,
			name.Name ?? "CheatEngine.Mcp");
	}
}
