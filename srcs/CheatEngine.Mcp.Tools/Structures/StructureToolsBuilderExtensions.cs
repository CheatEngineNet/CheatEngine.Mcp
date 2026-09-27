namespace CheatEngine.Mcp.Tools.Structures;

/// <summary>The composition entry point of the <c>structure</c> tool domain (13 tools in the v2 catalog).</summary>
public static class StructureToolsBuilderExtensions
{
	extension(ICheatEngineMcpBuilder builder)
	{
		/// <summary>
		///     Declares the <c>structure_*</c> tool containers and their JSON metadata. Structures are Cheat Engine's global
		///     state, so the domain registers no activation service of its own.
		/// </summary>
		/// <returns>The same builder.</returns>
		public ICheatEngineMcpBuilder AddStructureTools()
		{
			ArgumentNullException.ThrowIfNull(builder);
			return builder
				.AddJsonTypeInfoResolver(StructuresJsonContext.Default)
				.AddToolType<StructureTools>()
				.AddToolType<StructureElementTools>()
				.AddToolType<StructureValueTools>()
				.AddToolType<StructureCompareTools>();
		}
	}
}
