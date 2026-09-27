using CheatEngine.Mcp.Resources.Docs;

namespace CheatEngine.Mcp.Resources;

/// <summary>The Resources project's single composition entry point.</summary>
public static class CheatEngineResourcesBuilderExtensions
{
	extension(ICheatEngineMcpBuilder builder)
	{
		/// <summary>
		///     Declares every Cheat Engine resource container: the knowledge documents and the workflow bodies, which are
		///     Local (static) and served by every backend and by the gateway. The live <c>cheatengine://instance/…</c>
		///     projections of read-only tools join here once those tools exist.
		/// </summary>
		/// <returns>The same builder.</returns>
		public ICheatEngineMcpBuilder AddResources()
		{
			ArgumentNullException.ThrowIfNull(builder);
			return builder.AddResourceType<CheatEngineDocResources>();
		}
	}
}
