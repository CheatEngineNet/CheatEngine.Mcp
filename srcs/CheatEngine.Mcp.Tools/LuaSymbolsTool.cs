using System.ComponentModel;

using CheatEngine.Client;

using ModelContextProtocol.Server;

namespace CheatEngine.Mcp.Tools;

/// <summary>Exposes additional copied symbol and RTTI inspection through Cheat Engine Lua.</summary>
[McpServerToolType]
public sealed class LuaSymbolsTool
{
	private const int MaximumIdentifierLength = 256;
	private readonly ICheatEngineClient _client;

	public LuaSymbolsTool(ICheatEngineClient client)
	{
		ArgumentNullException.ThrowIfNull(client);
		_client = client;
	}

	[McpServerTool(Name = "lookup_rtti_class_name")]
	[Description("Resolve a likely RTTI class name for a target address, returning found=false when unavailable.")]
	public object GetRttiClassName([Description("Target address expression or number.")] string address)
	{
		return LuaToolRuntime.Invoke(_client, "getRTTIClassName",
			"local resolved = getAddressSafe(a[1]); assert(resolved ~= nil, 'Address could not be resolved.'); local className = getRTTIClassName(resolved); return { address = string.format('0x%X', resolved), className = className, found = className ~= nil }",
			address);
	}

	[McpServerTool(Name = "get_module_preference")]
	[Description("Get Cheat Engine's symbol lookup module-precedence list, capped at 1024 entries.")]
	public object GetModulePreference()
	{
		return LuaToolRuntime.Invoke(_client, "getModulePreference",
			"local modules = getModulePreference(); local result = {}; for i = 1, math.min(#modules, 1024) do result[i] = modules[i] end; return { modules = result, truncated = #modules > 1024 }");
	}

	[McpServerTool(Name = "set_module_preference")]
	[Description("Put one extensionless module name first in Cheat Engine symbol lookup precedence.")]
	public object SetModulePreference([Description("Extensionless module name, up to 256 characters.")] string module)
	{
		if (!IsIdentifier(module) || module.Contains('.', StringComparison.Ordinal))
		{
			return ToolExecution.Error("module must be an extensionless name containing 1 to 256 characters.");
		}

		return LuaToolRuntime.Invoke(_client, "setModulePreference",
			"setModulePreference(a[1]); return { module = a[1] }", module);
	}

	[McpServerTool(Name = "get_structure_elements")]
	[Description("Get PDB-backed structure elements by name, capped at 4096 entries.")]
	public object GetStructureElements([Description("Structure name, up to 256 characters.")] string name)
	{
		if (!IsIdentifier(name))
		{
			return ToolExecution.Error("name must contain 1 to 256 characters.");
		}

		return LuaToolRuntime.Invoke(_client, "getStructureElementsFromName",
			"local elements = getStructureElementsFromName(a[1]); if elements == nil then return { found = false, elements = {} } end; local result = {}; for i = 1, math.min(#elements, 4096) do result[i] = elements[i] end; return { found = true, elements = result, truncated = #elements > 4096 }",
			name);
	}

	[McpServerTool(Name = "load_new_symbols")]
	[Description("Ask Cheat Engine to scan for loaded modules and add their symbols.")]
	public object LoadNewSymbols()
	{
		return LuaToolRuntime.Invoke(_client, "loadNewSymbols", "loadNewSymbols(); return { loaded = true }");
	}

	[McpServerTool(Name = "reinitialize_symbols")]
	[Description("Reinitialize Cheat Engine's symbol handler.")]
	public object ReinitializeSymbols([Description("Wait for completion before returning.")] bool waitUntilDone = true)
	{
		return LuaToolRuntime.Invoke(_client, "reinitializeSymbolhandler",
			"reinitializeSymbolhandler(a[1]); return { symbolsLoaded = symbolsDoneLoading() }", waitUntilDone);
	}

	[McpServerTool(Name = "reinitialize_dotnet_symbols")]
	[Description("Reinitialize the .NET symbol list for an optional module.")]
	public object ReinitializeDotNetSymbols(
		[Description("Optional module name, up to 256 characters.")]
		string? module = null)
	{
		if (module is { } value && !IsIdentifier(value))
		{
			return ToolExecution.Error("module must contain 1 to 256 characters.");
		}

		return LuaToolRuntime.Invoke(_client, "reinitializeDotNetSymbolhandler",
			"reinitializeDotNetSymbolhandler(a[1]); return { module = a[1] }", module);
	}

	[McpServerTool(Name = "wait_for_symbols")]
	[Description("Wait for one Cheat Engine symbol-loading stage.")]
	public object WaitForSymbols([Description("sections, exports, dotnet, or pdb.")] string stage)
	{
		string? function = stage?.ToLowerInvariant() switch
		{
			"sections" => "waitForSections",
			"exports" => "waitForExports",
			"dotnet" => "waitForDotNet",
			"pdb" => "waitForPDB",
			_ => null
		};
		if (function is null)
		{
			return ToolExecution.Error("stage must be sections, exports, dotnet, or pdb.");
		}

		return LuaToolRuntime.Invoke(_client, function, function + "(); return { stage = a[1], loaded = true }", stage);
	}

	[McpServerTool(Name = "get_symbols_loading_state")]
	[Description("Get whether Cheat Engine reports all symbols loaded.")]
	public object GetSymbolsLoadingState()
	{
		return LuaToolRuntime.Invoke(_client, "symbolsDoneLoading", "return { loaded = symbolsDoneLoading() }");
	}

	private static bool IsIdentifier(string? value)
	{
		return !string.IsNullOrWhiteSpace(value) && value.Length <= MaximumIdentifierLength;
	}
}
