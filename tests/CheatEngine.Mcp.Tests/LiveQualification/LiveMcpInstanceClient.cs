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
		return client.CallToolAsync(name, Route(arguments));
	}

	public Task<LiveMcpToolResult> CallToolRawAsync(string name,
		IReadOnlyDictionary<string, object?>? arguments = null) => client.CallToolRawAsync(name, Route(arguments));

	private Dictionary<string, object?> Route(IReadOnlyDictionary<string, object?>? arguments)
	{
		Dictionary<string, object?> routed = arguments is null
			? new Dictionary<string, object?>(StringComparer.Ordinal)
			: new Dictionary<string, object?>(arguments, StringComparer.Ordinal);
		if (!routed.TryAdd("instanceId", InstanceId))
		{
			throw new ArgumentException("Bound gateway calls must not replace their instanceId.", nameof(arguments));
		}

		return routed;
	}
}
