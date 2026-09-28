using Microsoft.Extensions.Logging;

namespace CheatEngine.Mcp.Plugin.Logging;

/// <summary>One category of the plugin log; formats the message on the caller and never blocks it.</summary>
internal sealed class PluginLogger : ILogger
{
	private readonly string _category;
	private readonly LogLevel _minimumLevel;
	private readonly PluginLogReference _reference;

	internal PluginLogger(PluginLogReference reference, string category, LogLevel minimumLevel)
	{
		_reference = reference;
		_category = category;
		_minimumLevel = minimumLevel;
	}

	/// <inheritdoc />
	public IDisposable? BeginScope<TState>(TState state) where TState : notnull
	{
		return NullScope.Instance;
	}

	/// <inheritdoc />
	public bool IsEnabled(LogLevel logLevel)
	{
		return logLevel != LogLevel.None && logLevel >= _minimumLevel;
	}

	/// <inheritdoc />
	public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
		Func<TState, Exception?, string> formatter)
	{
		ArgumentNullException.ThrowIfNull(formatter);
		if (!IsEnabled(logLevel))
		{
			return;
		}

		// The exception text is built by the writer; the queue keeps a bounded message.
		string message = PluginLogFormat.Truncate(formatter(state, exception) ?? string.Empty,
			_reference.MaxEntryChars);
		_reference.TryWrite(new PluginLogEntry(_reference.Time.GetLocalNow(), logLevel, _category, message,
			exception));
	}

	private sealed class NullScope : IDisposable
	{
		internal static readonly NullScope Instance = new();

		public void Dispose()
		{
		}
	}
}
