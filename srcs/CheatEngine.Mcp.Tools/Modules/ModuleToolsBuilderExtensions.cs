namespace CheatEngine.Mcp.Tools.Modules;

/// <summary>The composition entry point of the <c>module</c> tool domain (4 tools in the v2 catalog).</summary>
public static class ModuleToolsBuilderExtensions
{
	extension(ICheatEngineMcpBuilder builder)
	{
		/// <summary>
		///     Declares the <c>module_*</c> tool containers and their JSON metadata: <see cref="ModuleTools" />
		///     (<c>module_list</c>, <c>module_get</c>), <see cref="ModuleExportTools" /> (<c>module_list_exports</c>) and
		///     <see cref="ModulePatchTools" /> (<c>module_find_patches</c>).
		/// </summary>
		/// <returns>The same builder.</returns>
		public ICheatEngineMcpBuilder AddModuleTools()
		{
			ArgumentNullException.ThrowIfNull(builder);
			return builder
				.AddJsonTypeInfoResolver(ModuleJsonContext.Default)
				.AddToolType<ModuleTools>()
				.AddToolType<ModuleExportTools>()
				.AddToolType<ModulePatchTools>();
		}
	}
}
