using System.ComponentModel.DataAnnotations;

using CheatEngine.Mcp.Hosting.Discovery;

namespace CheatEngine.Mcp.Hosting.Configuration;

/// <summary>How one activation's backend is published to the gateway, bound from the <c>Mcp</c> section.</summary>
public sealed class McpDiscoveryOptions
{
	/// <summary>The configuration section that holds the MCP settings.</summary>
	public const string SectionName = McpBackendOptions.SectionName;

	/// <summary>The display name reported by <c>list_instances</c>; names may repeat across instances.</summary>
	[Required(ErrorMessage = "Mcp:InstanceName is required.")]
	[MaxLength(128, ErrorMessage = "Mcp:InstanceName must be at most 128 characters.")]
	public string InstanceName
	{
		get;
		set;
	} = $"Cheat Engine {Environment.ProcessId}";

	/// <summary>The absolute registry directory; the gateway must read the same directory.</summary>
	[AbsolutePath(ErrorMessage = "Mcp:InstanceDirectory must be an absolute directory.")]
	public string InstanceDirectory
	{
		get;
		set;
	} = InstanceRegistry.DefaultDirectory;
}
