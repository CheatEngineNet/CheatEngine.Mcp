using Microsoft.Extensions.Options;

using ModelContextProtocol.Server;

namespace CheatEngine.Mcp.Core.Composition;

/// <summary>Fails server startup on duplicate primitive identifiers, which the MCP SDK would otherwise drop silently.</summary>
internal sealed class McpPrimitiveValidator(
	IEnumerable<McpServerTool> tools,
	IEnumerable<McpServerPrompt> prompts,
	IEnumerable<McpServerResource> resources) : IValidateOptions<McpServerOptions>
{
	public ValidateOptionsResult Validate(string? name, McpServerOptions options)
	{
		List<string> failures = [];
		AddDuplicates(failures, "tool", tools.Select(static tool => tool.ProtocolTool.Name));
		AddDuplicates(failures, "prompt", prompts.Select(static prompt => prompt.ProtocolPrompt.Name));
		AddDuplicates(failures, "resource",
			resources.Select(static resource => resource.ProtocolResourceTemplate.UriTemplate));
		return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
	}

	private static void AddDuplicates(List<string> failures, string kind, IEnumerable<string> identifiers)
	{
		foreach (IGrouping<string, string> duplicate in identifiers
			         .GroupBy(static identifier => identifier, StringComparer.Ordinal)
			         .Where(static group => group.Count() > 1))
		{
			failures.Add($"Duplicate MCP {kind} '{duplicate.Key}' would be dropped silently.");
		}
	}
}
