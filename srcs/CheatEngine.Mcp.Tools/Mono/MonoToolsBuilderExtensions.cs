namespace CheatEngine.Mcp.Tools.Mono;

/// <summary>The composition entry point of the <c>mono</c> tool domain (14 tools in the v2 catalog).</summary>
public static class MonoToolsBuilderExtensions
{
	extension(ICheatEngineMcpBuilder builder)
	{
		/// <summary>
		///     Declares the <c>mono_*</c> tool container and its JSON metadata. Attach state and instance-search jobs are
		///     activation-owned target resources and are released through the runtime lifecycle.
		/// </summary>
		/// <returns>The same builder.</returns>
		public ICheatEngineMcpBuilder AddMonoTools()
		{
			ArgumentNullException.ThrowIfNull(builder);
			return builder
				.AddJsonTypeInfoResolver(MonoJsonContext.Default)
				.AddToolType<MonoTools>();
		}
	}
}
