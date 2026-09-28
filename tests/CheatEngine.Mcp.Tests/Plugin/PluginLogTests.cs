using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

using CheatEngine.Mcp.Plugin;
using CheatEngine.Mcp.Plugin.Logging;
using CheatEngine.Mcp.Tests.Support;

using Microsoft.Extensions.Logging;

namespace CheatEngine.Mcp.Tests.Plugin;

/// <summary>The plugin-owned rolling file log: layout, levels, bounds, generations and failure handling.</summary>
public sealed partial class PluginLogTests : IDisposable
{
	private const string Timestamp = @"\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d{4}";
	private static readonly TimeSpan DrainTimeout = TimeSpan.FromSeconds(15);

	private readonly string _directory =
		Path.Combine(Path.GetTempPath(), $"CheatEngine.Mcp.Tests-{Guid.NewGuid():N}");

	public void Dispose()
	{
		if (Directory.Exists(_directory))
		{
			Directory.Delete(_directory, true);
		}
	}

	[Fact]
	public async Task Log_InformationEntry_WritesTheDocumentedLayout()
	{
		PluginLog log = new(_directory);
		InvalidOperationException failure = new("boom");
		using (ILoggerProvider provider = log.CreateProvider())
		{
			ILogger logger = provider.CreateLogger("CheatEngine.Mcp.Tests.Layout");
			Write(logger, LogLevel.Information, "Listening at http://127.0.0.1:1234.");
			WriteFailure(logger, LogLevel.Error, failure, "Call memory_scan failed.");
		}

		await TestLog.ReleaseAsync(log);

		Assert.Equal(Path.Combine(_directory, $"CheatEngine.Mcp.{Environment.ProcessId}.log"), log.LogFilePath);
		byte[] bytes = await File.ReadAllBytesAsync(log.LogFilePath, TestContext.Current.CancellationToken);
		Assert.False(bytes is [0xEF, 0xBB, 0xBF, ..], "The log is UTF-8 without a byte order mark.");
		string[] lines = Encoding.UTF8.GetString(bytes).Split("\r\n");
		Assert.Equal(3, lines.Length);
		Assert.Equal(string.Empty, lines[2]);
		Assert.Matches(
			$@"^{Timestamp}\|INFO\|CheatEngine\.Mcp\.Tests\.Layout\|Listening at http://127\.0\.0\.1:1234\.$",
			lines[0]);
		Assert.Matches(
			$@"^{Timestamp}\|ERROR\|CheatEngine\.Mcp\.Tests\.Layout\|Call memory_scan failed\. System\.InvalidOperationException: boom$",
			lines[1]);
		// Local time, like the former NLog longdate.
		DateTime written =
			DateTime.ParseExact(lines[0][..24], "yyyy-MM-dd HH:mm:ss.ffff", CultureInfo.InvariantCulture);
		Assert.InRange(written, DateTime.Now.AddMinutes(-5), DateTime.Now.AddMinutes(1));
	}

	[Fact]
	public async Task Log_BelowMinimumLevel_IsNotWritten()
	{
		PluginLogSink sink = new();
		PluginLog log = new(sink, _directory, LogLevel.Warning);
		using (ILoggerProvider provider = log.CreateProvider())
		{
			ILogger logger = provider.CreateLogger("CheatEngine.Mcp.Tests.Level");
			Assert.False(logger.IsEnabled(LogLevel.Information));
			Assert.True(logger.IsEnabled(LogLevel.Warning));
			Assert.False(logger.IsEnabled(LogLevel.None));
			Write(logger, LogLevel.Information, "hidden");
			Write(logger, LogLevel.Warning, "shown");
			Write(logger, LogLevel.None, "never");
		}

		sink.Dispose();
		await TestLog.ReleaseAsync(log);

		string line = Assert.Single(ReadLines(log.LogFilePath));
		Assert.EndsWith("|WARN|CheatEngine.Mcp.Tests.Level|shown", line, StringComparison.Ordinal);
	}

	[Theory]
	[InlineData("Microsoft.AspNetCore")]
	[InlineData("Microsoft.AspNetCore.Server.Kestrel.Connections")]
	[InlineData("System.Net.Http.HttpClient.Default.LogicalHandler")]
	[InlineData("ModelContextProtocol.Server.McpServer")]
	public void MinimumLevel_TransportCategory_IsNeverBelowInformation(string category)
	{
		Assert.Equal(LogLevel.Information, PluginLoggerProvider.MinimumLevelFor(category, LogLevel.Trace));
		Assert.Equal(LogLevel.Information, PluginLoggerProvider.MinimumLevelFor(category, LogLevel.Debug));
		Assert.Equal(LogLevel.Warning, PluginLoggerProvider.MinimumLevelFor(category, LogLevel.Warning));
		Assert.Equal(LogLevel.None, PluginLoggerProvider.MinimumLevelFor(category, LogLevel.None));
	}

	[Theory]
	[InlineData("CheatEngine.Mcp.Hosting.Backend.McpBackendHost")]
	[InlineData("Microsoft.Extensions.Hosting.Internal.Host")]
	[InlineData("ModelContextProtocolExtensions")]
	[InlineData("System.Net.HttpListener")]
	public void MinimumLevel_OtherCategory_KeepsTheConfiguredLevel(string category)
	{
		Assert.Equal(LogLevel.Trace, PluginLoggerProvider.MinimumLevelFor(category, LogLevel.Trace));
	}

	[Fact]
	public async Task Log_TraceLevel_WritesDebugEntriesButFloorsTransportCategories()
	{
		PluginLogSink sink = new();
		PluginLog log = new(sink, _directory, LogLevel.Trace);
		using (ILoggerProvider provider = log.CreateProvider())
		{
			ILogger own = provider.CreateLogger("CheatEngine.Mcp.Tests.Trace");
			Write(own, LogLevel.Trace, "own trace");
			ILogger transport = provider.CreateLogger("ModelContextProtocol.Server.McpServer");
			Assert.False(transport.IsEnabled(LogLevel.Debug));
			Write(transport, LogLevel.Debug, "Bearer secret-token");
			Write(transport, LogLevel.Information, "transport information");
		}

		sink.Dispose();
		await TestLog.ReleaseAsync(log);

		string[] lines = ReadLines(log.LogFilePath);
		Assert.Equal(2, lines.Length);
		Assert.EndsWith("|TRACE|CheatEngine.Mcp.Tests.Trace|own trace", lines[0], StringComparison.Ordinal);
		Assert.EndsWith("|INFO|ModelContextProtocol.Server.McpServer|transport information", lines[1],
			StringComparison.Ordinal);
	}

	[Fact]
	public async Task Log_ManyThreads_KeepsEveryLineWhole()
	{
		const int threads = 8;
		const int entries = 500;
		PluginLog log = new(_directory);
		using (ILoggerProvider provider = log.CreateProvider())
		{
			ILogger logger = provider.CreateLogger("CheatEngine.Mcp.Tests.Threads");
			string padding = new('x', 200);
			Parallel.For(0, threads, thread =>
			{
				for (int index = 0; index < entries; index++)
				{
					LogThreadEntry(logger, thread, index, padding);
				}
			});
		}

		await TestLog.ReleaseAsync(log);

		string[] lines = ReadLines(log.LogFilePath);
		Assert.Equal(threads * entries, lines.Length);
		Regex whole = WholeThreadLine();
		HashSet<(int Thread, int Index)> seen = [];
		foreach (string line in lines)
		{
			Match match = whole.Match(line);
			Assert.True(match.Success, $"Torn or malformed line: {line}");
			Assert.True(seen.Add((int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture),
					int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture))), $"Duplicated line: {line}");
		}
	}

	[Fact]
	public async Task Log_WhileFileWriteIsBlocked_ReturnsImmediatelyAndReportsDrops()
	{
		using ManualResetEventSlim gate = new(false);
		using ManualResetEventSlim entered = new(false);
		PluginLogSink sink = new(Options(queueCapacity: 16), TimeProvider.System,
			path => new GatedStream(PluginLogFile.OpenAppend(path), gate, entered));
		PluginLog log = new(sink, _directory, LogLevel.Information);
		ILoggerProvider provider = log.CreateProvider();
		ILogger logger = provider.CreateLogger("CheatEngine.Mcp.Tests.Blocked");
		LogEntry(logger, 0);
		Assert.True(entered.Wait(DrainTimeout, TestContext.Current.CancellationToken),
			"The writer never started writing.");

		Stopwatch elapsed = Stopwatch.StartNew();
		for (int index = 1; index <= 10_000; index++)
		{
			LogEntry(logger, index);
		}

		// Releasing every reference while the writer is stuck in a write must not wait for it either.
		provider.Dispose();
		log.Dispose();
		sink.Dispose();
		elapsed.Stop();
		Assert.True(elapsed.Elapsed < TimeSpan.FromSeconds(1),
			$"Logging and releasing took {elapsed.Elapsed} while the file write was blocked.");
		Assert.Equal(0, sink.References);
		Assert.False(sink.Drained.IsCompleted, "The writer cannot have drained while its write is blocked.");

		gate.Set();
		await sink.Drained.WaitAsync(DrainTimeout, TestContext.Current.CancellationToken);

		string[] lines = ReadLines(log.LogFilePath);
		long dropped = lines.Select(static line => DroppedNotice().Match(line))
			.Where(static match => match.Success)
			.Sum(static match => long.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture));
		int written = lines.Count(static line => line.Contains("|CheatEngine.Mcp.Tests.Blocked|entry ",
			StringComparison.Ordinal));
		Assert.True(dropped > 0, "A full queue must drop and count entries.");
		Assert.Equal(10_001, written + dropped);
	}

	[Fact]
	public async Task Roll_AboveMaximumSize_KeepsFiveNewestArchives()
	{
		PluginLogSink sink = new(Options(1024), TimeProvider.System, PluginLogFile.OpenAppend);
		PluginLog log = new(sink, _directory, LogLevel.Information);
		using (ILoggerProvider provider = log.CreateProvider())
		{
			ILogger logger = provider.CreateLogger("CheatEngine.Mcp.Tests.Roll");
			string padding = new('x', 40);
			for (int index = 0; index < 200; index++)
			{
				LogPaddedEntry(logger, index, padding);
			}
		}

		sink.Dispose();
		await TestLog.ReleaseAsync(log);

		string[] files = [log.LogFilePath, .. Enumerable.Range(1, 5).Select(Archive)];
		Assert.All(files, file => Assert.InRange(new FileInfo(file).Length, 1L, 1024L));
		Assert.False(File.Exists(Archive(6)), "Only five archives are kept.");
		int[][] indices = files.Select(static file => ReadLines(file).Select(EntryIndex).ToArray()).ToArray();
		Assert.Equal(199, indices[0][^1]);
		for (int file = 0; file < indices.Length; file++)
		{
			Assert.Equal(Enumerable.Range(indices[file][0], indices[file].Length), indices[file]);
			if (file > 0)
			{
				// Each archive holds the entries that directly precede the next newer file's.
				Assert.Equal(indices[file - 1][0] - 1, indices[file][^1]);
			}
		}
	}

	[Fact]
	public void Roll_WhileAViewerDeniesDelete_KeepsAppendingAndRetriesAfterFiveSeconds()
	{
		ManualTimeProvider time = new();
		string path = PluginLogSink.FilePathIn(_directory);
		using PluginLogFile file = new(path, Options(1024), time, PluginLogFile.OpenAppend);
		Assert.True(file.Write(Line(time, new string('a', 600))));
		using (FileStream viewer = new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
		{
			Assert.True(file.Write(Line(time, new string('b', 600))));
			Assert.True(file.Write(Line(time, new string('c', 600))));
			file.Flush();
			Assert.False(File.Exists(file.ArchivePath(1)), "A file a viewer holds without delete sharing cannot move.");
			Assert.True(viewer.Length > 1024, "The log keeps appending while the roll waits for its retry.");
		}

		// The viewer is gone, but the failed roll is only retried after the delay.
		Assert.True(file.Write(Line(time, new string('d', 600))));
		Assert.False(File.Exists(file.ArchivePath(1)));
		time.Advance(PluginLogFile.RetryDelay);
		Assert.True(file.Write(Line(time, new string('e', 600))));
		file.Dispose();

		Assert.Equal(4, ReadLines(file.ArchivePath(1)).Length);
		Assert.EndsWith(new string('e', 600), Assert.Single(ReadLines(path)), StringComparison.Ordinal);
	}

	[Fact]
	public async Task Release_LastReference_DrainsQueuedEntries()
	{
		PluginLog log = new(_directory);
		ILoggerProvider provider = log.CreateProvider();
		ILogger logger = provider.CreateLogger("CheatEngine.Mcp.Tests.Drain");
		for (int index = 0; index < 100; index++)
		{
			LogEntry(logger, index);
		}

		provider.Dispose();
		Assert.Equal(1, log.Sink.References);
		Assert.False(log.Sink.Drained.IsCompleted, "The log's own reference keeps the generation open.");
		log.Dispose();
		await log.Sink.Drained.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

		Assert.Equal(Enumerable.Range(0, 100), ReadLines(log.LogFilePath).Select(EntryIndex));
	}

	[Fact]
	public async Task Acquire_AfterAllReleased_ReopensAndAppendsInOrder()
	{
		PluginLogSink sink = new();
		PluginLog first = new(sink, _directory, LogLevel.Information);
		using (ILoggerProvider provider = first.CreateProvider())
		{
			ILogger logger = provider.CreateLogger("CheatEngine.Mcp.Tests.Generation");
			Write(logger, LogLevel.Information, "first");
		}

		first.Dispose();
		Task firstGeneration = sink.Drained;
		PluginLog second = new(sink, _directory, LogLevel.Information);
		Assert.NotSame(firstGeneration, sink.Drained);
		using (ILoggerProvider provider = second.CreateProvider())
		{
			ILogger logger = provider.CreateLogger("CheatEngine.Mcp.Tests.Generation");
			Write(logger, LogLevel.Information, "second");
		}

		second.Dispose();
		sink.Dispose();
		await sink.Drained.WaitAsync(DrainTimeout, TestContext.Current.CancellationToken);

		Assert.True(firstGeneration.IsCompletedSuccessfully);
		Assert.Equal(first.LogFilePath, second.LogFilePath);
		string[] lines = ReadLines(second.LogFilePath);
		Assert.Equal(2, lines.Length);
		Assert.EndsWith("|first", lines[0], StringComparison.Ordinal);
		Assert.EndsWith("|second", lines[1], StringComparison.Ordinal);
	}

	[Fact]
	public async Task Acquire_WhilePreviousGenerationDrains_NeverInterleaves()
	{
		using ManualResetEventSlim gate = new(false);
		using ManualResetEventSlim entered = new(false);
		int opened = 0;
		int open = 0;
		int mostOpen = 0;

		Stream Open(string path)
		{
			bool first = Interlocked.Increment(ref opened) == 1;
			int current = Interlocked.Increment(ref open);
			InterlockedMax(ref mostOpen, current);
			return new GatedStream(PluginLogFile.OpenAppend(path), first ? gate : null, entered,
				() => Interlocked.Decrement(ref open));
		}

		PluginLogSink sink = new(Options(), TimeProvider.System, Open);
		PluginLog first = new(sink, _directory, LogLevel.Information);
		ILoggerProvider firstProvider = first.CreateProvider();
		ILogger firstLogger = firstProvider.CreateLogger("CheatEngine.Mcp.Tests.First");
		for (int index = 0; index < 50; index++)
		{
			LogEntry(firstLogger, index);
		}

		Assert.True(entered.Wait(DrainTimeout, TestContext.Current.CancellationToken),
			"The first generation never wrote.");
		// The first generation is completed but still draining: its write is blocked.
		firstProvider.Dispose();
		first.Dispose();
		PluginLog second = new(sink, _directory, LogLevel.Information);
		using (ILoggerProvider provider = second.CreateProvider())
		{
			ILogger logger = provider.CreateLogger("CheatEngine.Mcp.Tests.Second");
			for (int index = 0; index < 50; index++)
			{
				LogEntry(logger, index);
			}
		}

		second.Dispose();
		sink.Dispose();
		await Task.Delay(TimeSpan.FromMilliseconds(250), TestContext.Current.CancellationToken);
		Assert.Equal(1, Volatile.Read(ref opened));

		gate.Set();
		await sink.Drained.WaitAsync(DrainTimeout, TestContext.Current.CancellationToken);

		Assert.Equal(2, opened);
		Assert.Equal(1, mostOpen);
		string[] lines = ReadLines(second.LogFilePath);
		Assert.Equal(100, lines.Length);
		Assert.All(lines[..50], static line => Assert.Contains("|CheatEngine.Mcp.Tests.First|", line));
		Assert.All(lines[50..], static line => Assert.Contains("|CheatEngine.Mcp.Tests.Second|", line));
		Assert.Equal(Enumerable.Range(0, 50).Concat(Enumerable.Range(0, 50)), lines.Select(EntryIndex));
	}

	[Fact]
	public async Task Open_DirectoryIsAFile_DropsWithoutThrowing()
	{
		Directory.CreateDirectory(_directory);
		string notADirectory = Path.Combine(_directory, "not-a-directory");
		await File.WriteAllTextAsync(notADirectory, "x", TestContext.Current.CancellationToken);
		PluginLog log = new(notADirectory);
		using (ILoggerProvider provider = log.CreateProvider())
		{
			ILogger logger = provider.CreateLogger("CheatEngine.Mcp.Tests.Unavailable");
			for (int index = 0; index < 3; index++)
			{
				Write(logger, LogLevel.Warning, "dropped");
			}
		}

		await TestLog.ReleaseAsync(log);

		Assert.True(log.Sink.Drained.IsCompletedSuccessfully);
		Assert.Equal("x", await File.ReadAllTextAsync(notADirectory, TestContext.Current.CancellationToken));
	}

	[Fact]
	public void Open_TransientFailure_RetriesAfterFiveSecondsAndReportsDrops()
	{
		ManualTimeProvider time = new();
		int attempts = 0;
		string path = PluginLogSink.FilePathIn(_directory);

		using (PluginLogFile file = new(path, Options(), time, Open))
		{
			Assert.False(file.Write(Line(time, "a")));
			Assert.False(file.Write(Line(time, "b")));
			Assert.Equal(1, attempts);
			time.Advance(PluginLogFile.RetryDelay);
			Assert.True(file.Write(Line(time, "c")));
			Assert.Equal(2, attempts);
		}

		string[] lines = ReadLines(path);
		Assert.Equal(2, lines.Length);
		Assert.Matches(
			$@"^{Timestamp}\|WARN\|CheatEngine\.Mcp\.Plugin\.Logging\.PluginLogFile\|2 log entries were dropped while the log file was unavailable\.$",
			lines[0]);
		Assert.EndsWith("|c", lines[1], StringComparison.Ordinal);

		Stream Open(string file)
		{
			return ++attempts == 1 ? throw new IOException("The file is locked.") : PluginLogFile.OpenAppend(file);
		}
	}

	[Fact]
	public async Task Log_LongEntry_IsTruncatedAtItsBound()
	{
		PluginLogSink sink = new(Options(maxEntryChars: 300), TimeProvider.System, PluginLogFile.OpenAppend);
		PluginLog log = new(sink, _directory, LogLevel.Information);
		string payload = new('x', 10_000);
		InvalidOperationException failure = new(new string('y', 10_000));
		using (ILoggerProvider provider = log.CreateProvider())
		{
			ILogger logger = provider.CreateLogger("CheatEngine.Mcp.Tests.Bound");
			Write(logger, LogLevel.Information, payload);
			WriteFailure(logger, LogLevel.Error, failure, "short");
		}

		sink.Dispose();
		await TestLog.ReleaseAsync(log);

		string[] lines = ReadLines(log.LogFilePath);
		Assert.Equal(2, lines.Length);
		Assert.All(lines, static line =>
		{
			Assert.Equal(300, line.Length);
			Assert.EndsWith(PluginLogFormat.TruncationMarker, line, StringComparison.Ordinal);
		});
	}

	[Fact]
	public async Task Dispose_Sink_ThenCreateProvider_ThrowsObjectDisposed()
	{
		PluginLogSink disposed = new();
		disposed.Dispose();
		Assert.Throws<ObjectDisposedException>(() => new PluginLog(disposed, _directory, LogLevel.Information));

		PluginLogSink sink = new();
		PluginLog log = new(sink, _directory, LogLevel.Information);
		sink.Dispose();
		// A disposed sink refuses new references, while the log's existing reference stays valid until released.
		Assert.Throws<ObjectDisposedException>(() => log.CreateProvider());
		Assert.Equal(1, sink.References);
		await TestLog.ReleaseAsync(log);
		Assert.Equal(0, sink.References);
		Assert.Throws<ObjectDisposedException>(() => log.CreateProvider());
	}

	private static PluginLogFileOptions Options(long maxFileBytes = 10 * 1024 * 1024, int queueCapacity = 8192,
		int maxEntryChars = 32 * 1024)
	{
		return new PluginLogFileOptions(maxFileBytes, 5, queueCapacity, maxEntryChars);
	}

	private static string Line(TimeProvider time, string message)
	{
		return PluginLogFormat.Line(
			new PluginLogEntry(time.GetLocalNow(), LogLevel.Information, "CheatEngine.Mcp.Tests.File", message, null),
			32 * 1024);
	}

	private static string[] ReadLines(string path)
	{
		string text = File.ReadAllText(path, Encoding.UTF8);
		Assert.EndsWith("\r\n", text, StringComparison.Ordinal);
		return text[..^2].Split("\r\n");
	}

	private static int EntryIndex(string line)
	{
		Match match = EntryIndexPattern().Match(line);
		Assert.True(match.Success, $"Not an entry line: {line}");
		return int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
	}

	private static void InterlockedMax(ref int target, int value)
	{
		int current = Volatile.Read(ref target);
		while (value > current)
		{
			int observed = Interlocked.CompareExchange(ref target, value, current);
			if (observed == current)
			{
				return;
			}

			current = observed;
		}
	}

	private string Archive(int index)
	{
		return Path.Combine(_directory, $"CheatEngine.Mcp.{Environment.ProcessId}.{index}.log");
	}

	[LoggerMessage(Message = "{Text}")]
	private static partial void Write(ILogger logger, LogLevel level, string text);

	[LoggerMessage(Message = "{Text}")]
	private static partial void WriteFailure(ILogger logger, LogLevel level, Exception exception, string text);

	[LoggerMessage(Level = LogLevel.Information, Message = "entry {Index}")]
	private static partial void LogEntry(ILogger logger, int index);

	[LoggerMessage(Level = LogLevel.Information, Message = "entry {Index} {Padding}")]
	private static partial void LogPaddedEntry(ILogger logger, int index, string padding);

	[LoggerMessage(Level = LogLevel.Information, Message = "thread {Thread} entry {Index} {Padding}")]
	private static partial void LogThreadEntry(ILogger logger, int thread, int index, string padding);

	[GeneratedRegex(
		@"^\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d{4}\|INFO\|CheatEngine\.Mcp\.Tests\.Threads\|thread (\d) entry (\d+) x{200}$")]
	private static partial Regex WholeThreadLine();

	[GeneratedRegex(
		@"\|WARN\|CheatEngine\.Mcp\.Plugin\.Logging\.PluginLogWriter\|(\d+) log entries were dropped because the log writer fell behind\.$")]
	private static partial Regex DroppedNotice();

	[GeneratedRegex(@"\|entry (\d+)(?: |$)")]
	private static partial Regex EntryIndexPattern();

	/// <summary>A clock that moves only when the test advances it.</summary>
	private sealed class ManualTimeProvider : TimeProvider
	{
		private long _timestamp;
		private DateTimeOffset _utcNow = new(2026, 9, 27, 12, 0, 0, TimeSpan.Zero);

		public override long TimestampFrequency => TimeSpan.TicksPerSecond;

		public override DateTimeOffset GetUtcNow()
		{
			return _utcNow;
		}

		public override long GetTimestamp()
		{
			return _timestamp;
		}

		public void Advance(TimeSpan delay)
		{
			_timestamp += delay.Ticks;
			_utcNow += delay;
		}
	}

	/// <summary>A file stream whose writes can be held at a gate, reporting the first write and its disposal.</summary>
	private sealed class GatedStream(
		Stream inner,
		ManualResetEventSlim? gate,
		ManualResetEventSlim entered,
		Action? disposed = null) : Stream
	{
		private int _disposed;

		public override bool CanRead => false;
		public override bool CanSeek => inner.CanSeek;
		public override bool CanWrite => true;
		public override long Length => inner.Length;

		public override long Position
		{
			get => inner.Position;
			set => throw new NotSupportedException();
		}

		public override void Write(byte[] buffer, int offset, int count)
		{
			entered.Set();
			gate?.Wait();
			inner.Write(buffer, offset, count);
		}

		public override void Flush()
		{
			inner.Flush();
		}

		public override int Read(byte[] buffer, int offset, int count)
		{
			throw new NotSupportedException();
		}

		public override long Seek(long offset, SeekOrigin origin)
		{
			throw new NotSupportedException();
		}

		public override void SetLength(long value)
		{
			throw new NotSupportedException();
		}

		protected override void Dispose(bool disposing)
		{
			if (disposing && Interlocked.Exchange(ref _disposed, 1) == 0)
			{
				inner.Dispose();
				disposed?.Invoke();
			}

			base.Dispose(disposing);
		}
	}
}
