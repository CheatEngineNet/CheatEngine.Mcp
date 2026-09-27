namespace CheatEngine.Mcp.Tools;

/// <summary>Provides constrained local file reads and atomic writes for pointer data.</summary>
internal static class PointerDataFile
{
	internal static string ValidatePath(string filePath, string extension)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
		ArgumentException.ThrowIfNullOrWhiteSpace(extension);
		if (extension[0] != '.')
		{
			throw new ArgumentException("The extension must begin with a period.", nameof(extension));
		}
		if (!string.Equals(extension, ".json", StringComparison.OrdinalIgnoreCase)
			&& !string.Equals(extension, ".scandata", StringComparison.OrdinalIgnoreCase))
		{
			throw new ArgumentException("Pointer data files must use .json or .scandata.", nameof(extension));
		}
		if (!Path.IsPathFullyQualified(filePath) || filePath.StartsWith("\\\\", StringComparison.Ordinal)
			|| filePath.StartsWith("//", StringComparison.Ordinal))
		{
			throw new ArgumentException("The data file path must be an absolute local path.", nameof(filePath));
		}

		string fullPath = Path.GetFullPath(filePath);
		string? root = Path.GetPathRoot(fullPath);
		if (root is null || root.Length != 3 || root[1] != ':' || root[2] != Path.DirectorySeparatorChar
			|| fullPath.AsSpan(root.Length).Contains(':') || !string.Equals(Path.GetExtension(fullPath), extension, StringComparison.OrdinalIgnoreCase))
		{
			throw new ArgumentException("The data file path must be a local path with the required extension and no alternate data stream.", nameof(filePath));
		}

		DriveInfo drive = new(root);
		if (drive.DriveType is DriveType.Network or DriveType.NoRootDirectory or DriveType.Unknown)
		{
			throw new ArgumentException("The data file path must be on a local drive.", nameof(filePath));
		}
		return fullPath;
	}

	internal static void Write(string filePath, string extension, bool overwrite, Action<Stream> write, CancellationToken stopping)
	{
		ArgumentNullException.ThrowIfNull(write);
		string path = ValidatePath(filePath, extension);
		stopping.ThrowIfCancellationRequested();
		string? directory = Path.GetDirectoryName(path);
		if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
		{
			throw new DirectoryNotFoundException("The data file directory does not exist.");
		}
		string temporaryPath = Path.Combine(directory, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
		try
		{
			using (FileStream temporary = new(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
			{
				write(temporary);
				temporary.Flush(flushToDisk: true);
			}
			stopping.ThrowIfCancellationRequested();
			File.Move(temporaryPath, path, overwrite);
		}
		finally
		{
			File.Delete(temporaryPath);
		}
	}

	internal static T Read<T>(string filePath, string extension, int maximumBytes, Func<Stream, T> read, CancellationToken stopping)
	{
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumBytes);
		ArgumentNullException.ThrowIfNull(read);
		string path = ValidatePath(filePath, extension);
		stopping.ThrowIfCancellationRequested();
		using FileStream file = new(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.SequentialScan);
		if (file.Length > maximumBytes)
		{
			throw new InvalidDataException($"The data file exceeds the {maximumBytes}-byte limit.");
		}
		stopping.ThrowIfCancellationRequested();
		return read(file);
	}
}
