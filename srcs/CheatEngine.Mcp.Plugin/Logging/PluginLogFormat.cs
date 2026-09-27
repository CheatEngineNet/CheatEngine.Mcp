using System.Globalization;

using Microsoft.Extensions.Logging;

namespace CheatEngine.Mcp.Plugin.Logging;

/// <summary>
///     The plugin log line: <c>yyyy-MM-dd HH:mm:ss.ffff|LEVEL|category|message[ exception]</c> and CRLF, in the
///     invariant culture, local time and UTF-8 without a byte order mark; the layout of the former NLog file target.
/// </summary>
internal static class PluginLogFormat
{
	/// <summary>Appended when an entry is cut at its bound.</summary>
	internal const string TruncationMarker = " …[truncated]";

	/// <summary>Formats one entry, exception included, bounded to <paramref name="maxChars" /> before the line end.</summary>
	/// <param name="entry">The entry.</param>
	/// <param name="maxChars">The bound of the text before the line end.</param>
	/// <returns>The line, ending with CRLF.</returns>
	internal static string Line(in PluginLogEntry entry, int maxChars)
	{
		string prefix = string.Create(CultureInfo.InvariantCulture,
			$"{entry.Timestamp:yyyy-MM-dd HH:mm:ss.ffff}|{Level(entry.Level)}|{entry.Category}|");
		string text = entry.Exception is null
			? string.Concat(prefix, entry.Message)
			: string.Concat(prefix, entry.Message, " ", entry.Exception.ToString());
		return string.Concat(Truncate(text, maxChars), "\r\n");
	}

	/// <summary>Cuts a text at its bound, never between the halves of a surrogate pair.</summary>
	/// <param name="text">The text.</param>
	/// <param name="maxChars">The bound, marker included.</param>
	/// <returns>The text, or its bounded prefix followed by <see cref="TruncationMarker" />.</returns>
	internal static string Truncate(string text, int maxChars)
	{
		if (text.Length <= maxChars)
		{
			return text;
		}

		int length = Math.Max(0, maxChars - TruncationMarker.Length);
		if (length > 0 && char.IsHighSurrogate(text[length - 1]))
		{
			length--;
		}

		return string.Concat(text.AsSpan(0, length), TruncationMarker);
	}

	/// <summary>The upper-case level names of the former NLog layout.</summary>
	/// <param name="level">The level.</param>
	/// <returns>The level name.</returns>
	internal static string Level(LogLevel level)
	{
		return level switch
		{
			LogLevel.Trace => "TRACE",
			LogLevel.Debug => "DEBUG",
			LogLevel.Information => "INFO",
			LogLevel.Warning => "WARN",
			LogLevel.Error => "ERROR",
			LogLevel.Critical => "FATAL",
			_ => "OFF"
		};
	}
}
