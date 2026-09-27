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
		///     Declares every v2 Cheat Engine tool container and the services they share in catalog order.
		/// </summary>
		/// <returns>The same builder.</returns>
		public ICheatEngineMcpBuilder AddTools()
		{
			ArgumentNullException.ThrowIfNull(builder);
			if (builder.Mode is CheatEngineMcpMode.Backend)
			{
				// Core registers TargetResources and its guard; the visible scanner has no lease of its own.
				builder.Services.TryAddEnumerable(ServiceDescriptor
					.Scoped<ITargetTransitionGuard, MainScannerTransitionGuard>());
			}

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
				.AddLuaTools();
		}
	}
}
