using System.Globalization;
using System.Threading.Channels;

using Microsoft.Extensions.Logging;

namespace CheatEngine.Mcp.Plugin.Logging;

/// <summary>One generation of the plugin log: a bounded queue drained by one background writer.</summary>
/// <remarks>
///     Enqueuing never blocks: when the queue is full the entry is dropped and counted, and the writer logs the count.
///     The writer runs on the thread pool, waits for the previous generation to close the same file before it opens
///     it, flushes each batch to the operating system and, once completed and drained, flushes to the disk and closes
///     the file. It catches every exception, so <see cref="Completion" /> never faults.
/// </remarks>
internal sealed class PluginLogWriter
{
	private const int BatchSize = 256;

	private readonly PluginLogFileOptions _options;
	private readonly Channel<PluginLogEntry> _queue;
	private int _completed;
	private long _dropped;

	/// <summary>Starts a generation; its file opens only after <paramref name="previous" /> completed.</summary>
	/// <param name="path">The log file.</param>
	/// <param name="options">The file and queue bounds.</param>
	/// <param name="time">The clock of the entries this generation writes itself.</param>
	/// <param name="open">Opens the log file for append.</param>
	/// <param name="previous">The completion of the previous generation of the same sink.</param>
	internal PluginLogWriter(string path, PluginLogFileOptions options, TimeProvider time, Func<string, Stream> open,
		Task previous)
	{
		ArgumentNullException.ThrowIfNull(previous);
		_options = options;
		Time = time;
		// The background task owns the file from here on and closes it once the queue is drained.
		PluginLogFile file = new(path, options, time, open);
		FilePath = file.FilePath;
		_queue = Channel.CreateBounded<PluginLogEntry>(new BoundedChannelOptions(options.QueueCapacity)
		{
			// Wait makes TryWrite report a full queue instead of silently dropping, so drops can be counted.
			FullMode = BoundedChannelFullMode.Wait,
			SingleReader = true,
			SingleWriter = false,
			// Never run the writer inline on the logging thread, which can be Cheat Engine's main thread.
			AllowSynchronousContinuations = false
		});
		Completion = Task.Run(() => RunAsync(previous, file));
	}

	/// <summary>The log file of this generation.</summary>
	internal string FilePath
	{
		get;
	}

	/// <summary>The clock of entry timestamps.</summary>
	internal TimeProvider Time
	{
		get;
	}

	/// <summary>The bound of one entry.</summary>
	internal int MaxEntryChars => _options.MaxEntryChars;

	/// <summary>Completes once the queue was completed, drained and the file closed; never faults.</summary>
	internal Task Completion
	{
		get;
	}

	/// <summary>Queues one entry without blocking.</summary>
	/// <param name="entry">The entry.</param>
	/// <returns>Whether the entry was queued; a full queue counts it as dropped.</returns>
	internal bool TryEnqueue(in PluginLogEntry entry)
	{
		if (_queue.Writer.TryWrite(entry))
		{
			return true;
		}

		if (Volatile.Read(ref _completed) == 0)
		{
			Interlocked.Increment(ref _dropped);
		}

		return false;
	}

	/// <summary>Stops accepting entries; the writer drains the queue and closes the file. Never waits.</summary>
	internal void Complete()
	{
		Volatile.Write(ref _completed, 1);
		_queue.Writer.TryComplete();
	}

	private async Task RunAsync(Task previous, PluginLogFile file)
	{
		try
		{
			// The previous generation appends to the same file: never write while it is still draining.
			await previous.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
			ChannelReader<PluginLogEntry> reader = _queue.Reader;
			while (await reader.WaitToReadAsync().ConfigureAwait(false))
			{
				for (int count = 0; count < BatchSize && reader.TryRead(out PluginLogEntry entry); count++)
				{
					Write(file, entry);
				}

				ReportDropped(file);
				file.Flush();
			}

			ReportDropped(file);
		}
		catch (Exception)
		{
			// A logger must never fault its owner; entries still queued are lost with this generation.
		}
		finally
		{
			file.Dispose();
		}
	}

	private void Write(PluginLogFile file, in PluginLogEntry entry)
	{
		string line;
		try
		{
			line = PluginLogFormat.Line(entry, _options.MaxEntryChars);
		}
		catch (Exception exception)
		{
			// An exception whose ToString throws still leaves a trace of the entry.
			line = PluginLogFormat.Line(entry with
			{
				Exception = null,
				Message = string.Concat(entry.Message, " (the exception could not be formatted: ",
					exception.GetType().FullName, ")")
			}, _options.MaxEntryChars);
		}

		file.Write(line);
	}

	private void ReportDropped(PluginLogFile file)
	{
		long dropped = Interlocked.Exchange(ref _dropped, 0);
		if (dropped > 0)
		{
			string message = string.Create(CultureInfo.InvariantCulture,
				$"{dropped} log entries were dropped because the log writer fell behind.");
			file.Write(PluginLogFormat.Line(
				new PluginLogEntry(Time.GetLocalNow(), LogLevel.Warning, typeof(PluginLogWriter).FullName!, message,
					null),
				_options.MaxEntryChars));
		}
	}
}
