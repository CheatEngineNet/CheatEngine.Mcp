namespace CheatEngine.Mcp.Tools.Record;

/// <summary>The composition entry point of the <c>record</c> tool domain (14 tools in the v2 catalog).</summary>
public static class RecordToolsBuilderExtensions
{
	extension(ICheatEngineMcpBuilder builder)
	{
		/// <summary>
		///     Declares the <c>record_*</c> tool containers and their JSON metadata.
		/// </summary>
		/// <returns>The same builder.</returns>
		public ICheatEngineMcpBuilder AddRecordTools()
		{
			ArgumentNullException.ThrowIfNull(builder);
			return builder
				.AddJsonTypeInfoResolver(RecordJsonContext.Default)
				.AddToolType<RecordReadTools>()
				.AddToolType<RecordMutationTools>()
				.AddToolType<RecordClearTools>();
		}
	}
}
