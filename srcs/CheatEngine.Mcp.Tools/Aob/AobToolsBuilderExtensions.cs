using CheatEngine.Mcp.Tools.Scan;

using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CheatEngine.Mcp.Tools.Aob;

/// <summary>The composition entry point of the <c>aob</c> tool domain (3 tools in the v2 catalog).</summary>
public static class AobToolsBuilderExtensions
{
	extension(ICheatEngineMcpBuilder builder)
	{
		/// <summary>
		///     Declares the <c>aob_*</c> tool container and its JSON metadata, and, for a backend, the activation's
		///     <c>MEM_MAPPED</c> override owner, which <c>aob_find</c>, <c>aob_find_value</c> and <c>scan_first</c>
		///     share.
		/// </summary>
		/// <returns>The same builder.</returns>
		public ICheatEngineMcpBuilder AddAobTools()
		{
			ArgumentNullException.ThrowIfNull(builder);
			if (builder.Mode is CheatEngineMcpMode.Backend)
			{
				builder.Services.TryAddScoped<MappedMemoryOverride>();
			}

			return builder
				.AddJsonTypeInfoResolver(AobJsonContext.Default)
				.AddToolType<AobTools>();
		}
	}
}
