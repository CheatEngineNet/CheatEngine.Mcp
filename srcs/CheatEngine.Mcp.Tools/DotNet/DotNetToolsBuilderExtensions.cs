namespace CheatEngine.Mcp.Tools.DotNet;

/// <summary>The composition entry point of the <c>dotnet</c> tool domain (9 tools in the v2 catalog).</summary>
public static class DotNetToolsBuilderExtensions
{
	extension(ICheatEngineMcpBuilder builder)
	{
		/// <summary>
		///     Declares the <c>dotnet_*</c> tool container and its source-generated JSON metadata. The collector itself is
		///     a Cheat Engine runtime service, reached only through fixed Lua routed by <see cref="ToolDispatch" />.
		/// </summary>
		/// <returns>The same builder.</returns>
		public ICheatEngineMcpBuilder AddDotNetTools()
		{
			ArgumentNullException.ThrowIfNull(builder);
			return builder
				.AddJsonTypeInfoResolver(DotNetJsonContext.Default)
				.AddToolType<DotNetTools>();
		}
	}
}
