using CheatEngine.Mcp.Tools.Aob;
using CheatEngine.Mcp.Tools.Asm;
using CheatEngine.Mcp.Tools.Code;
using CheatEngine.Mcp.Tools.Debugger;
using CheatEngine.Mcp.Tools.DotNet;
using CheatEngine.Mcp.Tools.Exec;
using CheatEngine.Mcp.Tools.Kernel;
using CheatEngine.Mcp.Tools.Lua;
using CheatEngine.Mcp.Tools.Memory;
using CheatEngine.Mcp.Tools.Modules;
using CheatEngine.Mcp.Tools.Mono;
using CheatEngine.Mcp.Tools.Pointer;
using CheatEngine.Mcp.Tools.Processes;
using CheatEngine.Mcp.Tools.Record;
using CheatEngine.Mcp.Tools.Runtime;
using CheatEngine.Mcp.Tools.Scan;
using CheatEngine.Mcp.Tools.Speedhack;
using CheatEngine.Mcp.Tools.Structures;
using CheatEngine.Mcp.Tools.Symbol;
using CheatEngine.Mcp.Tools.Table;
using CheatEngine.Mcp.Tools.Util;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CheatEngine.Mcp.Tools;

/// <summary>The Tools project's single composition entry point.</summary>
public static class CheatEngineToolsBuilderExtensions
{
	extension(ICheatEngineMcpBuilder builder)
	{
		/// <summary>
		///     Declares every Cheat Engine tool container and the services they share: the v2 domains in catalog order,
		///     then the legacy containers that no domain has replaced yet.
		/// </summary>
		/// <returns>The same builder.</returns>
		public ICheatEngineMcpBuilder AddTools()
		{
			ArgumentNullException.ThrowIfNull(builder);
			if (builder.Mode is CheatEngineMcpMode.Backend)
			{
				// Core registers TargetResources, its guard and TargetTransitionGuards; these are the tools' own guards.
				builder.Services.TryAddScoped<LuaDebuggerCaptureGuard>();
				builder.Services.TryAddEnumerable(
					ServiceDescriptor.Scoped<ITargetTransitionGuard, LuaDebuggerCaptureGuard>(static services =>
						services.GetRequiredService<LuaDebuggerCaptureGuard>()));
				builder.Services.TryAddEnumerable(ServiceDescriptor
					.Scoped<ITargetTransitionGuard, MainScannerTransitionGuard>());
			}

			// Each domain batch fills its own Add<Domain>Tools() and deletes the legacy lines below that it replaces.
			return builder
				.AddRuntimeTools()
				.AddProcessTools()
				.AddMemoryTools()
				.AddScanTools()
				.AddAobTools()
				.AddPointerTools()
				.AddModuleTools()
				.AddSymbolTools()
				.AddSpeedhackTools()
				.AddUtilTools()
				.AddCodeTools()
				.AddAsmTools()
				.AddRecordTools()
				.AddTableTools()
				.AddStructureTools()
				.AddDebuggerTools()
				.AddExecTools()
				.AddDotNetTools()
				.AddMonoTools()
				.AddKernelTools()
				.AddLuaTools()
				// Legacy containers, removed by 2.0.0.
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
