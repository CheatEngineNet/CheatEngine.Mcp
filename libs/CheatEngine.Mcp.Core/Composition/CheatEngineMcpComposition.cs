using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace CheatEngine.Mcp.Core.Composition;

/// <summary>Evaluates a composition without building the application container.</summary>
public static class CheatEngineMcpComposition
{
	/// <summary>Returns the primitive manifest that a composition declares.</summary>
	/// <param name="mode">The composition mode.</param>
	/// <param name="configure">The composition, for example <c>builder => builder.AddTools()</c>.</param>
	/// <returns>The declared primitives and host-service registrations.</returns>
	public static CheatEngineMcpPrimitiveOptions CreateManifest(CheatEngineMcpMode mode,
		Action<ICheatEngineMcpBuilder> configure)
	{
		ArgumentNullException.ThrowIfNull(configure);
		ServiceCollection services = new();
		configure(new CheatEngineMcpBuilder(services, mode));
		services.AddOptions<CheatEngineMcpPrimitiveOptions>();
		using ServiceProvider provider = services.BuildServiceProvider();
		return provider.GetRequiredService<IOptions<CheatEngineMcpPrimitiveOptions>>().Value;
	}
}
