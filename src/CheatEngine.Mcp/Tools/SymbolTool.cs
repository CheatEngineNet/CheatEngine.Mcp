using System.ComponentModel;

using CheatEngine.Client;
using CheatEngine.Client.Inspection;
using CheatEngine.SDK.Engine.Inspection;

using ModelContextProtocol.Server;

namespace CheatEngine.Mcp.Tools;

[McpServerToolType]
public sealed class SymbolTool
{
	private readonly ICheatEngineClient _client;

	public SymbolTool(ICheatEngineClient client)
	{
		ArgumentNullException.ThrowIfNull(client);
		_client = client;
	}

	[McpServerTool(Name = "enum_modules"), Description("Copy a bounded module list from the selected target.")]
	public object EnumModules([Description("Maximum modules to copy (1-4096).")] int maximumResults = 1024)
	{
		return ToolExecution.Run(_client, () =>
		{
			if (maximumResults is < 1 or > 4096)
			{
				return ToolExecution.Error("maximumResults must be between 1 and 4096.");
			}

			return new
			{
				success = true,
				modules = _client.Inspection.GetModules(new InspectionCollectionRequest(maximumResults))
			};
		});
	}

	[McpServerTool(Name = "get_symbol_info"), Description("Get copied metadata for one Cheat Engine symbol expression.")]
	public object GetSymbolInfo([Description("A Cheat Engine symbol expression.")] string symbolName)
	{
		return ToolExecution.Run(_client, () => new
		{
			success = true,
			symbol = _client.Inspection.GetSymbol(new SymbolExpression(symbolName))
		});
	}

	[McpServerTool(Name = "enum_module_sections"), Description("Copy a bounded section list for one target module through CheatEngine.Client.")]
	public object EnumerateModuleSections([Description("Module name, up to 256 characters.")] string module,
		[Description("Maximum sections to copy (1-4096).")] int maximumResults = 1024)
	{
		return ToolExecution.Run(_client, () =>
		{
			if (string.IsNullOrWhiteSpace(module) || module.Length > 256)
			{
				return ToolExecution.Error("module must contain 1 to 256 characters.");
			}

			if (maximumResults is < 1 or > 4096)
			{
				return ToolExecution.Error("maximumResults must be between 1 and 4096.");
			}

			return new
			{
				success = true,
				sections = _client.Inspection.GetModuleSections(new ModuleName(module), new InspectionCollectionRequest(maximumResults))
			};
		});
	}

	[McpServerTool(Name = "get_name_from_address"), Description("Resolve the best Cheat Engine name for an address.")]
	public object GetNameFromAddress([Description("A hexadecimal address or Cheat Engine address expression.")] string address)
	{
		return ToolExecution.Run(_client, () =>
		{
			string name = _client.Inspection.ResolveName(ToolExecution.Address(_client, address));
			return new
			{
				success = true,
				name
			};
		});
	}
}
