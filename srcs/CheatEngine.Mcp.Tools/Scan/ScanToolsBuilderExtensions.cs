using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CheatEngine.Mcp.Tools.Scan;

/// <summary>The composition entry point of the <c>scan</c> tool domain (8 tools in the v2 catalog).</summary>
public static class ScanToolsBuilderExtensions
{
	extension(ICheatEngineMcpBuilder builder)
	{
		/// <summary>
		///     Declares the <c>scan_*</c> tool container and source-generated JSON metadata, and, for a backend, the
		///     activation's <c>MEM_MAPPED</c> override owner that the scans with <c>includeMapped</c> share.
		/// </summary>
		/// <returns>The same builder.</returns>
		public ICheatEngineMcpBuilder AddScanTools()
		{
			ArgumentNullException.ThrowIfNull(builder);
			if (builder.Mode is CheatEngineMcpMode.Backend)
			{
				builder.Services.TryAddScoped<MappedMemoryOverride>();
			}

			return builder
				.AddJsonTypeInfoResolver(ScanJsonContext.Default)
				.AddToolType<ScanTools>();
		}
	}
}
