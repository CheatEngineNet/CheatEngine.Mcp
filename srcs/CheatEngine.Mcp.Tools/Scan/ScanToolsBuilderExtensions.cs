namespace CheatEngine.Mcp.Tools.Scan;

/// <summary>The composition entry point of the <c>scan</c> tool domain (8 tools in the v2 catalog).</summary>
public static class ScanToolsBuilderExtensions
{
	extension(ICheatEngineMcpBuilder builder)
	{
		/// <summary>
		///     Declares the <c>scan_*</c> tool container and source-generated JSON metadata.
		/// </summary>
		/// <returns>The same builder.</returns>
		public ICheatEngineMcpBuilder AddScanTools()
		{
			ArgumentNullException.ThrowIfNull(builder);
			return builder
				.AddJsonTypeInfoResolver(ScanJsonContext.Default)
				.AddToolType<ScanTools>();
		}
	}
}
