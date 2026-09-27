using System.Collections.Concurrent;
using System.Text;

using Microsoft.Extensions.Logging;

namespace CheatEngine.Mcp.Tests.Support;

/// <summary>
///     Records every log entry a host writes, with its category, formatted message, structured values and exception,
///     so a test can search everything a log file or console would have shown.
/// </summary>
internal sealed class LogCapture : ILoggerProvider
{
	private readonly ConcurrentQueue<CapturedLog> _entries = new();

	internal IReadOnlyCollection<CapturedLog> Entries => _entries;

	public ILogger CreateLogger(string categoryName)
	{
		return new CaptureLogger(this, categoryName);
	}

	public void Dispose()
	{
		// The capture outlives the hosts that borrow it.
	}

	/// <summary>The backend's view of the capture: a lease whose disposal never closes it.</summary>
	internal IMcpBackendLogging ForBackend(LogLevel minimumLevel)
	{
		return new BackendLogging(this, minimumLevel);
	}

	private sealed class CaptureLogger(LogCapture capture, string category) : ILogger
	{
		public IDisposable? BeginScope<TState>(TState state) where TState : notnull
		{
			return null;
		}

		public bool IsEnabled(LogLevel logLevel)
		{
			return logLevel != LogLevel.None;
		}

		public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
			Func<TState, Exception?, string> formatter)
		{
			StringBuilder text = new(formatter(state, exception));
			if (state is IEnumerable<KeyValuePair<string, object?>> values)
			{
				foreach ((string key, object? value) in values)
				{
					text.Append(" | ").Append(key).Append('=').Append(value);
				}
			}

			if (exception is not null)
			{
				text.Append(" | ").Append(exception);
			}

			capture._entries.Enqueue(new CapturedLog(logLevel, category, text.ToString()));
		}
	}

	private sealed class BackendLogging(LogCapture capture, LogLevel minimumLevel) : IMcpBackendLogging
	{
		public LogLevel MinimumLevel => minimumLevel;

		public ILoggerProvider CreateProvider()
		{
			return capture;
		}
	}
}

/// <summary>One captured log entry.</summary>
/// <param name="Level">The entry's level.</param>
/// <param name="Category">The logger category.</param>
/// <param name="Text">The formatted message, structured values and exception.</param>
internal sealed record CapturedLog(LogLevel Level, string Category, string Text);
