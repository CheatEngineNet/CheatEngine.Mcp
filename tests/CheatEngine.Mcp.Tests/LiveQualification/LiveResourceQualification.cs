using System.Runtime.Versioning;
using System.Text.Json.Nodes;

using CheatEngine.Mcp.Core.Contract;
using CheatEngine.Mcp.Prompts.Workflows;
using CheatEngine.Mcp.Resources.Knowledge;

using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace CheatEngine.Mcp.Tests.LiveQualification;

/// <summary>Read-only resource and prompt checks through the real packaged stdio gateway.</summary>
[SupportedOSPlatform("windows")]
internal static class LiveResourceQualification
{
	internal static async Task VerifyAsync(LiveSandboxSession sandbox, LiveMcpClient gateway)
	{
		Assert.Equal(LiveMcpClient.ProtocolVersion, gateway.NegotiatedProtocolVersion);
		ListResourcesResult resources = await gateway.ListResourcesAsync();
		Assert.Null(resources.NextCursor);
		Assert.Equal(CheatEngineKnowledge.DocumentSlugs.Select(McpResourceUris.Doc).Order(StringComparer.Ordinal),
			resources.Resources.Where(resource => resource.Uri.StartsWith("cheatengine://docs/", StringComparison.Ordinal))
				.Select(resource => resource.Uri).Order(StringComparer.Ordinal));
		Assert.Contains(resources.Resources, resource => resource.Uri == McpResourceUris.GatewayInstances);
		IList<McpClientResourceTemplate> templates = await gateway.ListResourceTemplatesAsync();
		Assert.Contains(templates, template => template.UriTemplate == "cheatengine://instances/{instanceId}/runtime");
		Assert.Contains(templates, template => template.UriTemplate == "cheatengine://docs/workflows/{workflow}");
		ReadResourceResult safety = await gateway.ReadResourceAsync(McpResourceUris.Doc("safety"));
		Assert.Equal(CheatEngineKnowledge.ReadDocument("safety"),
			Assert.IsType<TextResourceContents>(Assert.Single(safety.Contents)).Text);
		IList<McpClientPrompt> prompts = await gateway.ListPromptsAsync();
		Assert.Equal(CheatEngineWorkflows.All.Count, prompts.Count);
		GetPromptResult prompt = await gateway.GetPromptAsync("find_writer",
			new Dictionary<string, object?> { ["address"] = sandbox.HostA.TargetAddress });
		Assert.Contains(CheatEngineToolNames.InstanceList,
			Assert.IsType<TextContentBlock>(prompt.Messages[0].Content).Text, StringComparison.Ordinal);
		foreach (LiveSandboxHost host in new[] { sandbox.HostA, sandbox.HostB })
		{
			ReadResourceResult runtime = await gateway.ReadResourceAsync(McpResourceUris.ToGateway(
				McpResourceUris.InstancePrefix + "runtime", host.InstanceId));
			JsonNode payload = JsonNode.Parse(Assert.IsType<TextResourceContents>(Assert.Single(runtime.Contents)).Text)!;
			Assert.Equal(Path.GetFileName(host.PluginPath), payload["runtime"]!["pluginFileName"]!.GetValue<string>());
			Assert.True(payload["runtime"]!["epoch"]!.GetValue<long>() > 0);
		}
		await VerifyInstanceCompletionsAsync(gateway, sandbox.HostA.InstanceId, sandbox.HostB.InstanceId);
		sandbox.Record("packaged_resources_prompts_completions", new
		{
			client = "ModelContextProtocol .NET test client",
			protocol = gateway.NegotiatedProtocolVersion,
			resources = resources.Resources.Count,
			templates = templates.Count,
			prompts = prompts.Count,
			limitation = "This does not qualify an interactive client application's configuration or UI."
		});
	}

	internal static async Task VerifyInstanceCompletionsAsync(LiveMcpClient gateway, params string[] expected)
	{
		CompleteResult completion = await gateway.CompleteInstancesAsync();
		Assert.Equal(expected.Order(StringComparer.Ordinal), completion.Completion.Values.Order(StringComparer.Ordinal));
		Assert.False(completion.Completion.HasMore == true);
	}
}
