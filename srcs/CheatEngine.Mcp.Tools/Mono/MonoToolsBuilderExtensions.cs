namespace CheatEngine.Mcp.Tools.Mono;

/// <summary>The composition entry point of the <c>mono</c> tool domain (14 tools in the v2 catalog).</summary>
public static class MonoToolsBuilderExtensions
{
	extension(ICheatEngineMcpBuilder builder)
	{
		/// <summary>
		///     Declares the <c>mono_*</c> tool containers and their JSON metadata. None is declared yet; the domain is
		///     new in v2 and replaces no legacy tool.
		/// </summary>
		/// <returns>The same builder.</returns>
		public ICheatEngineMcpBuilder AddMonoTools()
		{
			ArgumentNullException.ThrowIfNull(builder);
			// B12 adds AddJsonTypeInfoResolver(MonoJsonContext.Default) and one AddToolType<...>() per container here.
			return builder;
		}
	}
}
