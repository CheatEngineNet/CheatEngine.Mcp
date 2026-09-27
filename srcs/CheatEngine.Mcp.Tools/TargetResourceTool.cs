using System.ComponentModel;

using CheatEngine.Client;

using ModelContextProtocol.Server;

namespace CheatEngine.Mcp.Tools;

[McpServerToolType]
public sealed class TargetResourceTool(ICheatEngineClient client, TargetResources resources)
{
	[McpServerTool(Name = "release_target_resources")]
	[Description(
		"Release owned scans, symbols, allocations and patches in reverse creation order before switching processes. Stops at the first incomplete release.")]
	public object ReleaseTargetResources()
	{
		return ToolExecution.Run(client, () =>
		{
			ReleaseAllResult result = resources.ReleaseAll(client.Stopping);
			if (result.Failed is { } failed)
			{
				return new
				{
					success = false,
					error = "An owned target resource could not be fully released.",
					release = failed.Release.Kind.ToString(),
					hostEffect = failed.Release.HostEffect.ToString(),
					resource = failed.Resource,
					retryable = failed.Release.IsRetryable,
					manualRecoveryRequired = failed.Release.RequiresManualRecovery
				};
			}

			return result.IsComplete
				? new
				{
					success = true
				}
				: ToolExecution.Error(
					$"{result.Remaining.Count} resource(s) still hold host state and need manual recovery or an acknowledgement.");
		});
	}
}
