using ModelContextProtocol.Protocol;

namespace CheatEngine.Mcp.Resources.Live;

/// <summary>Creates the private, uncached JSON result shared by every live resource projection.</summary>
internal static class LiveResourceResults
{
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
