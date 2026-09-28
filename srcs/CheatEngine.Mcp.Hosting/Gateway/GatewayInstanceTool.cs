using System.ComponentModel;
using System.Text.Json;

using CheatEngine.Mcp.Core.Contract;
using CheatEngine.Mcp.Hosting.Discovery;

using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace CheatEngine.Mcp.Hosting.Gateway;

/// <summary>The gateway's own <c>instance_list</c> tool: discovery of the verified local Cheat Engine instances.</summary>
internal sealed class GatewayInstanceTool(InstanceRegistry registry, InstanceIdentityVerifier verifier)
{
	/// <summary>The tool name; the only tool that takes no <c>instanceId</c>.</summary>
	internal const string Name = CheatEngineToolNames.InstanceList;

	private const int MaximumParallelProbes = 16;
	private static readonly JsonSerializerOptions SerializerOptions = CreateSerializerOptions();

	/// <summary>How long discovery may take before it returns the instances verified so far.</summary>
	internal static readonly TimeSpan DiscoveryBudget = TimeSpan.FromSeconds(10);

	/// <summary>Lists the instances whose backend confirmed the identity its registry record claims.</summary>
	/// <param name="cancellationToken">The request's cancellation.</param>
	/// <returns>The verified instances, never their tokens or endpoints.</returns>
	[McpServerTool(Name = Name, Title = "List Cheat Engine instances", ReadOnly = true, Destructive = false,
		Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
	[Description("Lists the local Cheat Engine instances whose MCP plugin is enabled and verified. Call it first: "
				 + "every other tool needs one returned instanceId, and there is no default instance. Names may repeat; "
				 + "route by instanceId. An instanceId changes whenever the plugin is enabled again or CE restarts.")]
	public async Task<InstanceListResult> ListAsync(CancellationToken cancellationToken)
	{
		InstanceListEntry?[] discovered = [];
		bool discoveryIncomplete = false;
		using CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		deadline.CancelAfter(DiscoveryBudget);
		try
		{
			InstanceDescriptor[] instances = registry.ReadActive(deadline.Token).ToArray();
			discovered = new InstanceListEntry?[instances.Length];
			await Parallel.ForEachAsync(Enumerable.Range(0, instances.Length),
				new ParallelOptions
				{
					MaxDegreeOfParallelism = MaximumParallelProbes,
					CancellationToken = deadline.Token
				},
				async (index, token) =>
				{
					discovered[index] = await ProbeAsync(instances[index], token).ConfigureAwait(false);
				}).ConfigureAwait(false);
		}
		catch (OperationCanceledException) when (deadline.IsCancellationRequested &&
												 !cancellationToken.IsCancellationRequested)
		{
			// Return the candidates verified within the budget; never imply that a partial list is complete.
			discoveryIncomplete = true;
		}

		cancellationToken.ThrowIfCancellationRequested();
		return new InstanceListResult(discovered.OfType<InstanceListEntry>().ToArray(), discoveryIncomplete);
	}

	/// <summary>The tool's protocol metadata, built from <see cref="ListAsync" /> without any instance.</summary>
	/// <returns>The tool definition the gateway lists.</returns>
	internal static Tool CreateProtocolTool()
	{
		McpServerToolCreateOptions options = new()
		{
			SerializerOptions = SerializerOptions,
			SchemaCreateOptions = SchemaTransform.SchemaCreateOptions
		};
		// Schema only: the router calls ListAsync on its own instance, so this factory never runs.
		return McpServerTool.Create(typeof(GatewayInstanceTool).GetMethod(nameof(ListAsync))!,
			static _ => throw new InvalidOperationException($"The gateway invokes {Name} directly."),
			options).ProtocolTool;
	}

	/// <summary>Runs the tool and shapes its result: structured content plus the same JSON as text.</summary>
	/// <param name="cancellationToken">The request's cancellation.</param>
	/// <returns>The tool result.</returns>
	internal async Task<CallToolResult> CallAsync(CancellationToken cancellationToken)
	{
		InstanceListResult result = await ListAsync(cancellationToken).ConfigureAwait(false);
		JsonElement payload = JsonSerializer.SerializeToElement(result, HostingJsonContext.Default.InstanceListResult);
		return new CallToolResult
		{
			StructuredContent = payload,
			Content = [new TextContentBlock { Text = payload.GetRawText() }]
		};
	}

	private async Task<InstanceListEntry?> ProbeAsync(InstanceDescriptor instance, CancellationToken cancellationToken)
	{
		try
		{
			await verifier.VerifyAsync(instance, cancellationToken).ConfigureAwait(false);
			return new InstanceListEntry(instance.InstanceId, instance.Name, instance.ProcessId,
				instance.PluginVersion);
		}
		catch (InstanceUnavailableException)
		{
			// A registry record is only a candidate; an unverified one is left out.
			return null;
		}
	}

	private static JsonSerializerOptions CreateSerializerOptions()
	{
		CheatEngineMcpPrimitiveOptions manifest = CheatEngineMcpComposition.CreateManifest(
			CheatEngineMcpMode.Catalog,
			static builder => builder.AddJsonTypeInfoResolver(HostingJsonContext.Default));
		return CheatEngineMcpJson.CreateOptions(manifest);
	}
}
