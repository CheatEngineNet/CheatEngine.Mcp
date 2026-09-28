using System.Globalization;
using System.Text;

using Microsoft.Extensions.Logging;

namespace CheatEngine.Mcp.Plugin.Logging;

/// <summary>Appends lines to the plugin log file, rolls it at its size bound and survives I/O failures.</summary>
/// <remarks>
///     One writer generation uses an instance from one thread at a time. The default stream appends with read, write
///     and delete sharing, so log viewers and cleanup tools never block Cheat Engine. An I/O failure closes the file and
///     drops entries: the file is reopened at most every <see cref="RetryDelay" />, and the number of dropped entries is
///     written once it is writable again. A roll that fails keeps appending to the current file and is retried after the
///     same delay. Rolling moves the current file to <c>.1</c>, and <c>.1</c> to <c>.2</c> up to the archive bound,
///     whose oldest file is deleted.
/// </remarks>
internal sealed class PluginLogFile : IDisposable
{
	/// <summary>The minimum interval between two attempts to reopen or roll the file after a failure.</summary>
	internal static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(5);

	private readonly string _archiveExtension;
	private readonly string _archivePrefix;
	private readonly Func<string, Stream> _open;
	private readonly PluginLogFileOptions _options;
	private readonly TimeProvider _time;
	private byte[] _buffer = new byte[4096];
	private long _dropped;
	private long _length;
	private long? _openFailedAt;
	private long? _rollFailedAt;
	private Stream? _stream;

	/// <summary>Prepares the file; nothing is opened before the first line.</summary>
	/// <param name="path">The current log file.</param>
	/// <param name="options">The size and archive bounds.</param>
	/// <param name="time">The clock of the retry delay and of the dropped-entries notice.</param>
	/// <param name="open">Opens the current file for append.</param>
	internal PluginLogFile(string path, PluginLogFileOptions options, TimeProvider time, Func<string, Stream> open)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(path);
		ArgumentNullException.ThrowIfNull(options);
		ArgumentNullException.ThrowIfNull(time);
		ArgumentNullException.ThrowIfNull(open);
		FilePath = path;
		_options = options;
		_time = time;
		_open = open;
		_archivePrefix = Path.Combine(Path.GetDirectoryName(path) ?? string.Empty,
			Path.GetFileNameWithoutExtension(path));
		_archiveExtension = Path.GetExtension(path);
	}

	/// <summary>The current log file.</summary>
	internal string FilePath
	{
		get;
	}

	/// <summary>Flushes to the disk and closes the file; never throws.</summary>
	public void Dispose()
	{
		Close(true);
	}

	/// <summary>Opens the log file for append, creating its folder; the sink's default.</summary>
	/// <param name="path">The current log file.</param>
	/// <returns>The append stream.</returns>
	internal static Stream OpenAppend(string path)
	{
		string? directory = Path.GetDirectoryName(path);
		if (!string.IsNullOrEmpty(directory))
		{
			Directory.CreateDirectory(directory);
		}

		return new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete, 65536);
	}

	/// <summary>The path of one archive, <c>.1</c> being the newest.</summary>
	/// <param name="index">The archive index.</param>
	/// <returns>The archive path.</returns>
	internal string ArchivePath(int index)
	{
		return string.Create(CultureInfo.InvariantCulture, $"{_archivePrefix}.{index}{_archiveExtension}");
	}

	/// <summary>Appends one formatted line, or counts it as dropped while the file is unavailable.</summary>
	/// <param name="line">The line, line end included.</param>
	/// <returns>Whether the line reached the stream.</returns>
	internal bool Write(string line)
	{
		if (EnsureOpen() && Append(line))
		{
			return true;
		}

		_dropped++;
		return false;
	}

	/// <summary>Hands the buffered lines to the operating system.</summary>
	internal void Flush()
	{
		if (_stream is null)
		{
			return;
		}

		try
		{
			_stream.Flush();
		}
		catch (Exception)
		{
			Fail();
		}
	}

	private bool EnsureOpen()
	{
		if (_stream is not null)
		{
			return true;
		}

		if ((_openFailedAt is long failedAt && _time.GetElapsedTime(failedAt) < RetryDelay) || !TryOpen())
		{
			return false;
		}

		if (_dropped > 0 && Append(DroppedNotice()))
		{
			_dropped = 0;
		}

		return _stream is not null;
	}

	private string DroppedNotice()
	{
		string message = string.Create(CultureInfo.InvariantCulture,
			$"{_dropped} log entries were dropped while the log file was unavailable.");
		return PluginLogFormat.Line(
			new PluginLogEntry(_time.GetLocalNow(), LogLevel.Warning, typeof(PluginLogFile).FullName!, message, null),
			_options.MaxEntryChars);
	}

	private bool TryOpen()
	{
		try
		{
			_stream = _open(FilePath);
			_length = _stream.CanSeek ? _stream.Length : 0;
			_openFailedAt = null;
			return true;
		}
		catch (Exception)
		{
			Fail();
			return false;
		}
	}

	private bool Append(string line)
	{
		int count = Encoding.UTF8.GetByteCount(line);
		if (_length > 0 && _length + count > _options.MaxFileBytes && TryRoll() && _stream is null)
		{
			return false;
		}

		try
		{
			if (_buffer.Length < count)
			{
				_buffer = new byte[Math.Max(count, _buffer.Length * 2)];
			}

			int written = Encoding.UTF8.GetBytes(line, _buffer);
			_stream!.Write(_buffer, 0, written);
			_length += written;
			return true;
		}
		catch (Exception)
		{
			Fail();
			return false;
		}
	}

	/// <summary>Rolls the current file unless a failed roll is still waiting for its retry delay.</summary>
	/// <returns>Whether the file was closed for a roll attempt; it is then reopened, or failed.</returns>
	private bool TryRoll()
	{
		if (_rollFailedAt is long failedAt && _time.GetElapsedTime(failedAt) < RetryDelay)
		{
			return false;
		}

		Close(false);
		try
		{
			File.Delete(ArchivePath(_options.MaxArchives));
			for (int index = _options.MaxArchives - 1; index >= 1; index--)
			{
				string archive = ArchivePath(index);
				if (File.Exists(archive))
				{
					File.Move(archive, ArchivePath(index + 1), true);
				}
			}

			File.Move(FilePath, ArchivePath(1), true);
			_rollFailedAt = null;
		}
		catch (Exception)
		{
			// Keep appending to the current file; a viewer that denies delete sharing can hold it for a while.
			_rollFailedAt = _time.GetTimestamp();
		}

		TryOpen();
		return true;
	}

	private void Fail()
	{
		Close(false);
		_openFailedAt = _time.GetTimestamp();
	}

	private void Close(bool durable)
	{
		Stream? stream = _stream;
		_stream = null;
		if (stream is null)
		{
			return;
		}

		try
		{
			if (durable && stream is FileStream file)
			{
				file.Flush(true);
			}
			else
			{
				stream.Flush();
			}
		}
		catch (Exception)
		{
			// The entries still buffered are lost with the file handle; the writer must go on.
		}
		finally
		{
			try
			{
				stream.Dispose();
			}
			catch (Exception)
			{
				// A failed close leaves nothing to recover.
			}
		}
	}
}
