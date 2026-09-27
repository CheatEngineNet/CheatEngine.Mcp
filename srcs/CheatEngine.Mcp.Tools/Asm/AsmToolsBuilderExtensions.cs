namespace CheatEngine.Mcp.Tools.Asm;

/// <summary>The composition entry point of the <c>asm</c> tool domain (8 tools in the v2 catalog).</summary>
public static class AsmToolsBuilderExtensions
{
	extension(ICheatEngineMcpBuilder builder)
	{
		/// <summary>
		///     Declares the <c>asm_*</c> tool container and its JSON metadata.
		/// </summary>
		/// <returns>The same builder.</returns>
		public ICheatEngineMcpBuilder AddAsmTools()
		{
			ArgumentNullException.ThrowIfNull(builder);
			return builder
				.AddJsonTypeInfoResolver(AsmJsonContext.Default)
				.AddToolType<AsmTools>();
		}
	}
}
