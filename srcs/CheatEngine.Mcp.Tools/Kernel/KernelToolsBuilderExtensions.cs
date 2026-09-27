namespace CheatEngine.Mcp.Tools.Kernel;

/// <summary>The composition entry point of the <c>kernel</c> tool domain (7 tools in the v2 catalog).</summary>
public static class KernelToolsBuilderExtensions
{
	extension(ICheatEngineMcpBuilder builder)
	{
		/// <summary>
		///     Declares the <c>kernel_*</c> tool containers and their JSON metadata. None is declared yet; the legacy tools
		///     this domain replaces are still declared by <c>AddTools()</c>.
		/// </summary>
		/// <returns>The same builder.</returns>
		public ICheatEngineMcpBuilder AddKernelTools()
		{
			ArgumentNullException.ThrowIfNull(builder);
			// B11 adds AddJsonTypeInfoResolver(KernelJsonContext.Default) and one AddToolType<...>() per container
			// here, then removes the legacy lines it replaces from AddTools().
			return builder;
		}
	}
}
