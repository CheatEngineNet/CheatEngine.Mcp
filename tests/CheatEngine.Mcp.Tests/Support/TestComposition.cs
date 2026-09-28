using CheatEngine.Mcp.Gateway;
using CheatEngine.Mcp.Plugin;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

using ModelContextProtocol.Protocol;

namespace CheatEngine.Mcp.Tests.Support;

/// <summary>The production compositions, reused so tests never restate which primitives exist.</summary>
internal static class TestComposition
{
	/// <summary>The manifest the plugin's primitive composition declares for its backend.</summary>
	internal static CheatEngineMcpPrimitiveOptions BackendManifest
	{
		get;
	} =
		CheatEngineMcpComposition.CreateManifest(CheatEngineMcpMode.Backend,
			static builder => CheatEngineMcpPlugin.ComposePrimitives(builder));

	/// <summary>The manifest the real gateway executable composes.</summary>
	internal static CheatEngineMcpPrimitiveOptions GatewayManifest
	{
		get;
	} = CreateGatewayManifest();

	internal static IReadOnlyList<Tool> GatewayTools
	{
		get;
	} = GatewayToolCatalog.Create(GatewayManifest);

	private static CheatEngineMcpPrimitiveOptions CreateGatewayManifest()
	{
		HostApplicationBuilder builder = GatewayProgram.CreateBuilder(
			["--instance-directory", Path.Combine(Path.GetTempPath(), $"CheatEngine.Mcp.Tests-{Guid.NewGuid():N}")]);
		using ServiceProvider provider = builder.Services.BuildServiceProvider();
		return provider.GetRequiredService<IOptions<CheatEngineMcpPrimitiveOptions>>().Value;
	}
}
