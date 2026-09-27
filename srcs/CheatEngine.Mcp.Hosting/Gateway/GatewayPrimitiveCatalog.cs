using Microsoft.Extensions.Options;

namespace CheatEngine.Mcp.Hosting.Gateway;

/// <summary>
///     The gateway's one schema-only catalog of the composed primitives, built once on first use: the tool listing and
///     the routed resource templates both derive from it, so no primitive type is ever constructed.
/// </summary>
internal sealed class GatewayPrimitiveCatalog(IOptions<CheatEngineMcpPrimitiveOptions> manifest)
{
	private readonly Lazy<McpPrimitiveCatalog> _catalog = new(() => McpPrimitiveCatalog.Create(manifest.Value));

	/// <summary>The validated catalog.</summary>
	internal McpPrimitiveCatalog Catalog => _catalog.Value;
}
