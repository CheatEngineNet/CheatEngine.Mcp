using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CheatEngine.Mcp.Tools.Pointer;

/// <summary>The composition entry point of the <c>pointer</c> tool domain (10 tools in the v2 catalog).</summary>
public static class PointerToolsBuilderExtensions
{
	extension(ICheatEngineMcpBuilder builder)
	{
		/// <summary>
		///     Declares the <c>pointer_*</c> tool containers and their JSON metadata and, for a backend, the activation's
		///     <see cref="PointerStore" />.
		/// </summary>
		/// <returns>The same builder.</returns>
		public ICheatEngineMcpBuilder AddPointerTools()
		{
			ArgumentNullException.ThrowIfNull(builder);
			if (builder.Mode is CheatEngineMcpMode.Backend)
			{
				builder.Services.TryAddScoped<PointerStore>();
			}

			return builder
				.AddJsonTypeInfoResolver(PointerJsonContext.Default)
				.AddToolType<PointerChainTools>()
				.AddToolType<PointerReferenceTools>()
				.AddToolType<PointerMapTools>()
				.AddToolType<PointerScanTools>();
		}
	}
}
