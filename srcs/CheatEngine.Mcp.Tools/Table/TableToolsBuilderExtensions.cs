namespace CheatEngine.Mcp.Tools.Table;

/// <summary>The composition entry point of the <c>table</c> tool domain (3 tools in the v2 catalog).</summary>
public static class TableToolsBuilderExtensions
{
	extension(ICheatEngineMcpBuilder builder)
	{
		/// <summary>
		///     Declares the <c>table_*</c> tool container and its JSON metadata.
		/// </summary>
		/// <returns>The same builder.</returns>
		public ICheatEngineMcpBuilder AddTableTools()
		{
			ArgumentNullException.ThrowIfNull(builder);
			return builder
				.AddJsonTypeInfoResolver(TableJsonContext.Default)
				.AddToolType<TableTools>();
		}
	}
}
