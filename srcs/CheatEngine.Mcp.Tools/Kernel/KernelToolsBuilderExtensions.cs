namespace CheatEngine.Mcp.Tools.Kernel;

/// <summary>The composition entry point of the <c>kernel</c> tool domain (7 tools in the v2 catalog).</summary>
public static class KernelToolsBuilderExtensions
{
	extension(ICheatEngineMcpBuilder builder)
	{
		/// <summary>
		///     Declares the <c>kernel_*</c> tool container and its JSON metadata.
		/// </summary>
		/// <returns>The same builder.</returns>
		public ICheatEngineMcpBuilder AddKernelTools()
		{
			ArgumentNullException.ThrowIfNull(builder);
			return builder
				.AddJsonTypeInfoResolver(KernelJsonContext.Default)
				.AddToolType<KernelTools>();
		}
	}
}
