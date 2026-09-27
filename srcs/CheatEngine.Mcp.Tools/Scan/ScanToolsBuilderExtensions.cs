namespace CheatEngine.Mcp.Tools.Scan;

/// <summary>The composition entry point of the <c>scan</c> tool domain (8 tools in the v2 catalog).</summary>
public static class ScanToolsBuilderExtensions
{
	extension(ICheatEngineMcpBuilder builder)
	{
		/// <summary>
		///     Declares the <c>scan_*</c> tool containers and their JSON metadata. None is declared yet; the legacy tools
		///     this domain replaces are still declared by <c>AddTools()</c>.
		/// </summary>
		/// <returns>The same builder.</returns>
		public ICheatEngineMcpBuilder AddScanTools()
		{
			ArgumentNullException.ThrowIfNull(builder);
			// B3 adds AddJsonTypeInfoResolver(ScanJsonContext.Default) and one AddToolType<...>() per container
			// here, then removes the legacy lines it replaces from AddTools().
			return builder;
		}
	}
}
