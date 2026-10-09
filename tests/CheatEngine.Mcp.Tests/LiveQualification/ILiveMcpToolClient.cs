using System.Text.Json.Nodes;

namespace CheatEngine.Mcp.Tests.LiveQualification;

internal interface ILiveMcpToolClient
{
	public Task<JsonNode?> CallToolAsync(string name, IReadOnlyDictionary<string, object?>? arguments = null);

	public Task<LiveMcpToolResult> CallToolRawAsync(string name,
		IReadOnlyDictionary<string, object?>? arguments = null);
}
