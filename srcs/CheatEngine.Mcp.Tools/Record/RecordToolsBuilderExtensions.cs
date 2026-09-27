namespace CheatEngine.Mcp.Tools.Record;

/// <summary>The composition entry point of the <c>record</c> tool domain (12 tools in the v2 catalog).</summary>
public static class RecordToolsBuilderExtensions
{
	extension(ICheatEngineMcpBuilder builder)
	{
		/// <summary>
		///     Declares the <c>record_*</c> tool containers and their JSON metadata. None is declared yet; the legacy tools
		///     this domain replaces are still declared by <c>AddTools()</c>.
		/// </summary>
		/// <returns>The same builder.</returns>
		public ICheatEngineMcpBuilder AddRecordTools()
		{
			ArgumentNullException.ThrowIfNull(builder);
			// B8 adds AddJsonTypeInfoResolver(RecordJsonContext.Default) and one AddToolType<...>() per container
			// here, then removes the legacy lines it replaces from AddTools().
			return builder;
		}
	}
}
