using System.Buffers.Binary;
using System.Text;

namespace CheatEngine.Mcp.Tests.LiveQualification.Infrastructure;

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
	private readonly List<DebugOutputMessage> _messages = [];
	private readonly HashSet<int> _tracked = [];
	private int _dropped;
	private int _ignored;

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
