using Microsoft.Extensions.Logging;

namespace CheatEngine.Mcp.Plugin.Logging;

/// <summary>One queued log entry; the writer formats the exception, never the logging thread.</summary>
/// <param name="Timestamp">The local time the entry was logged.</param>
/// <param name="Level">The entry level.</param>
/// <param name="Category">The logger category.</param>
/// <param name="Message">The formatted message, already bounded.</param>
/// <param name="Exception">The exception to append, if any.</param>
internal readonly record struct PluginLogEntry(
	DateTimeOffset Timestamp,
	LogLevel Level,
	string Category,
	string Message,
	Exception? Exception);
