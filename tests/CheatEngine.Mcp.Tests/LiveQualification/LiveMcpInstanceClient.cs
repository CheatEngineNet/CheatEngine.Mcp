using System.Text.Json.Nodes;

namespace CheatEngine.Mcp.Tests.LiveQualification;

internal sealed class LiveMcpInstanceClient(LiveMcpClient client, string instanceId,
	CancellationToken? callCancellation = null) : ILiveMcpToolClient
{
	public string InstanceId
	{
		get;
	} = instanceId;

	/// <summary>Uses a separate bounded cancellation scope for cleanup on the same gateway connection.</summary>
	internal LiveMcpInstanceClient WithCancellation(CancellationToken cancellationToken) =>
		new(client, InstanceId, cancellationToken);

	public Task<JsonNode?> CallToolAsync(string name, IReadOnlyDictionary<string, object?>? arguments = null)
	{
		return callCancellation is { } token
			? client.CallToolWithCancellationAsync(name, Route(arguments), token)
			: client.CallToolAsync(name, Route(arguments));
	}

	public Task<LiveMcpToolResult> CallToolRawAsync(string name,
		IReadOnlyDictionary<string, object?>? arguments = null) => callCancellation is { } token
			? client.CallToolRawWithCancellationAsync(name, Route(arguments), token)
			: client.CallToolRawAsync(name, Route(arguments));

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
