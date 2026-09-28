using System.Collections.Immutable;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

using CheatEngine.Mcp.Tests.Support;

using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace CheatEngine.Mcp.Tests.Core;

/// <summary>
///     The completion bound every host installs: a <c>completion/complete</c> answer keeps at most
///     <see cref="McpCompletions.MaximumValues" /> values, whoever computed them, including the allowed values the SDK
///     appends without a bound, and then reports the whole count as <c>total</c> and sets <c>hasMore</c>.
/// </summary>
[Collection(nameof(SerialTestGroup))]
public sealed class CompletionBoundTests
{
	/// <summary>More allowed values than one completion may return: value000 to value149.</summary>
	private static readonly ImmutableArray<string> ManyValues =
		[.. Enumerable.Range(0, 150).Select(static index => $"value{index:D3}")];

	private static readonly CheatEngineMcpPrimitiveOptions Manifest =
		CheatEngineMcpComposition.CreateManifest(CheatEngineMcpMode.Catalog,
			static builder => builder.AddPromptType<ManyValuesPrompt>());

	[Fact]
	public void Bound_LongerCompletion_KeepsTheFirstValuesWithTheTotalAndHasMore()
	{
		Completion completion = new()
		{
			Values = [.. ManyValues],
			Total = ManyValues.Length
		};

		McpCompletions.Bound(completion);

		Assert.Equal(ManyValues.Take(McpCompletions.MaximumValues), completion.Values);
		Assert.Equal((ManyValues.Length, true), (completion.Total, completion.HasMore));
	}

	[Fact]
	public void Bound_LargerReportedTotal_IsKept()
	{
		Completion completion = new()
		{
			Values = [.. ManyValues.Take(120)],
			Total = 500
		};

		McpCompletions.Bound(completion);

		Assert.Equal(ManyValues.Take(McpCompletions.MaximumValues), completion.Values);
		Assert.Equal((500, true), (completion.Total, completion.HasMore));
	}

	[Theory]
	[InlineData(0)]
	[InlineData(McpCompletions.MaximumValues)]
	public void Bound_CompletionWithinTheBound_IsLeftAsItIs(int count)
	{
		Completion completion = new()
		{
			Values = [.. ManyValues.Take(count)],
			Total = 7
		};
		IList<string> values = completion.Values;

		McpCompletions.Bound(completion);

		Assert.Same(values, completion.Values);
		Assert.Equal((7, (bool?) null), (completion.Total, completion.HasMore));
	}

	[Fact]
	public async Task Backend_AllowedValuesBeyondTheBound_CompleteTheFirstValuesWithTotalAndHasMore()
	{
		await using TestMcpPipeline pipeline = await TestMcpPipeline.StartAsync(Manifest, McpPrimitiveBinding.Catalog);

		(Completion all, Completion narrowed) = await CompleteAsync(pipeline.Client);

		AssertBounded(all, narrowed);
	}

	[Fact]
	public async Task Gateway_AllowedValuesBeyondTheBound_CompleteTheFirstValuesWithTotalAndHasMore()
	{
		await using GatewayTestHost gateway = await GatewayTestHost.StartAsync(extraPrimitives:
			[new CheatEngineMcpPrimitive(CheatEngineMcpPrimitiveKind.Prompt, typeof(ManyValuesPrompt))]);
		await using McpClient client = await gateway.ConnectAsync();

		(Completion all, Completion narrowed) = await CompleteAsync(client);

		AssertBounded(all, narrowed);
	}

	private static async Task<(Completion All, Completion Narrowed)> CompleteAsync(McpClient client)
	{
		PromptReference reference = new()
		{
			Name = ManyValuesPrompt.Name
		};
		CompleteResult all = await client.CompleteAsync(reference, "value", string.Empty,
			cancellationToken: TestContext.Current.CancellationToken);
		CompleteResult narrowed = await client.CompleteAsync(reference, "value", "VALUE1",
			cancellationToken: TestContext.Current.CancellationToken);
		return (all.Completion, narrowed.Completion);
	}

	private static void AssertBounded(Completion all, Completion narrowed)
	{
		string[] hundreds = [.. ManyValues.Where(static value => value.StartsWith("value1", StringComparison.Ordinal))];

		Assert.Equal(ManyValues.Take(McpCompletions.MaximumValues), all.Values);
		Assert.Equal((ManyValues.Length, true), (all.Total, all.HasMore));
		Assert.Equal(hundreds, narrowed.Values);
		Assert.Equal(hundreds.Length, narrowed.Total);
		Assert.NotEqual(true, narrowed.HasMore);
	}

	/// <summary>The allowed values of <see cref="ManyValuesPrompt" />.</summary>
	[AttributeUsage(AttributeTargets.Parameter)]
	internal sealed class ManyValuesAttribute() : AllowedValuesAttribute([.. ManyValues]);

	[McpServerPromptType]
	public sealed class ManyValuesPrompt
	{
		internal const string Name = "many_values";

		[McpServerPrompt(Name = Name, Title = "Many values")]
		[Description("A prompt whose argument allows more values than one completion returns.")]
		public static string Prompt([Description("A value.")][Display(Name = "Value")][ManyValues] string value)
		{
			return value;
		}
	}
}
