using System.Collections;

using CheatEngine.Mcp.Core.Contract;

using ModelContextProtocol;
using ModelContextProtocol.Protocol;

namespace CheatEngine.Mcp.Hosting.Gateway;

/// <summary>
///     The failures the gateway itself reports: for tools, the v2 envelope (<c>isError:true</c>, one text block holding
///     <c>{"error":{...}}</c> and no structured content); for resource reads, a JSON-RPC error whose <c>data</c> carries
///     the same fields. A backend's own tool result, error or not, is never rewritten.
/// </summary>
internal static class GatewayErrors
{
	/// <summary>The hint of every failure whose outcome is unknown.</summary>
	internal const string NoReplayHint = "Do not repeat a mutation; inspect state with a read-only tool first.";

	/// <summary>The hint of every failure that happened before the backend received the call.</summary>
	internal const string RediscoverHint =
		$"Call {CheatEngineToolNames.InstanceList} and use a current instanceId; the gateway never redirects a call to " +
		"another instance.";

	private const int MaximumEchoedIdLength = 64;
	private const int MaximumEchoedUriLength = 256;

	/// <summary>The requested tool is not in the gateway's listing.</summary>
	internal static CallToolResult UnknownTool(string tool)
	{
		return Create(ToolErrorKind.InvalidArgument, $"Tool '{Echo(tool)}' is not available from this gateway.", null,
			ToolHostEffect.NotStarted, "List the tools again and call one of them.");
	}

	/// <summary>The routing argument is missing, empty or not a string.</summary>
	internal static CallToolResult MissingInstanceId(string tool)
	{
		ToolError error = CheatEngineToolException.InvalidArgument(GatewayToolCatalog.InstanceIdArgumentName,
			$"a non-empty string returned by {CheatEngineToolNames.InstanceList} is required.",
			$"Call {CheatEngineToolNames.InstanceList}, then pass one of its instanceId values.").Error;
		return ToolErrorResults.Create(error with
		{
			Operation = tool
		});
	}

	/// <summary>No live, valid registry record has the requested instance id.</summary>
	internal static CallToolResult UnknownInstance(string tool, string instanceId)
	{
		return Create(ToolErrorKind.InstanceUnavailable,
			$"No enabled Cheat Engine instance has instanceId '{Echo(instanceId)}'; it may have been disabled, " +
			"enabled again or closed.", tool, ToolHostEffect.NotStarted, RediscoverHint);
	}

	/// <summary>The instance could not be reached, verified or kept connected.</summary>
	/// <param name="tool">The requested tool.</param>
	/// <param name="instanceId">The requested instance.</param>
	/// <param name="reason">A caller-safe reason that never carries a transport message.</param>
	/// <param name="effect">
	///     <see cref="ToolHostEffect.NotStarted" /> before the call was sent, <see cref="ToolHostEffect.Unknown" /> after.
	/// </param>
	internal static CallToolResult Unavailable(string tool, string instanceId, string reason, ToolHostEffect effect)
	{
		return Create(ToolErrorKind.InstanceUnavailable,
			$"Cheat Engine instance '{Echo(instanceId)}' is unavailable: {reason}", tool, effect,
			effect == ToolHostEffect.NotStarted ? RediscoverHint : NoReplayHint);
	}

	/// <summary>The call's deadline expired while the backend had it.</summary>
	internal static CallToolResult Timeout(string tool, string instanceId, TimeSpan timeout)
	{
		return Create(ToolErrorKind.Timeout,
			$"Cheat Engine instance '{Echo(instanceId)}' did not answer within {timeout.TotalSeconds:0} s; the " +
			"operation may still be running or may have completed.", tool, ToolHostEffect.Unknown, NoReplayHint);
	}

	/// <summary>A resource URI the gateway does not serve or route: a JSON-RPC not-found error.</summary>
	/// <param name="uri">The requested URI.</param>
	/// <param name="hint">What to read instead.</param>
	/// <param name="upstreamVersion">The upstream session's protocol version, which selects the code.</param>
	internal static McpProtocolException ResourceNotFound(string uri, string hint, string? upstreamVersion)
	{
		return McpResourceErrors.Create(new ToolError(ToolErrorKind.NotFound,
			$"Unknown resource URI: '{Echo(uri, MaximumEchoedUriLength)}'.", null, ToolHostEffect.NotStarted, false,
			hint), upstreamVersion);
	}

	/// <summary>A routed read that failed at the gateway: a JSON-RPC internal error with the contract data.</summary>
	/// <param name="kind">The failure kind.</param>
	/// <param name="message">A caller-safe message.</param>
	/// <param name="effect">Whether the read reached the backend.</param>
	/// <param name="hint">The next step.</param>
	/// <param name="upstreamVersion">The upstream session's protocol version.</param>
	internal static McpProtocolException ResourceFailure(ToolErrorKind kind, string message, ToolHostEffect effect,
		string hint, string? upstreamVersion)
	{
		return McpResourceErrors.Create(new ToolError(kind, message, "resources/read", effect, false, hint),
			upstreamVersion);
	}

	/// <summary>
	///     Passes a backend's JSON-RPC error on: the message loses the client's <c>Request failed (remote):</c> prefix,
	///     the contract data is kept, and a not-found code is chosen again for the upstream protocol version.
	/// </summary>
	/// <param name="backend">The backend's error, as the gateway's client raised it.</param>
	/// <param name="upstreamVersion">The upstream session's protocol version.</param>
	/// <returns>The error to throw upstream.</returns>
	internal static McpProtocolException FromBackend(McpProtocolException backend, string? upstreamVersion)
	{
		const string remotePrefix = "Request failed (remote): ";
		string message = backend.Message.StartsWith(remotePrefix, StringComparison.Ordinal)
			? backend.Message[remotePrefix.Length..]
			: backend.Message;
		bool notFound = backend.ErrorCode == McpErrorCode.ResourceNotFound ||
						(backend.Data["kind"] is string kind &&
						 kind == McpResourceErrors.ContractName(ToolErrorKind.NotFound));
		McpProtocolException upstream = new(message, backend,
			notFound ? McpResourceErrors.Code(ToolErrorKind.NotFound, upstreamVersion) : backend.ErrorCode);
		foreach (DictionaryEntry entry in backend.Data)
		{
			upstream.Data[entry.Key] = entry.Value;
		}

		return upstream;
	}

	private static CallToolResult Create(ToolErrorKind kind, string message, string? operation, ToolHostEffect effect,
		string hint)
	{
		// Retrying cannot help: the id is stale, the backend is gone, or the outcome is unknown.
		return ToolErrorResults.Create(new ToolError(kind, message, operation, effect, false, hint));
	}

	private static string Echo(string value, int maximumLength = MaximumEchoedIdLength)
	{
		return value.Length <= maximumLength
			? value
			: string.Concat(value.AsSpan(0, maximumLength), "...");
	}
}
