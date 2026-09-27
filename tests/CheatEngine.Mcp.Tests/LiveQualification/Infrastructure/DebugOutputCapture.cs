// Adapted from CheatEngine.Client; see NOTICE.md and licenses/CheatEngine.Client.LICENSE.

using System.IO.MemoryMappedFiles;
using System.Runtime.Versioning;
using System.Text;

namespace CheatEngine.Mcp.Tests.LiveQualification.Infrastructure;

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

	private readonly EventWaitHandle _bufferReady;
	private readonly EventWaitHandle _dataReady;
	private readonly Thread _listener;

	private readonly MemoryMappedFile _section;
	private readonly ManualResetEvent _stop = new(false);
	private readonly MemoryMappedViewAccessor _view;

	private DebugOutputCapture(MemoryMappedFile section, EventWaitHandle bufferReady, EventWaitHandle dataReady,
		Encoding ansi)
	{
		_section = section;
		_view = section.CreateViewAccessor(0, DebugOutputBuffer.BufferSize, MemoryMappedFileAccess.Read);
		_bufferReady = bufferReady;
		_dataReady = dataReady;
		Buffer = new DebugOutputBuffer(ansi);
		_listener = new Thread(Listen) { IsBackground = true, Name = "DBWIN listener" };
		_listener.Start();
	}

	/// <summary>The captured messages.</summary>
	internal DebugOutputBuffer Buffer
	{
		get;
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

	/// <summary>Owns the DBWIN objects and starts listening.</summary>
	internal static DebugOutputCapture Start(Encoding ansi)
	{
		ArgumentNullException.ThrowIfNull(ansi);
		EventWaitHandle bufferReady =
			new(false, EventResetMode.AutoReset, BufferReadyName, out bool bufferReadyCreated);
		EventWaitHandle? dataReady = null;
		MemoryMappedFile? section = null;
		try
		{
			dataReady = new EventWaitHandle(false, EventResetMode.AutoReset, DataReadyName, out bool dataReadyCreated);
			if (!bufferReadyCreated || !dataReadyCreated)
			{
				throw new InvalidOperationException(
					"Another debug output listener (for example DebugView) owns the DBWIN events; close it.");
			}

			section = MemoryMappedFile.CreateNew(BufferName, DebugOutputBuffer.BufferSize,
				MemoryMappedFileAccess.ReadWrite);
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
		IEnumerable<string> lines = Buffer.Messages.Where(message => message.ProcessId == processId)
			.Select(static message => message.Text);
		File.WriteAllLines(path, lines, new UTF8Encoding(false));
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
