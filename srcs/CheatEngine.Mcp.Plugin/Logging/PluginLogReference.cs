namespace CheatEngine.Mcp.Plugin.Logging;

/// <summary>One reference to the current generation of a <see cref="PluginLogSink" />.</summary>
internal sealed class PluginLogReference : IDisposable
{
	private readonly PluginLogWriter _writer;
	private PluginLogSink? _sink;

	internal PluginLogReference(PluginLogSink sink, PluginLogWriter writer)
	{
		_sink = sink;
		_writer = writer;
	}

	/// <summary>The log file the generation writes.</summary>
	internal string FilePath => _writer.FilePath;

	/// <summary>The clock of entry timestamps.</summary>
	internal TimeProvider Time => _writer.Time;

	/// <summary>The bound of one entry.</summary>
	internal int MaxEntryChars => _writer.MaxEntryChars;

	/// <summary>Releases the reference exactly once; never waits for the writer.</summary>
	public void Dispose()
	{
		Interlocked.Exchange(ref _sink, null)?.Release(_writer);
	}

	/// <summary>Queues one entry without blocking; a released reference drops it silently.</summary>
	/// <param name="entry">The entry.</param>
	/// <returns>Whether the entry was queued.</returns>
	internal bool TryWrite(in PluginLogEntry entry)
	{
		return Volatile.Read(ref _sink) is not null && _writer.TryEnqueue(entry);
	}
}
