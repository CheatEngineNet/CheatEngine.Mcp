using Microsoft.Win32.SafeHandles;

namespace CheatEngine.Mcp.Core.Files;

/// <summary>
///     A host file opened by <see cref="McpFilePaths.OpenRead" /> and pinned while it is held: the handle shares reading
///     only, so no process can write, replace, rename or delete the file until it is disposed.
/// </summary>
/// <remarks>
///     A tool inspects <see cref="Content" /> and then hands <see cref="FullPath" /> to the Cheat Engine work that opens
///     the file by name, disposing this object only after that work returns; the file Cheat Engine reads is then the one
///     that was inspected. For a cheat table: open, run <see cref="Tables.CheatTableInspector.Inspect(HeldFile)" />,
///     enforce its requirements, load through the Client, then dispose.
/// </remarks>
public sealed class HeldFile : IDisposable
{
	private readonly SafeFileHandle _handle;

	internal HeldFile(string fullPath, SafeFileHandle handle, long length, ReadOnlyMemory<byte> content,
		bool isComplete)
	{
		FullPath = fullPath;
		_handle = handle;
		Length = length;
		Content = content;
		IsComplete = isComplete;
	}

	/// <summary>The normalized full path that passed <see cref="McpFilePaths" />; the only path to hand on.</summary>
	public string FullPath
	{
		get;
	}

	/// <summary>The file's length in bytes when it was opened.</summary>
	public long Length
	{
		get;
	}

	/// <summary>The file's bytes, or empty when the file is longer than the limit it was opened with.</summary>
	public ReadOnlyMemory<byte> Content
	{
		get;
	}

	/// <summary>Whether <see cref="Content" /> holds the whole file; <see langword="false" /> for a file over the limit.</summary>
	public bool IsComplete
	{
		get;
	}

	/// <summary>Whether the handle is still held.</summary>
	public bool IsHeld => !_handle.IsClosed;

	/// <summary>Releases the handle, which lets other processes change the file again.</summary>
	public void Dispose()
	{
		_handle.Dispose();
	}

	/// <summary>Reads a block from the pinned file without reopening its path.</summary>
	public int ReadAt(Span<byte> destination, long offset)
	{
		ArgumentOutOfRangeException.ThrowIfNegative(offset);
		return RandomAccess.Read(_handle, destination, offset);
	}
}
