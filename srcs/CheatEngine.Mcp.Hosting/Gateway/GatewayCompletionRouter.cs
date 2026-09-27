using CheatEngine.Mcp.Hosting.Discovery;

using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace CheatEngine.Mcp.Hosting.Gateway;

/// <summary>
///     The gateway's completion handler: it completes <c>instanceId</c> in a routed resource template from the instances
///     whose identity was verified in the last <see cref="IdentityWindow" />, ids only. Everything else returns no values
///     here; the SDK then appends the allowed values of the Local prompts and templates. Forwarding other template
///     arguments to a backend is not offered yet.
/// </summary>
internal sealed class GatewayCompletionRouter(
	InstanceIdentityVerifier verifier,
	GatewayResourceCatalog catalog,
	InstanceRegistry registry)
{
	/// <summary>The most values one completion returns.</summary>
	internal const int MaximumValues = 100;

	/// <summary>How recent a verified identity must be to be offered.</summary>
	internal static readonly TimeSpan IdentityWindow = InstanceIdentityVerifier.VerificationWindow;

	internal ValueTask<CompleteResult> CompleteAsync(RequestContext<CompleteRequestParams> context,
		CancellationToken cancellationToken)
	{
		Completion completion = new();
		if (context.Params is { Ref: ResourceTemplateReference { Uri: { } template }, Argument: { } argument } &&
			string.Equals(argument.Name, McpResourceUris.InstanceIdVariable, StringComparison.Ordinal) &&
			catalog.IsRoutedTemplate(template))
		{
			HashSet<string> active = registry.ReadActive(cancellationToken)
				.Select(static instance => instance.InstanceId).ToHashSet(StringComparer.Ordinal);
			string prefix = argument.Value ?? string.Empty;
			string[] matches = verifier.RecentlyVerified(IdentityWindow)
				.Where(active.Contains)
				.Where(instanceId => instanceId.StartsWith(prefix, StringComparison.Ordinal)).ToArray();
			completion.Values = [.. matches.Take(MaximumValues)];
			completion.Total = matches.Length;
			completion.HasMore = matches.Length > MaximumValues;
		}

		return ValueTask.FromResult(new CompleteResult { Completion = completion });
	}
}
