using System.Globalization;

namespace CheatEngine.Mcp.Plugin.Logging;

/// <summary>The one plugin log file of a Cheat Engine process, shared by reference across activations.</summary>
/// <remarks>
///     The plugin instance owns the sink: the SDK constructs the plugin once and reuses it across enables, so this
///     is instance state, never static. The first reference starts a writer generation; the last release completes it
///     without waiting. A new generation opens the file only after the previous one drained and closed it, so a backend
///     still shutting down after its activation ended never writes the file concurrently with the next activation.
/// </remarks>
internal sealed class PluginLogSink : IDisposable
{
	private readonly Lock _gate = new();
	private readonly Func<string, Stream> _open;
	private readonly PluginLogFileOptions _options;
	private readonly TimeProvider _time;
	private bool _disposed;
	private Task _drained = Task.CompletedTask;
	private int _references;
	private PluginLogWriter? _writer;

	/// <summary>A sink with the default bounds, the system clock and the shared append stream.</summary>
	internal PluginLogSink() : this(PluginLogFileOptions.Default, TimeProvider.System, PluginLogFile.OpenAppend)
	{
	}

	/// <summary>A sink with explicit bounds, clock and stream factory.</summary>
	/// <param name="options">The file and queue bounds.</param>
	/// <param name="time">The clock of entry timestamps and retry delays.</param>
	/// <param name="open">Opens the log file for append.</param>
	internal PluginLogSink(PluginLogFileOptions options, TimeProvider time, Func<string, Stream> open)
	{
		ArgumentNullException.ThrowIfNull(options);
		ArgumentNullException.ThrowIfNull(time);
		ArgumentNullException.ThrowIfNull(open);
		options.Validate();
		_options = options;
		_time = time;
		_open = open;
	}

	/// <summary>The references currently held.</summary>
	internal int References
	{
		get
		{
			lock (_gate)
			{
				return _references;
			}
		}
	}

	/// <summary>Completes once the latest generation was released, drained and closed.</summary>
	internal Task Drained
	{
		get
		{
			lock (_gate)
			{
				return _drained;
			}
		}
	}

	/// <summary>Refuses later references; generations still referenced keep writing until they are released.</summary>
	public void Dispose()
	{
		lock (_gate)
		{
			_disposed = true;
		}
	}

	/// <summary>The log file of a data directory: <c>CheatEngine.Mcp.&lt;pid&gt;.log</c>.</summary>
	/// <param name="directory">The plugin data directory.</param>
	/// <returns>The log file path.</returns>
	internal static string FilePathIn(string directory)
	{
		return Path.Combine(directory,
			string.Create(CultureInfo.InvariantCulture, $"CheatEngine.Mcp.{Environment.ProcessId}.log"));
	}

	/// <summary>Takes a reference; the first one starts a generation that writes to <paramref name="directory" />.</summary>
	/// <param name="directory">The plugin data directory; a generation already running keeps its own file.</param>
	/// <returns>The reference, to dispose once.</returns>
	/// <exception cref="ObjectDisposedException">The sink was disposed.</exception>
	internal PluginLogReference Acquire(string directory)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(directory);
		lock (_gate)
		{
			ObjectDisposedException.ThrowIf(_disposed, this);
			if (_writer is null)
			{
				_writer = new PluginLogWriter(FilePathIn(directory), _options, _time, _open, _drained);
				_drained = _writer.Completion;
			}

			_references++;
			return new PluginLogReference(this, _writer);
		}
	}

	/// <summary>Releases one reference; the last one completes the generation without waiting for it.</summary>
	/// <param name="writer">The generation the reference belongs to.</param>
	internal void Release(PluginLogWriter writer)
	{
		lock (_gate)
		{
			if (--_references == 0)
			{
				_writer = null;
				writer.Complete();
			}
		}
	}
}
