using Microsoft.Extensions.DependencyInjection;

namespace CheatEngine.Mcp.Core.Composition;

/// <summary>The default <see cref="ICheatEngineMcpBuilder" />.</summary>
public sealed class CheatEngineMcpBuilder : ICheatEngineMcpBuilder
{
	/// <summary>Creates a builder over a container.</summary>
	/// <param name="services">The container that receives the composition's registrations.</param>
	/// <param name="mode">Whether the composition serves a live backend or a schema-only catalog.</param>
	public CheatEngineMcpBuilder(IServiceCollection services, CheatEngineMcpMode mode)
	{
		ArgumentNullException.ThrowIfNull(services);
		Services = services;
		Mode = mode;
	}

	/// <inheritdoc />
	public IServiceCollection Services
	{
		get;
	}

	/// <inheritdoc />
	public CheatEngineMcpMode Mode
	{
		get;
	}
}
