using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;

namespace CheatEngine.Mcp.Bootstrap;

internal static class BundleCache
{
	internal static string Extract(Stream payload, string cacheRoot)
	{
		if (!Path.IsPathFullyQualified(cacheRoot))
		{
			throw new ArgumentException("MCP_BUNDLE_CACHE_DIRECTORY must be an absolute directory.", nameof(cacheRoot));
		}
		cacheRoot = Path.GetFullPath(cacheRoot);
		string hash = Convert.ToHexString(SHA256.HashData(payload));
		payload.Position = 0;
		using ZipArchive archive = new(payload, ZipArchiveMode.Read, leaveOpen: true);
		ValidateNames(archive);
		string directory = Path.Combine(cacheRoot, hash);
		if (Directory.Exists(directory))
		{
			ValidateFiles(archive, directory);
			return directory;
		}

		Directory.CreateDirectory(cacheRoot);
		string staging = Path.Combine(cacheRoot, $".{hash}.{Guid.NewGuid():N}.tmp");
		Directory.CreateDirectory(staging);
		try
		{
			foreach (ZipArchiveEntry entry in archive.Entries)
			{
				string path = Path.Combine(staging, entry.FullName);
				Directory.CreateDirectory(Path.GetDirectoryName(path)!);
				using Stream source = entry.Open();
				using FileStream destination = new(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
				source.CopyTo(destination);
			}
			try
			{
				// Atomic publication lets simultaneous CE processes share only a complete cache.
				Directory.Move(staging, directory);
			}
			catch (IOException) when (Directory.Exists(directory))
			{
				ValidateFiles(archive, directory);
			}
			return directory;
		}
		finally
		{
			if (Directory.Exists(staging))
			{
				try
				{
					Directory.Delete(staging, recursive: true);
				}
				catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
				{
					Trace.TraceWarning("Could not remove unused plugin staging directory {0}: {1}", staging, exception.Message);
				}
			}
		}
	}

	private static void ValidateNames(ZipArchive archive)
	{
		HashSet<string> names = new(StringComparer.OrdinalIgnoreCase);
		foreach (ZipArchiveEntry entry in archive.Entries)
		{
			string name = entry.FullName;
			if (string.IsNullOrWhiteSpace(name) || name.Contains('\\', StringComparison.Ordinal) ||
				name.Contains(':', StringComparison.Ordinal) ||
				name.Split('/').Any(static part => part is "" or "." or ".." || part.EndsWith(' ') || part.EndsWith('.')) ||
				!names.Add(name))
			{
				throw new InvalidDataException($"Invalid or duplicate bundled path: {name}");
			}
		}
		if (archive.Entries.Count == 0)
		{
			throw new InvalidDataException("The plugin payload is empty.");
		}
	}

	private static void ValidateFiles(ZipArchive archive, string directory)
	{
		RejectReparsePoint(directory);
		ValidateFileSet(archive, directory);
		foreach (ZipArchiveEntry entry in archive.Entries)
		{
			string path = Path.Combine(directory, entry.FullName);
			for (string? parent = Path.GetDirectoryName(path); parent is not null &&
				!string.Equals(parent, directory, StringComparison.OrdinalIgnoreCase); parent = Path.GetDirectoryName(parent))
			{
				RejectReparsePoint(parent);
			}
			RejectReparsePoint(path);
			using FileStream cached = File.OpenRead(path);
			using Stream bundled = entry.Open();
			if (cached.Length != entry.Length || !SHA256.HashData(cached).AsSpan().SequenceEqual(SHA256.HashData(bundled)))
			{
				throw new InvalidDataException($"Plugin cache integrity check failed: {path}. Close CE and remove this cache version before retrying.");
			}
		}
	}

	private static void ValidateFileSet(ZipArchive archive, string directory)
	{
		HashSet<string> files = new(archive.Entries.Select(static entry => entry.FullName), StringComparer.OrdinalIgnoreCase);
		HashSet<string> directories = new(StringComparer.OrdinalIgnoreCase);
		foreach (string file in files)
		{
			for (int slash = file.LastIndexOf('/'); slash >= 0; slash = file.LastIndexOf('/', slash - 1))
			{
				directories.Add(file[..slash]);
			}
		}
		Stack<string> pending = new();
		pending.Push(directory);
		while (pending.TryPop(out string? current))
		{
			foreach (string path in Directory.EnumerateFileSystemEntries(current))
			{
				RejectReparsePoint(path);
				string relative = Path.GetRelativePath(directory, path).Replace(Path.DirectorySeparatorChar, '/');
				bool isDirectory = (File.GetAttributes(path) & FileAttributes.Directory) != 0;
				if (!(isDirectory ? directories : files).Contains(relative))
				{
					throw new InvalidDataException($"Unexpected path in plugin cache: {path}. Close CE and remove this cache version before retrying.");
				}
				if (isDirectory)
				{
					pending.Push(path);
				}
			}
		}
	}

	private static void RejectReparsePoint(string path)
	{
		if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
		{
			throw new InvalidDataException($"The plugin cache must not contain links: {path}");
		}
	}
}
