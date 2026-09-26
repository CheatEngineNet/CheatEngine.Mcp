using System.ComponentModel;

using CheatEngine.Client;

using ModelContextProtocol.Server;

namespace CheatEngine.Mcp.Tools;

[McpServerToolType]
public sealed class TargetResourceTool(ICheatEngineClient client, TargetResources resources)
{
	[McpServerTool(Name = "release_target_resources"), Description("Release owned scans, symbols, allocations and patches in reverse creation order before switching processes. Stops at the first incomplete release.")]
	public object ReleaseTargetResources() => ToolExecution.Run(client,
		() => resources.ReleaseAll() ?? new { success = true });
}
