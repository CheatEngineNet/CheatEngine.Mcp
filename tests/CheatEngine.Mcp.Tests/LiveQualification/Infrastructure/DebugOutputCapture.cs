// Adapted from CheatEngine.Client; see NOTICE.md and licenses/CheatEngine.Client.LICENSE.
using System.Buffers.Binary;
using System.IO.MemoryMappedFiles;
using System.Runtime.Versioning;
using System.Text;

namespace CheatEngine.Mcp.Tests.LiveQualification;

/// <summary>One <c>OutputDebugString</c> message.</summary>
/// <param name="ProcessId">The process that wrote it.</param>
/// <param name="Text">The decoded text, without its trailing line break.</param>
internal readonly record struct DebugOutputMessage(int ProcessId, string Text);

/// <summary>
///     Decodes DBWIN buffers and keeps only the messages of the tracked processes (the sandboxed Cheat Engine), so the
///     debug output of any other process on the workstation is counted but never stored. A DBWIN buffer is 4096 bytes:
///     the writer's process id (little-endian DWORD), then an ANSI string terminated by NUL.
/// </summary>
internal sealed class DebugOutputBuffer
{
	/// <summary>The size of the DBWIN shared buffer.</summary>
	internal const int BufferSize = 4096;

	/// <summary>The most messages kept; later ones are counted as dropped.</summary>
	internal const int MaximumMessages = 100_000;

	private readonly Encoding _ansi;
	private readonly Lock _gate = new();
	private readonly HashSet<int> _tracked = [];
	private readonly List<DebugOutputMessage> _messages = [];
	private int _ignored;
	private int _dropped;

	/// <summary>Creates a buffer that decodes text with <paramref name="ansi" />, the writer's ANSI code page.</summary>
	internal DebugOutputBuffer(Encoding ansi)
	{
		ArgumentNullException.ThrowIfNull(ansi);
		_ansi = ansi;
	}

	/// <summary>The kept messages, in arrival order.</summary>
	internal IReadOnlyList<DebugOutputMessage> Messages
	{
		get
		{
			lock (_gate)
			{
				return [.. _messages];
			}
		}
	}

	/// <summary>How many messages of untracked processes were discarded.</summary>
	internal int Ignored
	{
		get
		{
			lock (_gate)
			{
				return _ignored;
			}
		}
	}

	/// <summary>How many messages of tracked processes exceeded <see cref="MaximumMessages" />.</summary>
	internal int Dropped
	{
		get
		{
			lock (_gate)
			{
				return _dropped;
			}
		}
	}

	/// <summary>The system ANSI code page, which <c>OutputDebugStringW</c> converts to; Latin-1 when unavailable.</summary>
	internal static Encoding SystemAnsiEncoding()
	{
		return CodePagesEncodingProvider.Instance.GetEncoding(0) ?? Encoding.Latin1;
	}

	/// <summary>Decodes one DBWIN buffer; <see langword="false" /> when it is too short to hold a process id.</summary>
	internal static bool TryDecode(ReadOnlySpan<byte> buffer, Encoding ansi, out DebugOutputMessage message)
	{
		ArgumentNullException.ThrowIfNull(ansi);
		message = default;
		if (buffer.Length < sizeof(int))
		{
			return false;
		}

		int processId = BinaryPrimitives.ReadInt32LittleEndian(buffer);
		ReadOnlySpan<byte> text = buffer[sizeof(int)..];
		int terminator = text.IndexOf((byte) 0);
		if (terminator >= 0)
		{
			text = text[..terminator];
		}

		message = new DebugOutputMessage(processId, ansi.GetString(text).TrimEnd('\r', '\n'));
		return true;
	}

	/// <summary>Keeps the messages of <paramref name="processId" /> from now on.</summary>
	internal void Track(int processId)
	{
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(processId);
		lock (_gate)
		{
			_tracked.Add(processId);
		}
	}

	/// <summary>Decodes one buffer and keeps it when its writer is tracked; returns whether it was kept.</summary>
	internal bool Accept(ReadOnlySpan<byte> buffer)
	{
		if (!TryDecode(buffer, _ansi, out DebugOutputMessage message))
		{
			return false;
		}

		lock (_gate)
		{
			if (!_tracked.Contains(message.ProcessId))
			{
				_ignored++;
				return false;
			}

			if (_messages.Count >= MaximumMessages)
			{
				_dropped++;
				return false;
			}

			_messages.Add(message);
			return true;
		}
	}
}

/// <summary>
///     A managed DBWIN listener, the protocol DebugView implements: it owns the <c>DBWIN_BUFFER</c> section (4096 bytes)
///     and the <c>DBWIN_BUFFER_READY</c> and <c>DBWIN_DATA_READY</c> events, signals ready, waits for data and hands every
///     buffer to a <see cref="DebugOutputBuffer" /> filtered on the Cheat Engine process id. It captures the SDK host log,
///     the identification line of <c>CHEATENGINE_SDK_IDENTIFY_ON_ENABLE</c> and the cleanup Cheat Engine runs at
///     <c>closeCE</c>. It refuses to start when another listener already owns the objects.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class DebugOutputCapture : IDisposable
{
	/// <summary>The shared section name.</summary>
	internal const string BufferName = "DBWIN_BUFFER";

	/// <summary>The event the listener signals when the section may be written.</summary>
	internal const string BufferReadyName = "DBWIN_BUFFER_READY";

	/// <summary>The event a writer signals when the section holds a message.</summary>
	internal const string DataReadyName = "DBWIN_DATA_READY";

	private readonly MemoryMappedFile _section;
	private readonly MemoryMappedViewAccessor _view;
	private readonly EventWaitHandle _bufferReady;
	private readonly EventWaitHandle _dataReady;
	private readonly ManualResetEvent _stop = new(false);
	private readonly Thread _listener;

	private DebugOutputCapture(MemoryMappedFile section, EventWaitHandle bufferReady, EventWaitHandle dataReady, Encoding ansi)
	{
		_section = section;
		_view = section.CreateViewAccessor(0, DebugOutputBuffer.BufferSize, MemoryMappedFileAccess.Read);
		_bufferReady = bufferReady;
		_dataReady = dataReady;
		Buffer = new DebugOutputBuffer(ansi);
		_listener = new Thread(Listen)
		{
			IsBackground = true,
			Name = "DBWIN listener"
		};
		_listener.Start();
	}

	/// <summary>The captured messages.</summary>
	internal DebugOutputBuffer Buffer
	{
		get;
	}

	/// <summary>Owns the DBWIN objects and starts listening.</summary>
	internal static DebugOutputCapture Start(Encoding ansi)
	{
		ArgumentNullException.ThrowIfNull(ansi);
		EventWaitHandle bufferReady = new(false, EventResetMode.AutoReset, BufferReadyName, out bool bufferReadyCreated);
		EventWaitHandle? dataReady = null;
		MemoryMappedFile? section = null;
		try
		{
			dataReady = new EventWaitHandle(false, EventResetMode.AutoReset, DataReadyName, out bool dataReadyCreated);
			if (!bufferReadyCreated || !dataReadyCreated)
			{
				throw new InvalidOperationException("Another debug output listener (for example DebugView) owns the DBWIN events; close it.");
			}

			section = MemoryMappedFile.CreateNew(BufferName, DebugOutputBuffer.BufferSize, MemoryMappedFileAccess.ReadWrite);
			return new DebugOutputCapture(section, bufferReady, dataReady, ansi);
		}
		catch
		{
			section?.Dispose();
			dataReady?.Dispose();
			bufferReady.Dispose();
			throw;
		}
	}

	/// <summary>Writes the messages of <paramref name="processId" />, one per line, to <paramref name="path" />.</summary>
	internal void WriteTo(string path, int processId)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(path);
		IEnumerable<string> lines = Buffer.Messages.Where(message => message.ProcessId == processId).Select(static message => message.Text);
		File.WriteAllLines(path, lines, new UTF8Encoding(false));
	}

	/// <inheritdoc />
	public void Dispose()
	{
		_stop.Set();
		_listener.Join(TimeSpan.FromSeconds(5));
		_view.Dispose();
		_section.Dispose();
		_dataReady.Dispose();
		_bufferReady.Dispose();
		_stop.Dispose();
	}

	private void Listen()
	{
		byte[] data = new byte[DebugOutputBuffer.BufferSize];
		WaitHandle[] handles = [_dataReady, _stop];
		while (true)
		{
			_bufferReady.Set();
			if (WaitHandle.WaitAny(handles) != 0)
			{
				return;
			}

			_view.ReadArray(0, data, 0, data.Length);
			Buffer.Accept(data);
		}
	}
}
