using CheatEngine.Mcp.Tests.Support;

using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace CheatEngine.Mcp.Tests.Prompts;

/// <summary>
///     The workflow prompts as the gateway serves them itself, without any instance: the same argument titles as a
///     backend lists, and the completion of <c>explain_error.tool</c> within the gateway's completion bound.
/// </summary>
[Collection(nameof(SerialTestGroup))]
public sealed class WorkflowPromptGatewayTests
{
	[Fact]
	public async Task Gateway_Prompts_ListTheArgumentTitlesAndBoundTheToolNameCompletion()
	{
		await using GatewayTestHost gateway = await GatewayTestHost.StartAsync();
		await using McpClient client = await gateway.ConnectAsync();

		IList<McpClientPrompt> prompts =
			await client.ListPromptsAsync(cancellationToken: TestContext.Current.CancellationToken);
		(Completion all, Completion memory) = await WorkflowPromptTests.CompleteToolAsync(client);

		Assert.Equal(WorkflowPromptTests.DisplayNames(),
			WorkflowPromptTests.ListedTitles(prompts.Select(static prompt => prompt.ProtocolPrompt)));
		WorkflowPromptTests.AssertToolNameCompletions(all, memory);
	}
}
