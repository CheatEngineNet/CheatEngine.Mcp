using System.Collections.Frozen;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace CheatEngine.Mcp.Hosting.Gateway;

/// <summary>
///     Decides which upstream <c>_meta</c> entries reach a backend: only W3C trace context, by allowlist.
/// </summary>
/// <remarks>
///     Everything else belongs to the upstream session, not to the gateway's own hop: the
///     <c>io.modelcontextprotocol/*</c> per-request protocol fields (a 2026-07-28 upstream would otherwise switch the
///     pinned 2025-06-18 backend hop, or be rejected by it), the upstream <c>progressToken</c> (the gateway relays no
///     progress, so the backend must not report to a token it does not own), and vendor keys.
/// </remarks>
internal static class GatewayMeta
{
	/// <summary>The forwarded keys: W3C trace context and baggage.</summary>
	internal static readonly FrozenSet<string> Forwarded =
		FrozenSet.Create(StringComparer.Ordinal, "traceparent", "tracestate", "baggage");

	/// <summary>Copies the allowlisted string entries of an upstream <c>_meta</c>.</summary>
	/// <param name="upstream">The upstream request's <c>_meta</c>.</param>
	/// <returns>A new object with the forwarded entries, or <see langword="null" /> when none remain.</returns>
	internal static JsonObject? ForBackend(JsonObject? upstream)
	{
		if (upstream is null)
		{
			return null;
		}

		JsonObject? forwarded = null;
		foreach ((string key, JsonNode? value) in upstream)
		{
			if (Forwarded.Contains(key) && value is JsonValue text && text.GetValueKind() == JsonValueKind.String)
			{
				(forwarded ??= new JsonObject())[key] = text.DeepClone();
			}
		}

		return forwarded;
	}
}
