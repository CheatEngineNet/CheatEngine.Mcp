using Microsoft.Win32.SafeHandles;

namespace CheatEngine.Mcp.Core.Files;

/// <summary>
///     A single file write held below validated directory handles. Disposing an uncommitted write removes its private
///     temporary file before releasing the directory chain.
/// </summary>
public sealed class McpFileWrite : IDisposable
{
	private readonly List<SafeFileHandle> _directories;
	private readonly SafeFileHandle _directory;
	private readonly string _name;
	private readonly bool _overwrite;
	private readonly string _temporaryName;
	private bool _cleanupAttempted;
	private bool _cleanupConfirmed;
	private bool _committed;
	private bool _disposed;
	private bool _externalWritePrepared;

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
		_temporaryName = TemporaryName();
		SafeFileHandle? file = null;
		try
		{
			file = WindowsAnchoredFiles.CreateNewFile(_directory, _temporaryName);
			Stream = new FileStream(file, FileAccess.Write, 1 << 16, false);
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

	/// <summary>The temporary path reserved below the anchored destination directory for an external writer.</summary>
	public string TemporaryPath => Path.Combine(Path.GetDirectoryName(FullPath)!, _temporaryName);

	/// <summary>
	///     Closes the private stream and returns its reserved temporary path for one external writer, while retaining the
	///     anchored directory chain. Call <see cref="Commit" /> only after that writer has returned successfully.
	/// </summary>
	/// <exception cref="InvalidOperationException">The transaction was already prepared, committed or disposed.</exception>
	public string PrepareForExternalWrite()
	{
		ObjectDisposedException.ThrowIf(_disposed, this);
		if (_committed || _externalWritePrepared)
		{
			throw new InvalidOperationException("The file write is already prepared or committed.");
		}

		Stream.Flush(true);
		Stream.Dispose();
		_externalWritePrepared = true;
		return TemporaryPath;
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
			if (!_committed)
			{
				CleanupTemporary();
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

	/// <summary>Flushes the temporary file and atomically renames it to the requested destination below the held parent.</summary>
	/// <exception cref="InvalidOperationException">The transaction is already committed or disposed.</exception>
	public void Commit()
	{
		ObjectDisposedException.ThrowIf(_disposed, this);
		if (_committed)
		{
			throw new InvalidOperationException("The file write is already committed.");
		}

		if (_externalWritePrepared)
		{
			using SafeFileHandle temporary = WindowsAnchoredFiles.OpenExistingFile(_directory, _temporaryName);
			WindowsAnchoredFiles.Rename(temporary, _directory, _name, _overwrite);
		}
		else
		{
			Stream.Flush(true);
			WindowsAnchoredFiles.Rename(Stream.SafeFileHandle, _directory, _name, _overwrite);
		}

		_committed = true;
	}

	/// <summary>
	///     Removes the uncommitted temporary file while its anchored directory chain is still held.
	///     Returns <see langword="true" /> only when removal was confirmed.
	/// </summary>
	/// <exception cref="InvalidOperationException">The transaction is already committed.</exception>
	public bool TryAbort()
	{
		ObjectDisposedException.ThrowIf(_disposed, this);
		if (_committed)
		{
			throw new InvalidOperationException("The file write is already committed.");
		}

		return CleanupTemporary();
	}

	private bool CleanupTemporary()
	{
		if (_cleanupAttempted)
		{
			return _cleanupConfirmed;
		}

		_cleanupAttempted = true;
		try
		{
			if (_externalWritePrepared)
			{
				using SafeFileHandle temporary = WindowsAnchoredFiles.OpenExistingFile(_directory, _temporaryName);
				WindowsAnchoredFiles.MarkForDelete(temporary);
			}
			else if (!Stream.SafeFileHandle.IsClosed)
			{
				// FileDispositionInformation permits no more operation on this handle except closing it.
				WindowsAnchoredFiles.MarkForDelete(Stream.SafeFileHandle);
			}

			_cleanupConfirmed = true;
		}
		catch (IOException)
		{
			// A failed cleanup leaves only an unguessable .partial name in the already anchored directory.
		}

		return _cleanupConfirmed;
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
