using CheatEngine.Mcp.Core.Composition;
using CheatEngine.Mcp.Hosting.Gateway;
using CheatEngine.Mcp.Prompts;
using CheatEngine.Mcp.Resources;
using CheatEngine.Mcp.Tools;

using Microsoft.Extensions.Hosting;

namespace CheatEngine.Mcp.Gateway;

/// <summary>The stdio gateway executable. It composes the same primitives as the plugin, in schema-only mode.</summary>
public static class GatewayProgram
{
	/// <summary>Runs the gateway until its stdio client disconnects.</summary>
	/// <param name="args">The gateway command line.</param>
	/// <returns>The gateway run.</returns>
	public static async Task Main(string[] args)
	{
		await CreateBuilder(args).Build().RunAsync().ConfigureAwait(false);
	}

	/// <summary>Composes the gateway host.</summary>
	/// <param name="args">The gateway command line.</param>
	/// <returns>The configured host builder.</returns>
	public static HostApplicationBuilder CreateBuilder(string[] args)
	{
		HostApplicationBuilder builder = Host.CreateApplicationBuilder();
		ComposePrimitives(builder.AddCheatEngineMcpGateway(args,
			typeof(GatewayProgram).Assembly.GetName().Version?.ToString()));
		return builder;
	}

	/// <summary>The primitives the gateway routes; the plugin composes the same chain.</summary>
	/// <param name="builder">The gateway's composition builder.</param>
	/// <returns>The same builder.</returns>
	public static ICheatEngineMcpBuilder ComposePrimitives(ICheatEngineMcpBuilder builder)
	{
		return builder
			.AddTools()
			.AddResources()
			.AddPrompts();
	}
}
