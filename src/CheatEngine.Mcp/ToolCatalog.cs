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
	internal static IMcpServerBuilder Configure(IMcpServerBuilder builder, bool schemaOnly = false) => builder
		.WithToolsAndSchemaTransform<RuntimeTool>(schemaOnly)
		.WithToolsAndSchemaTransform<TargetResourceTool>(schemaOnly)
		.WithToolsAndSchemaTransform<ProcessTool>(schemaOnly)
		.WithToolsAndSchemaTransform<MemoryTool>(schemaOnly)
		.WithToolsAndSchemaTransform<PointerTool>(schemaOnly)
		.WithToolsAndSchemaTransform<PointerScanTool>(schemaOnly)
		.WithToolsAndSchemaTransform<ScanTool>(schemaOnly)
		.WithToolsAndSchemaTransform<SymbolTool>(schemaOnly)
		.WithToolsAndSchemaTransform<MemoryViewTool>(schemaOnly)
		.WithToolsAndSchemaTransform<AdvancedMemoryTool>(schemaOnly)
		.WithToolsAndSchemaTransform<AddressListTool>(schemaOnly)
		.WithToolsAndSchemaTransform<CheatTableTool>(schemaOnly)
		.WithToolsAndSchemaTransform<LuaExecutionTool>(schemaOnly)
		.WithToolsAndSchemaTransform<AssemblyTool>(schemaOnly)
		.WithToolsAndSchemaTransform<AutoAssemblyTool>(schemaOnly)
		.WithToolsAndSchemaTransform<SymbolRegistryTool>(schemaOnly)
		.WithToolsAndSchemaTransform<ConversionTool>(schemaOnly)
		.WithToolsAndSchemaTransform<LuaMemoryTool>(schemaOnly)
		.WithToolsAndSchemaTransform<LuaProcessTool>(schemaOnly)
		.WithToolsAndSchemaTransform<LuaSymbolsTool>(schemaOnly)
		.WithToolsAndSchemaTransform<LuaStructureTool>(schemaOnly)
		.WithToolsAndSchemaTransform<LuaCodeTool>(schemaOnly)
		.WithToolsAndSchemaTransform<LuaTableTool>(schemaOnly)
		.WithToolsAndSchemaTransform<LuaDebuggerTool>(schemaOnly)
		.WithToolsAndSchemaTransform<LuaDebuggerCaptureTool>(schemaOnly)
		.WithToolsAndSchemaTransform<LuaDebuggerTraceTool>(schemaOnly)
		.WithToolsAndSchemaTransform<LuaInjectionTool>(schemaOnly)
		.WithToolsAndSchemaTransform<LuaDbvmTool>(schemaOnly);

	internal static IReadOnlyList<Tool> GetTools()
	{
		ServiceCollection services = new();
		services.AddLogging();
		Configure(services.AddMcpServer(), schemaOnly: true);
		using ServiceProvider provider = services.BuildServiceProvider();
		// Schema-only registrations expose metadata without creating Client or native state.
		return provider.GetServices<McpServerTool>()
			.Select(tool => JsonSerializer.SerializeToElement(tool.ProtocolTool, McpJsonUtilities.DefaultOptions)
				.Deserialize<Tool>(McpJsonUtilities.DefaultOptions)!)
			.OrderBy(tool => tool.Name, StringComparer.Ordinal).ToArray();
	}
}
