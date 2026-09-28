using ModelContextProtocol.Protocol;

namespace CheatEngine.Mcp.Resources.Live;

/// <summary>
///     Creates the private, uncached JSON result shared by every live resource projection, and holds the annotations
///     every live resource declares.
/// </summary>
internal static class LiveResourceResults
{
	/// <summary>
	///     The annotation priority of every live resource: optional context the assistant reads on demand, below every
	///     knowledge document. The audience is the assistant only.
	/// </summary>
	internal const double Priority = 0.3;

	/// <summary>The largest offset a paged live resource accepts: any non-negative 32-bit index.</summary>
	internal const int MaximumOffset = int.MaxValue;

	/// <summary>Wraps a projected tool result.</summary>
	/// <param name="uri">The canonical URI of the read.</param>
	/// <param name="text">The tool's structured result as JSON.</param>
	/// <returns>One JSON text content, never cached and private to the caller.</returns>
	internal static ReadResourceResult Json(string uri, string text)
	{
		return new ReadResourceResult
		{
			Contents =
			[
				new TextResourceContents { Uri = uri, MimeType = McpResourceUris.JsonMimeType, Text = text }
			],
			TimeToLive = TimeSpan.Zero,
			CacheScope = CacheScope.Private
		};
	}
}
