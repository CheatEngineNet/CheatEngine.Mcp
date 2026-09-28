using CheatEngine.Mcp.Core.Files;

namespace CheatEngine.Mcp.Tools.Pointer;

/// <summary>Reads a held file through its pinned handle without buffering the entire compressed map.</summary>
internal sealed class PinnedPointerFileStream(HeldFile file) : Stream
{
	private long _position;

	public override bool CanRead => true;
	public override bool CanSeek => false;
	public override bool CanWrite => false;
	public override long Length => file.Length;
	public override long Position
	{
		get => _position;
		set => throw new NotSupportedException();
	}

	public override int Read(Span<byte> buffer)
	{
		int count = file.ReadAt(buffer, _position);
		_position += count;
		return count;
	}

	public override int Read(byte[] buffer, int offset, int count)
	{
		return Read(buffer.AsSpan(offset, count));
	}

	public override void Flush()
	{
	}
	public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
	public override void SetLength(long value) => throw new NotSupportedException();
	public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
