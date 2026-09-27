using CheatEngine.Mcp.Hosting.Discovery;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using ModelContextProtocol.Protocol;

namespace CheatEngine.Mcp.Hosting.Gateway;

/// <summary>Composes the stdio multi-instance gateway.</summary>
public static class CheatEngineMcpGatewayExtensions
{
	extension(IHostApplicationBuilder builder)
	{
		/// <summary>
		///     Configures this host as the stdio gateway. The returned builder declares the routed primitives in
		///     <see cref="CheatEngineMcpMode.Catalog" /> mode, so no primitive type or Client service is ever constructed here.
		/// </summary>
		/// <param name="arguments">The gateway command line.</param>
		/// <param name="version">The version advertised as serverInfo.version.</param>
		/// <returns>The composition builder for the routed primitives.</returns>
		/// <exception cref="ArgumentException">The command line or a gateway environment variable is invalid.</exception>
		public ICheatEngineMcpBuilder AddCheatEngineMcpGateway(IReadOnlyList<string> arguments, string? version)
		{
			ArgumentNullException.ThrowIfNull(builder);
			GatewayOptions options = GatewayOptions.Resolve(arguments, Environment.GetEnvironmentVariable);
			// stdout carries only the MCP stdio stream.
			builder.Logging.ClearProviders();
			builder.Logging.AddConsole(console => console.LogToStandardErrorThreshold = LogLevel.Trace);
			builder.Logging.AddTokenSafeFloor();
			// Hosted services start in registration order: build the catalog before the stdio transport opens.
			builder.Services.AddHostedService<GatewayCatalogWarmup>();
			builder.Services.AddMcpServer(server =>
				{
					server.ServerInfo = new Implementation
					{
						Name = "CheatEngine.Mcp.Gateway", Version = version ?? "2.0.0"
					};
				})
				.WithStdioServerTransport();
			builder.Services.AddCheatEngineGatewayRouting(options);
			return new CheatEngineMcpBuilder(builder.Services, CheatEngineMcpMode.Catalog);
		}
	}
}
