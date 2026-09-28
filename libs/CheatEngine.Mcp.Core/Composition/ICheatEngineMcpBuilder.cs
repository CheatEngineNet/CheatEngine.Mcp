using Microsoft.Extensions.DependencyInjection;

namespace CheatEngine.Mcp.Core.Composition;

/// <summary>
///     Composes CheatEngine MCP primitives. The Tools, Resources and Prompts projects each contribute one fluent entry
///     point;
///     the plugin and the gateway apply the same composition in <see cref="CheatEngineMcpMode.Backend" /> or
///     <see cref="CheatEngineMcpMode.Catalog" /> mode.
/// </summary>
public interface ICheatEngineMcpBuilder
{
	/// <summary>The container that receives the composition's registrations.</summary>
	public IServiceCollection Services
	{
		get;
	}

	/// <summary>Whether the composition serves a live backend or a schema-only catalog.</summary>
	public CheatEngineMcpMode Mode
	{
		get;
	}
}
