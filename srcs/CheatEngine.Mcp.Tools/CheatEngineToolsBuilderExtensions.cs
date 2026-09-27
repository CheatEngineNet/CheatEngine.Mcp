using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CheatEngine.Mcp.Tools;

/// <summary>The Tools project's single composition entry point.</summary>
public static class CheatEngineToolsBuilderExtensions
{
	extension(ICheatEngineMcpBuilder builder)
	{
		/// <summary>Declares every Cheat Engine tool container, in catalog order, and the services they share.</summary>
		/// <returns>The same builder.</returns>
		public ICheatEngineMcpBuilder AddTools()
		{
			ArgumentNullException.ThrowIfNull(builder);
			if (builder.Mode is CheatEngineMcpMode.Backend)
			{
				// Shared by the tools of one activation; the target-transition guards rely on these being registered.
				builder.Services.TryAddScoped<TargetResources>();
				builder.Services.TryAddScoped<LuaDebuggerCaptureGuard>();
			}

			return builder
				.AddToolType<RuntimeTool>()
				.AddToolType<TargetResourceTool>()
				.AddToolType<ProcessTool>()
				.AddToolType<MemoryTool>()
				.AddToolType<PointerTool>()
				.AddToolType<PointerScanTool>()
				.AddToolType<ScanTool>()
				.AddToolType<SymbolTool>()
				.AddToolType<MemoryViewTool>()
				.AddToolType<AdvancedMemoryTool>()
				.AddToolType<AddressListTool>()
				.AddToolType<CheatTableTool>()
				.AddToolType<LuaExecutionTool>()
				.AddToolType<AssemblyTool>()
				.AddToolType<AutoAssemblyTool>()
				.AddToolType<SymbolRegistryTool>()
				.AddToolType<ConversionTool>()
				.AddToolType<LuaMemoryTool>()
				.AddToolType<LuaProcessTool>()
				.AddToolType<LuaSymbolsTool>()
				.AddToolType<LuaStructureTool>()
				.AddToolType<LuaCodeTool>()
				.AddToolType<LuaTableTool>()
				.AddToolType<LuaDebuggerTool>()
				.AddToolType<LuaDebuggerCaptureTool>()
				.AddToolType<LuaDebuggerTraceTool>()
				.AddToolType<LuaInjectionTool>()
				.AddToolType<LuaDbvmTool>();
		}
	}
}
