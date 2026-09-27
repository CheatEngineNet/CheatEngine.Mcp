using CheatEngine.Mcp.Core.Contract;

using ModelContextProtocol.Protocol;

namespace CheatEngine.Mcp.Hosting.Gateway;

/// <summary>
///     The failures the gateway itself reports, in the v2 envelope: <c>isError:true</c>, one text block holding
///     <c>{"error":{...}}</c> and no structured content. A backend's own result, error or not, is never rewritten.
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
		return ToolErrorResults.Create(error with { Operation = tool });
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

	private static CallToolResult Create(ToolErrorKind kind, string message, string? operation, ToolHostEffect effect,
		string hint)
	{
		// Retrying cannot help: the id is stale, the backend is gone, or the outcome is unknown.
		return ToolErrorResults.Create(new ToolError(kind, message, operation, effect, false, hint));
	}

	private static string Echo(string value)
	{
		return value.Length <= MaximumEchoedIdLength
			? value
			: string.Concat(value.AsSpan(0, MaximumEchoedIdLength), "...");
	}
}
