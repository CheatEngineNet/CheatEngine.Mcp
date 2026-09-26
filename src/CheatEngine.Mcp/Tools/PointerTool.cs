using System.ComponentModel;

using CheatEngine.Client;
using CheatEngine.Client.Memory;
using CheatEngine.SDK.Engine.Values;

using ModelContextProtocol.Server;

namespace CheatEngine.Mcp.Tools;

[McpServerToolType]
public sealed class PointerTool
{
	private readonly ICheatEngineClient _client;

	public PointerTool(ICheatEngineClient client)
	{
		ArgumentNullException.ThrowIfNull(client);
		_client = client;
	}

	[McpServerTool(Name = "read_pointer_chain"), Description("Resolve a bounded pointer chain using the target pointer width.")]
	public object ReadPointerChain([Description("Address containing the first pointer.")] string baseAddress,
		[Description("One to 64 signed offsets, applied after each dereference.")] long[] offsets)
	{
		return ToolExecution.Run(_client, () =>
		{
			ArgumentNullException.ThrowIfNull(offsets);
			Address resolved = _client.Memory.ResolvePointerChain(
				new PointerChainRequest(ToolExecution.Address(_client, baseAddress), offsets));
			return new
			{
				success = true,
				address = $"0x{resolved.Value:X}"
			};
		});
	}
}
