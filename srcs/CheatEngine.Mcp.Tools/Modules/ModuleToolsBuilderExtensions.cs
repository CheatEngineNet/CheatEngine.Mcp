namespace CheatEngine.Mcp.Tools.Modules;

/// <summary>The composition entry point of the <c>module</c> tool domain (4 tools in the v2 catalog).</summary>
public static class ModuleToolsBuilderExtensions
{
	extension(ICheatEngineMcpBuilder builder)
	{
		/// <summary>
		///     Declares the <c>module_*</c> tool containers and their JSON metadata. None is declared yet; the legacy tools
		///     this domain replaces are still declared by <c>AddTools()</c>.
		/// </summary>
		/// <returns>The same builder.</returns>
		public ICheatEngineMcpBuilder AddModuleTools()
		{
			ArgumentNullException.ThrowIfNull(builder);
			// B5 adds AddJsonTypeInfoResolver(ModuleJsonContext.Default) and one AddToolType<...>() per container
			// here, then removes the legacy lines it replaces from AddTools().
			return builder;
		}
	}
}
