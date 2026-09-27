using System.ComponentModel;

using CheatEngine.Client;
using CheatEngine.Client.Inspection;

using ModelContextProtocol.Server;

namespace CheatEngine.Mcp.Tools;

[McpServerToolType]
public sealed class MemoryViewTool
{
	private readonly ICheatEngineClient _client;

	public MemoryViewTool(ICheatEngineClient client)
	{
		ArgumentNullException.ThrowIfNull(client);
		_client = client;
	}

	[McpServerTool(Name = "enum_memory_regions")]
	[Description("Copy a bounded target memory-region map.")]
	public object EnumMemoryRegions([Description("Maximum regions to copy (1-16384).")] int maximumResults = 4096)
	{
		return ToolExecution.Run(_client, () =>
		{
			if (maximumResults is < 1 or > 16384)
			{
				return ToolExecution.Error("maximumResults must be between 1 and 16384.");
			}

			return new
			{
				success = true,
				regions = _client.Inspection.GetMemoryRegions(new InspectionCollectionRequest(maximumResults))
			};
		});
	}

	[McpServerTool(Name = "get_memory_region")]
	[Description("Get copied metadata for the region containing an address.")]
	public object GetMemoryRegion(
		[Description("A hexadecimal address or Cheat Engine address expression.")]
		string address)
	{
		return ToolExecution.Run(_client,
			() => new
			{
				success = true, region = _client.Inspection.GetMemoryRegion(ToolExecution.Address(_client, address))
			});
	}
}
