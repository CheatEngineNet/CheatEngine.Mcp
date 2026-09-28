using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

using CheatEngine.Client.Results;

using ModelContextProtocol;

namespace CheatEngine.Mcp.Core.Contract;

/// <summary>
///     The only way a tool reports a failure. The Core call-tool filter turns it into <c>isError:true</c> with the
///     <c>{"error":{...}}</c> envelope and no structured content.
/// </summary>
/// <remarks>
///     It derives from <see cref="McpException" /> so that even without the filter the SDK surfaces its message, and never
///     from <see cref="McpProtocolException" />, which the SDK would turn into a JSON-RPC error instead of a tool result.
/// </remarks>
public sealed class CheatEngineToolException : McpException
{
	internal const string TimeoutHint = "Do not repeat a mutation; inspect state with a read-only tool first.";
	internal const string InternalHint = "Inspect state with a read-only tool before repeating a mutation.";

	/// <summary>Creates the exception for a contract error.</summary>
	/// <param name="error">The error to report.</param>
	/// <param name="innerException">The underlying fault, which is never sent to the caller.</param>
	public CheatEngineToolException(ToolError error, Exception? innerException = null)
		: base(Validate(error).Message, innerException)
	{
		Error = error;
	}

	/// <summary>The error reported to the caller.</summary>
	public ToolError Error
	{
		get;
	}

	/// <summary>An argument is missing, malformed or out of range; nothing was attempted.</summary>
	/// <param name="parameter">The parameter name as it appears in the tool's input schema.</param>
	/// <param name="message">What is wrong with it.</param>
	/// <param name="hint">The recommended correction, if any.</param>
	/// <returns>The exception to throw.</returns>
	public static CheatEngineToolException InvalidArgument(string parameter, string message, string? hint = null)
	{
		return Argument(ToolErrorKind.InvalidArgument, parameter, message, hint);
	}

	/// <summary>A size, count or result limit would be exceeded; nothing was attempted.</summary>
	/// <param name="parameter">The parameter that sets the limit.</param>
	/// <param name="message">Which limit applies.</param>
	/// <returns>The exception to throw.</returns>
	public static CheatEngineToolException LimitExceeded(string parameter, string message)
	{
		return Argument(ToolErrorKind.LimitExceeded, parameter, message, ToolFailureMapping.LimitHint);
	}

	/// <summary>The target or Cheat Engine is not in a state that allows the operation.</summary>
	/// <param name="message">Which state blocks the operation.</param>
	/// <param name="hint">How to reach a state that allows it, if known.</param>
	/// <param name="effect">What the call did before it stopped.</param>
	/// <returns>The exception to throw.</returns>
	public static CheatEngineToolException InvalidState(string message, string? hint = null,
		ToolHostEffect effect = ToolHostEffect.NotStarted)
	{
		return Create(ToolErrorKind.InvalidState, message, null, effect, hint);
	}

	/// <summary>The addressed object does not exist; nothing was changed.</summary>
	/// <param name="message">What was not found.</param>
	/// <param name="hint">Where to look it up, if known.</param>
	/// <returns>The exception to throw.</returns>
	public static CheatEngineToolException NotFound(string message, string? hint = null)
	{
		return Create(ToolErrorKind.NotFound, message, null, ToolHostEffect.NotStarted, hint);
	}

	/// <summary>No process is attached; nothing was attempted.</summary>
	/// <param name="operation">The operation that needs a process, if known.</param>
	/// <returns>The exception to throw.</returns>
	public static CheatEngineToolException NotAttached(string? operation = null)
	{
		return Create(ToolErrorKind.NotAttached, "No process is attached.", operation, ToolHostEffect.NotStarted,
			ToolFailureMapping.AttachHint);
	}

	/// <summary>A competing operation or retained resource blocks the call; repeating it later may succeed.</summary>
	/// <param name="message">What blocks the call.</param>
	/// <param name="hint">How to release the blocker, if known.</param>
	/// <returns>The exception to throw.</returns>
	public static CheatEngineToolException Busy(string message, string? hint = null)
	{
		return Create(ToolErrorKind.Busy, message, null, ToolHostEffect.NotStarted, hint);
	}

	/// <summary>A server setting disables the tool or option; nothing was attempted.</summary>
	/// <param name="featureName">The <c>Mcp</c> setting that disables it, such as <c>EnableKernelAccess</c>.</param>
	/// <param name="toolName">The refused tool.</param>
	/// <returns>The exception to throw.</returns>
	public static CheatEngineToolException CapabilityDisabled(string featureName, string toolName)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(featureName);
		ArgumentException.ThrowIfNullOrWhiteSpace(toolName);
		return Create(ToolErrorKind.CapabilityDisabled, $"{toolName} is disabled by the Mcp:{featureName} setting.",
			null, ToolHostEffect.NotStarted,
			$"Set Mcp:{featureName} to true in appsettings.json, then disable and re-enable the plugin.");
	}

	/// <summary>This Cheat Engine build, target or runtime does not support the operation; nothing was attempted.</summary>
	/// <param name="message">What is unsupported.</param>
	/// <param name="operation">The unsupported operation, if known.</param>
	/// <returns>The exception to throw.</returns>
	public static CheatEngineToolException Unsupported(string message, string? operation = null)
	{
		return Create(ToolErrorKind.Unsupported, message, operation, ToolHostEffect.NotStarted, null);
	}

	/// <summary>The operation did not finish in time; it may still run or may have completed.</summary>
	/// <param name="message">What timed out.</param>
	/// <param name="effect">What is known about the host effect; usually unknown.</param>
	/// <returns>The exception to throw.</returns>
	public static CheatEngineToolException Timeout(string message, ToolHostEffect effect = ToolHostEffect.Unknown)
	{
		return Create(ToolErrorKind.Timeout, message, null, effect, TimeoutHint);
	}

	/// <summary>
	///     A composite operation completed only some of its steps. It is never a success: <paramref name="details" /> tells
	///     which steps completed.
	/// </summary>
	/// <typeparam name="TDetails">The details record.</typeparam>
	/// <param name="message">What completed and what did not.</param>
	/// <param name="effect">The overall host effect.</param>
	/// <param name="details">The completed and failed steps.</param>
	/// <param name="detailsType">The source-generated metadata of <typeparamref name="TDetails" />.</param>
	/// <param name="retryable">Whether repeating the identical call is safe, such as a retryable release.</param>
	/// <param name="hint">The recovery step, if any.</param>
	/// <returns>The exception to throw.</returns>
	public static CheatEngineToolException PartialEffect<TDetails>(string message, ToolHostEffect effect,
		TDetails details, JsonTypeInfo<TDetails> detailsType, bool retryable = false, string? hint = null)
	{
		ArgumentNullException.ThrowIfNull(detailsType);
		JsonElement serialized = JsonSerializer.SerializeToElement(details, detailsType);
		return new CheatEngineToolException(new ToolError(ToolErrorKind.PartialEffect, message, null, effect, retryable,
			hint, serialized));
	}

	/// <summary>Maps a classified Client failure, keeping its operation and host effect.</summary>
	/// <param name="failure">The Client failure.</param>
	/// <param name="activationStopping">Whether the activation's stopping token was set.</param>
	/// <returns>The exception to throw.</returns>
	public static CheatEngineToolException FromFailure(in CheatEngineFailure failure, bool activationStopping)
	{
		return new CheatEngineToolException(ToolFailureMapping.Map(failure, activationStopping), failure.Exception);
	}

	/// <summary>An unexpected fault whose outcome is unknown.</summary>
	/// <param name="message">A caller-safe description; never include paths, tokens or target data.</param>
	/// <param name="innerException">The underlying fault, which is never sent to the caller.</param>
	/// <returns>The exception to throw.</returns>
	public static CheatEngineToolException Internal(string message, Exception? innerException = null)
	{
		return new CheatEngineToolException(
			new ToolError(ToolErrorKind.Internal, message, null, ToolHostEffect.Unknown, false, InternalHint),
			innerException);
	}

	private static CheatEngineToolException Create(ToolErrorKind kind, string message, string? operation,
		ToolHostEffect effect, string? hint)
	{
		return new CheatEngineToolException(new ToolError(kind, message, operation, effect,
			ToolFailureMapping.IsRetryable(kind, effect), hint));
	}

	private static CheatEngineToolException Argument(ToolErrorKind kind, string parameter, string message,
		string? hint)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(parameter);
		JsonElement details =
			JsonSerializer.SerializeToElement(new ToolParameterDetails(parameter),
				CoreJsonContext.Default.ToolParameterDetails);
		return new CheatEngineToolException(new ToolError(kind, $"{parameter}: {message}", null,
			ToolHostEffect.NotStarted, false, hint, details));
	}

	private static ToolError Validate(ToolError error)
	{
		ArgumentNullException.ThrowIfNull(error);
		ArgumentException.ThrowIfNullOrWhiteSpace(error.Message, nameof(error));
		return error;
	}
}
