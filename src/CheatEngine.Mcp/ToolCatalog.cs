using System.Text.Json;

using CheatEngine.Mcp.Tools;

using Microsoft.Extensions.DependencyInjection;

using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace CheatEngine.Mcp;

/// <summary>One explicit tool catalog shared by the CE backend and the gateway's schema discovery.</summary>
internal static class ToolCatalog
{
	internal static IMcpServerBuilder Configure(IMcpServerBuilder builder) => builder
		.WithToolsAndSchemaTransform<RuntimeTool>()
		.WithToolsAndSchemaTransform<TargetResourceTool>()
		.WithToolsAndSchemaTransform<ProcessTool>()
		.WithToolsAndSchemaTransform<MemoryTool>()
		.WithToolsAndSchemaTransform<PointerTool>()
		.WithToolsAndSchemaTransform<PointerScanTool>()
		.WithToolsAndSchemaTransform<ScanTool>()
		.WithToolsAndSchemaTransform<SymbolTool>()
		.WithToolsAndSchemaTransform<MemoryViewTool>()
		.WithToolsAndSchemaTransform<AdvancedMemoryTool>()
		.WithToolsAndSchemaTransform<AddressListTool>()
		.WithToolsAndSchemaTransform<CheatTableTool>()
		.WithToolsAndSchemaTransform<LuaExecutionTool>()
		.WithToolsAndSchemaTransform<AssemblyTool>()
		.WithToolsAndSchemaTransform<AutoAssemblyTool>()
		.WithToolsAndSchemaTransform<SymbolRegistryTool>()
		.WithToolsAndSchemaTransform<ConversionTool>()
		.WithToolsAndSchemaTransform<LuaMemoryTool>()
		.WithToolsAndSchemaTransform<LuaProcessTool>()
		.WithToolsAndSchemaTransform<LuaSymbolsTool>()
		.WithToolsAndSchemaTransform<LuaStructureTool>()
		.WithToolsAndSchemaTransform<LuaCodeTool>()
		.WithToolsAndSchemaTransform<LuaTableTool>()
		.WithToolsAndSchemaTransform<LuaDebuggerTool>()
		.WithToolsAndSchemaTransform<LuaDebuggerCaptureTool>()
		.WithToolsAndSchemaTransform<LuaDebuggerTraceTool>()
		.WithToolsAndSchemaTransform<LuaInjectionTool>()
		.WithToolsAndSchemaTransform<LuaDbvmTool>();

	internal static IReadOnlyList<Tool> GetTools()
	{
		ServiceCollection services = new();
		services.AddLogging();
		Configure(services.AddMcpServer());
		using ServiceProvider provider = services.BuildServiceProvider();
		// SchemaTransform defers construction of tool targets until invocation; no Client or native state is created here.
		return provider.GetServices<McpServerTool>()
			.Select(tool => JsonSerializer.SerializeToElement(tool.ProtocolTool, McpJsonUtilities.DefaultOptions)
				.Deserialize<Tool>(McpJsonUtilities.DefaultOptions)!)
			.OrderBy(tool => tool.Name, StringComparer.Ordinal).ToArray();
	}
}
