using System.Text.Json;

namespace CheatEngine.Mcp.Core.Contract;

/// <summary>The failure reported by a tool call: <c>isError:true</c> with <c>{"error":{...}}</c> as its only content.</summary>
/// <param name="Kind">The failure class.</param>
/// <param name="Message">A caller-facing explanation.</param>
/// <param name="Operation">The Cheat Engine operation that failed, when known.</param>
/// <param name="HostEffect">What the call did to Cheat Engine or the target before it failed.</param>
/// <param name="Retryable">
///     Whether repeating the identical call later is safe and may succeed: only a busy or cancelled call that never
///     applied an effect.
/// </param>
/// <param name="Hint">The recommended next step, when there is one.</param>
/// <param name="Details">Structured context, such as the invalid parameter or the completed steps of a partial effect.</param>
public sealed record ToolError(
	ToolErrorKind Kind,
	string Message,
	string? Operation,
	ToolHostEffect HostEffect,
	bool Retryable,
	string? Hint = null,
	JsonElement? Details = null);
