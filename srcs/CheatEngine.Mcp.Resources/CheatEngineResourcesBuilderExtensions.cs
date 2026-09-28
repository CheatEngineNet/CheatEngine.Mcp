using CheatEngine.Mcp.Resources.Docs;
using CheatEngine.Mcp.Resources.Live;

namespace CheatEngine.Mcp.Resources;

/// <summary>The Resources project's single composition entry point.</summary>
public static class CheatEngineResourcesBuilderExtensions
{
	extension(ICheatEngineMcpBuilder builder)
	{
		/// <summary>
		///     Declares every Cheat Engine resource container: the knowledge documents and the workflow bodies, which are
		///     Local (static) and served by every backend and by the gateway, plus the live
		///     <c>cheatengine://instance/…</c> projections, each the structured result of one read-only, ungated,
		///     <c>short</c> v2 tool named by its <see cref="McpSourceToolAttribute" />.
		/// </summary>
		/// <returns>The same builder.</returns>
		public ICheatEngineMcpBuilder AddResources()
		{
			ArgumentNullException.ThrowIfNull(builder);
			return builder
				.AddResourceType<CheatEngineDocResources>()
				.AddResourceType<RuntimeLiveResources>()
				.AddResourceType<ProcessLiveResources>()
				.AddResourceType<ModuleLiveResources>()
				.AddResourceType<MemoryLiveResources>()
				.AddResourceType<RecordLiveResources>()
				.AddResourceType<StructureLiveResources>()
				.AddResourceType<CodeLiveResources>()
				.AddResourceType<ScanLiveResources>()
				.AddResourceType<AsmLiveResources>()
				.AddResourceType<SymbolLiveResources>()
				.AddResourceType<PointerLiveResources>()
				.AddResourceType<DebuggerLiveResources>()
				.AddResourceType<SpeedhackLiveResources>();
		}
	}
}
