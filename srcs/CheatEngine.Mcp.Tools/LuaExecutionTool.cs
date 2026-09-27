using System.ComponentModel;

using CheatEngine.Client;
using CheatEngine.Client.Lua;

using ModelContextProtocol.Server;

namespace CheatEngine.Mcp.Tools;

/// <summary>Runs trusted arbitrary Lua through the Client capability, enabled by default.</summary>
[McpServerToolType]
public sealed class LuaExecutionTool
{
	private readonly ICheatEngineClient _client;
	private readonly IUnsafeLuaClient? _unsafeLua;

	public LuaExecutionTool(ICheatEngineClient client, IUnsafeLuaClient? unsafeLua = null)
	{
		_client = client;
		_unsafeLua = unsafeLua;
	}

	[McpServerTool(Name = "execute_lua")]
	[Description(
		"Execute trusted Lua through the Client capability, enabled by default. Prefer typed MCP tools whenever possible.")]
	public object ExecuteLua(
		[Description("Trusted Lua source to execute.")]
		string script,
		[Description("Optional non-empty chunk name for Lua diagnostics.")]
		string? chunkName = null)
	{
		if (_unsafeLua is null)
		{
			return ToolExecution.Error("Unsafe Lua execution is disabled by this server.");
		}

		return ToolExecution.Run(_client, () =>
		{
			_unsafeLua.Execute(new LuaScript(script, chunkName));
			return new
			{
				success = true,
				message = "Executed successfully."
			};
		});
	}
}
