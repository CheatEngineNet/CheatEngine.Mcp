using System.Text.Json.Nodes;

namespace CheatEngine.Mcp.Tests.LiveQualification;

internal sealed class LiveMcpInstanceClient(LiveMcpClient client, string instanceId) : ILiveMcpToolClient
{
	public string InstanceId
	{
		get;
	} = instanceId;

	public Task<JsonNode?> CallToolAsync(string name, IReadOnlyDictionary<string, object?>? arguments = null)
	{
		Dictionary<string, object?> routed = arguments is null
			? new Dictionary<string, object?>(StringComparer.Ordinal)
			: new Dictionary<string, object?>(arguments, StringComparer.Ordinal);
		if (!routed.TryAdd("instanceId", InstanceId))
		{
			throw new ArgumentException("Bound gateway calls must not replace their instanceId.", nameof(arguments));
		}

		return client.CallToolAsync(name, routed);
	}
}
