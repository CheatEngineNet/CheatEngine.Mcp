using System.Text.RegularExpressions;

using CheatEngine.Mcp.Core.Contract;
using CheatEngine.Mcp.Prompts;
using CheatEngine.Mcp.Tests.Support;

using Microsoft.Extensions.Options;

namespace CheatEngine.Mcp.Tests.Contract;

/// <summary>The <c>initialize</c> instructions: their text, their tool references and how they reach both hosts.</summary>
public sealed partial class ServerInstructionsTests
{
	public static TheoryData<string, string> Texts => new()
	{
		{ "gateway", McpServerInstructions.Gateway }, { "backend", McpServerInstructions.Backend }
	};

	[Theory]
	[MemberData(nameof(Texts))]
	public void Instructions_CitedToolNames_AreFrozenNames(string host, string text)
	{
		string[] cited = SnakeToken().Matches(text).Select(static match => match.Value)
			.Where(static token => IsToolDomain(token.Split('_')[0]))
			.ToArray();

		Assert.NotEmpty(cited);
		Assert.All(cited, token => Assert.True(CheatEngineToolNames.All.Contains(token),
			$"The {host} instructions cite '{token}', which is not a frozen tool name."));
	}

	[Theory]
	[MemberData(nameof(Texts))]
	public void Instructions_Length_IsAboutOneHundredThirtyWords(string host, string text)
	{
		int words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;

		Assert.True(words is >= 100 and <= 160, $"The {host} instructions have {words} words.");
	}

	[Fact]
	public void Instructions_OnlyTheGatewayVariant_RoutesThroughInstanceList()
	{
		Assert.Contains(CheatEngineToolNames.InstanceList, McpServerInstructions.Gateway, StringComparison.Ordinal);
		Assert.DoesNotContain(CheatEngineToolNames.InstanceList, McpServerInstructions.Backend,
			StringComparison.Ordinal);
		Assert.DoesNotContain("instanceId", McpServerInstructions.Backend, StringComparison.Ordinal);
	}

	[Fact]
	public void Composition_BothHosts_DeclareThePromptsInstructions()
	{
		foreach (CheatEngineMcpPrimitiveOptions manifest in new[]
				 {
					 TestComposition.BackendManifest, TestComposition.GatewayManifest
				 })
		{
			Assert.Equal(McpServerInstructions.Gateway, manifest.GatewayInstructions);
			Assert.Equal(McpServerInstructions.Backend, manifest.BackendInstructions);
		}
	}

	[Fact]
	public void SetServerInstructions_DeclaredTwice_FailsValidation()
	{
		OptionsValidationException exception = Assert.Throws<OptionsValidationException>(() =>
			CheatEngineMcpComposition.CreateManifest(CheatEngineMcpMode.Catalog, static builder => builder
				.SetServerInstructions("Gateway text.", "Backend text.")
				.SetServerInstructions("Other gateway text.", "Other backend text.")));

		Assert.Contains("server instructions 2 times", exception.Message, StringComparison.Ordinal);
	}

	[Fact]
	public void SetServerInstructions_Omitted_LeavesBothHostsWithout()
	{
		CheatEngineMcpPrimitiveOptions manifest =
			CheatEngineMcpComposition.CreateManifest(CheatEngineMcpMode.Catalog, static _ =>
			{
			});

		Assert.Null(manifest.GatewayInstructions);
		Assert.Null(manifest.BackendInstructions);
	}

	private static bool IsToolDomain(string segment)
	{
		return McpContractRules.Domains.Contains(segment) || McpContractRules.GatewayDomains.Contains(segment);
	}

	[GeneratedRegex("[a-z][a-z0-9]*(?:_[a-z0-9]+)+", RegexOptions.CultureInvariant)]
	private static partial Regex SnakeToken();
}
