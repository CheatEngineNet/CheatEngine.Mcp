namespace CheatEngine.Mcp.Tools.DotNet;

/// <summary>The composition entry point of the <c>dotnet</c> tool domain (9 tools in the v2 catalog).</summary>
public static class DotNetToolsBuilderExtensions
{
	extension(ICheatEngineMcpBuilder builder)
	{
		/// <summary>
		///     Declares the <c>dotnet_*</c> tool containers and their JSON metadata. None is declared yet; the domain is
		///     new in v2 and replaces no legacy tool.
		/// </summary>
		/// <returns>The same builder.</returns>
		public ICheatEngineMcpBuilder AddDotNetTools()
		{
			ArgumentNullException.ThrowIfNull(builder);
			// B12 adds AddJsonTypeInfoResolver(DotNetJsonContext.Default) and one AddToolType<...>() per container here.
			return builder;
		}
	}
}
