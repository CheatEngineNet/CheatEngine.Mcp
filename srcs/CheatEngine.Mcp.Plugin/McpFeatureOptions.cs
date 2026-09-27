using CheatEngine.Mcp.Hosting.Configuration;

using Microsoft.Extensions.Configuration;

namespace CheatEngine.Mcp.Plugin;

/// <summary>The Client opt-ins, read while Cheat Engine configures the activation. They are not a sandbox.</summary>
internal sealed class McpFeatureOptions
{
	/// <summary>Whether <c>execute_lua</c> and the Lua-backed tools may run caller Lua.</summary>
	public bool EnableUnsafeLua
	{
		get;
		set;
	} = true;

	/// <summary>Whether the Client may apply Auto Assembler patches.</summary>
	public bool EnableAutoAssembler
	{
		get;
		set;
	} = true;

	/// <summary>Binds the opt-ins from the <c>Mcp</c> section; absent keys keep their enabled defaults.</summary>
	/// <param name="configuration">The activation configuration.</param>
	/// <returns>The opt-ins.</returns>
	internal static McpFeatureOptions Read(IConfiguration configuration)
	{
		return configuration.GetSection(McpBackendOptions.SectionName).Get<McpFeatureOptions>()
		       ?? new McpFeatureOptions();
	}
}
