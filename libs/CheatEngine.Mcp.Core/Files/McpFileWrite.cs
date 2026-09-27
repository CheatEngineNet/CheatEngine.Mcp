using Microsoft.Win32.SafeHandles;

namespace CheatEngine.Mcp.Core.Files;

/// <summary>
///     A single file write held below validated directory handles. Disposing an uncommitted write removes its private
///     temporary file before releasing the directory chain.
/// </summary>
public sealed class McpFileWrite : IDisposable
{
	private readonly SafeFileHandle _directory;
	private readonly List<SafeFileHandle> _directories;
	private readonly string _name;
	private readonly bool _overwrite;
	private bool _committed;
	private bool _disposed;

	internal McpFileWrite(string fullPath, List<SafeFileHandle> directories, string name, bool overwrite)
	{
		ArgumentNullException.ThrowIfNull(directories);
		ArgumentException.ThrowIfNullOrWhiteSpace(name);
		if (directories.Count == 0)
		{
			throw new ArgumentException("At least one anchored directory is required.", nameof(directories));
		}

		FullPath = fullPath;
		_directories = directories;
		_directory = directories[^1];
		_name = name;
		_overwrite = overwrite;
		SafeFileHandle? file = null;
		try
		{
			file = WindowsAnchoredFiles.CreateNewFile(_directory, TemporaryName());
			Stream = new FileStream(file, FileAccess.Write, 1 << 16, isAsync: false);
		}
		catch
		{
			file?.Dispose();
			DisposeDirectories();
			throw;
		}
	}

	/// <summary>The normalized destination path, retained for the tool result after this transaction commits.</summary>
	public string FullPath
	{
		get;
	}

	/// <summary>The exclusively held stream for the temporary file.</summary>
	public FileStream Stream
	{
		get;
	}

	/// <summary>Flushes the temporary file and atomically renames it to the requested destination below the held parent.</summary>
	/// <exception cref="InvalidOperationException">The transaction is already committed or disposed.</exception>
	public void Commit()
	{
		ObjectDisposedException.ThrowIf(_disposed, this);
		if (_committed)
		{
			throw new InvalidOperationException("The file write is already committed.");
		}

		Stream.Flush(flushToDisk: true);
		WindowsAnchoredFiles.Rename(Stream.SafeFileHandle, _directory, _name, _overwrite);
		_committed = true;
	}

	/// <inheritdoc />
	public void Dispose()
	{
		if (_disposed)
		{
			return;
		}

		_disposed = true;
		try
		{
			if (!_committed && !Stream.SafeFileHandle.IsClosed)
			{
				try
				{
					// FileDispositionInformation permits no more operation on this handle except closing it.
					WindowsAnchoredFiles.MarkForDelete(Stream.SafeFileHandle);
				}
				catch (IOException)
				{
					// A failed cleanup leaves only an unguessable .partial name in the already anchored directory.
				}
			}
		}
		finally
		{
			try
			{
				Stream.Dispose();
			}
			finally
			{
				DisposeDirectories();
			}
		}
	}

	private static string TemporaryName()
	{
		return $".{Guid.NewGuid():N}.partial";
	}

	private void DisposeDirectories()
	{
		for (int index = _directories.Count - 1; index >= 0; index--)
		{
			_directories[index].Dispose();
		}
	}
}
