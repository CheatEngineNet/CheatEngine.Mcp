using System.ComponentModel;

using CheatEngine.Client;
using CheatEngine.Client.Tables;

using ModelContextProtocol.Server;

namespace CheatEngine.Mcp.Tools;

/// <summary>Loads and saves table files admitted by the Client's configured table-root policy.</summary>
[McpServerToolType]
public sealed class CheatTableTool
{
	private readonly ICheatEngineClient _client;

	public CheatTableTool(ICheatEngineClient client)
	{
		_client = client;
	}

	[McpServerTool(Name = "load_cheat_table"), Description("Load a trusted absolute Cheat Engine table path. The Client enforces configured table roots.")]
	public object LoadCheatTable(
		[Description("Absolute .CT or .CETRAINER path under a configured trusted table root.")] string filename,
		[Description("Merge into the current table instead of replacing it.")] bool merge = false)
	{
		return ToolExecution.Run(_client, () =>
		{
			TrustedTableFile file = new(filename);
			_client.Tables.LoadTrustedTable(new TableLoadRequest(file, merge));
			return new
			{
				success = true,
				filename = file.FullPath,
				merge
			};
		});
	}

	[McpServerTool(Name = "save_cheat_table"), Description("Save the current table to a trusted absolute Cheat Engine table path.")]
	public object SaveCheatTable([Description("Absolute destination path under a configured trusted table root.")] string filename)
	{
		return ToolExecution.Run(_client, () =>
		{
			TrustedTableFile file = new(filename);
			_client.Tables.SaveTable(new TableSaveRequest(file));
			return new
			{
				success = true,
				filename = file.FullPath
			};
		});
	}
}
