using System.ComponentModel;

using CheatEngine.Client;
using CheatEngine.Client.Runtime;

using ModelContextProtocol.Server;

namespace CheatEngine.Mcp.Tools;

[McpServerToolType]
public sealed class RuntimeTool(ICheatEngineClient client)
{
	[McpServerTool(Name = "get_runtime_info"), Description("Read the active Client epoch, host version and capability evidence before using optional features.")]
	public object GetRuntimeInfo() => ToolExecution.Run(client, () =>
	{
		CheatEngineRuntimeSnapshot snapshot = client.Runtime.GetSnapshot();
		return new
		{
			success = true,
			epoch = snapshot.Epoch,
			version = snapshot.Version,
			platform = snapshot.Platform,
			capabilities = snapshot.Capabilities.Entries.ToArray().Select(capability => new
			{
				name = capability.Capability.Value,
				state = capability.State.ToString(),
				isAvailable = capability.IsAvailable,
				reason = capability.Reason,
				evidence = capability.Evidence
			}).ToArray()
		};
	});
}
