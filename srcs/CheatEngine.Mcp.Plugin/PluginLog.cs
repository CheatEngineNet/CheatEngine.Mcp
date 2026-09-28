using CheatEngine.Mcp.Hosting.Backend;
using CheatEngine.Mcp.Plugin.Logging;

using Microsoft.Extensions.Logging;

namespace CheatEngine.Mcp.Plugin;

/// <summary>An activation's view of the plugin log: one sink reference, the configured level and provider leases.</summary>
/// <remarks>
///     Each provider holds its own reference, so a backend still shutting down after its activation ended keeps the
///     file open. Nothing here blocks: the last release lets the background writer drain and close the file.
/// </remarks>
internal sealed class PluginLog : IMcpBackendLogging, IDisposable
{
	private readonly string _directory;
	private readonly Lock _gate = new();
	private readonly PluginLogSink? _ownedSink;
	private PluginLogReference? _reference;

	/// <summary>Takes one reference on the plugin's sink for an activation.</summary>
	/// <param name="sink">The plugin instance's sink.</param>
	/// <param name="directory">The plugin data directory.</param>
	/// <param name="minimumLevel">The configured minimum level.</param>
	internal PluginLog(PluginLogSink sink, string directory, LogLevel minimumLevel)
	{
		ArgumentNullException.ThrowIfNull(sink);
		ArgumentException.ThrowIfNullOrWhiteSpace(directory);
		Sink = sink;
		_directory = directory;
		MinimumLevel = minimumLevel;
		_reference = sink.Acquire(directory);
		LogFilePath = _reference.FilePath;
	}

	/// <summary>A log over a private sink at <see cref="LogLevel.Information" />, disposed with this log.</summary>
	/// <param name="directory">The plugin data directory.</param>
	internal PluginLog(string directory) : this(new PluginLogSink(), directory, LogLevel.Information, true)
	{
	}

	private PluginLog(PluginLogSink sink, string directory, LogLevel minimumLevel, bool ownsSink)
		: this(sink, directory, minimumLevel)
	{
		_ownedSink = ownsSink ? sink : null;
	}

	/// <summary>The file this log writes: <c>&lt;data directory&gt;/CheatEngine.Mcp.&lt;pid&gt;.log</c>.</summary>
	public string LogFilePath
	{
		get;
	}

	/// <summary>The sink this log references.</summary>
	internal PluginLogSink Sink
	{
		get;
	}

	/// <summary>Releases this log's reference, and a private sink; never waits for the writer.</summary>
	public void Dispose()
	{
		PluginLogReference? reference;
		lock (_gate)
		{
			reference = _reference;
			_reference = null;
		}

		if (reference is null)
		{
			return;
		}

		reference.Dispose();
		_ownedSink?.Dispose();
	}

	/// <inheritdoc />
	public LogLevel MinimumLevel
	{
		get;
	}

	/// <inheritdoc />
	public ILoggerProvider CreateProvider()
	{
		lock (_gate)
		{
			ObjectDisposedException.ThrowIf(_reference is null, this);
			return new PluginLoggerProvider(Sink.Acquire(_directory), MinimumLevel);
		}
	}
}
