using System.Globalization;
using System.Runtime.Versioning;
using System.Text;
using System.Text.Json;

namespace CheatEngine.Mcp.Tests.LiveQualification.Infrastructure;

/// <summary>
///     Backs up and restores a folder byte for byte: the backup is a copy plus a listing
///     (<c>cheatengine-client-appdata-backup/v1</c>); restoring deletes the folder, copies the backup back (or leaves it
///     absent when it was absent) and proves the listing equal.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class FileTreeBackup
{
	/// <summary>The listing schema.</summary>
	internal const string Schema = "cheatengine-client-appdata-backup/v1";

	/// <summary>Lists <paramref name="directory" />.</summary>
	internal static FileTreeSnapshot Capture(string directory)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(directory);
		if (!Directory.Exists(directory))
		{
			return new FileTreeSnapshot(false, []);
		}

		List<string> entries = [];
		foreach (string folder in Directory.EnumerateDirectories(directory, "*", SearchOption.AllDirectories))
		{
			entries.Add(Path.GetRelativePath(directory, folder).Replace('\\', '/') + "/");
		}

		foreach (string file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
		{
			entries.Add(string.Create(CultureInfo.InvariantCulture,
				$"{Path.GetRelativePath(directory, file).Replace('\\', '/')} {new FileInfo(file).Length} {CheatEngineInstallation.Sha256(file)}"));
		}

		entries.Sort(StringComparer.Ordinal);
		return new FileTreeSnapshot(true, entries);
	}

	/// <summary>Copies <paramref name="directory" /> to <paramref name="backup" />, if it exists, and returns its listing.</summary>
	internal static FileTreeSnapshot Backup(string directory, string backup)
	{
		FileTreeSnapshot snapshot = Capture(directory);
		if (snapshot.Exists)
		{
			CopyTree(directory, backup);
		}

		FileTreeSnapshot copy = Capture(backup);
		if (snapshot.Exists && !copy.Entries.SequenceEqual(snapshot.Entries, StringComparer.Ordinal))
		{
			throw new InvalidOperationException($"The backup of '{directory}' differs from it.");
		}

		return snapshot;
	}

	/// <summary>Replaces <paramref name="directory" /> with the backup and verifies it against <paramref name="expected" />.</summary>
	internal static void Restore(string directory, string backup, FileTreeSnapshot expected)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(directory);
		ArgumentNullException.ThrowIfNull(expected);
		if (Directory.Exists(directory))
		{
			foreach (string file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
			{
				File.SetAttributes(file, FileAttributes.Normal);
			}

			Directory.Delete(directory, true);
		}

		if (expected.Exists)
		{
			CopyTree(backup, directory);
		}

		FileTreeSnapshot restored = Capture(directory);
		if (restored.Exists != expected.Exists ||
		    !restored.Entries.SequenceEqual(expected.Entries, StringComparer.Ordinal))
		{
			throw new InvalidOperationException($"'{directory}' differs from its backup after the restore.");
		}
	}

	/// <summary>The listing as JSON.</summary>
	internal static string Serialize(string directory, FileTreeSnapshot snapshot)
	{
		ArgumentNullException.ThrowIfNull(snapshot);
		using MemoryStream buffer = new();
		using (Utf8JsonWriter json = new(buffer, CheatEngineRegistryGuard.WriterOptions))
		{
			json.WriteStartObject();
			json.WriteString("schema", Schema);
			json.WriteString("directory", directory);
			json.WriteBoolean("exists", snapshot.Exists);
			json.WriteStartArray("entries");
			foreach (string entry in snapshot.Entries)
			{
				json.WriteStringValue(entry);
			}

			json.WriteEndArray();
			json.WriteEndObject();
		}

		return Encoding.UTF8.GetString(buffer.ToArray());
	}

	/// <summary>Reads a listing written by <see cref="Serialize" />.</summary>
	internal static FileTreeSnapshot Parse(string text)
	{
		using JsonDocument document = JsonDocument.Parse(text);
		JsonElement root = document.RootElement;
		if (!string.Equals(root.GetProperty("schema").GetString(), Schema, StringComparison.Ordinal))
		{
			throw new InvalidDataException($"The folder listing does not declare {Schema}.");
		}

		return new FileTreeSnapshot(root.GetProperty("exists").GetBoolean(),
			[.. root.GetProperty("entries").EnumerateArray().Select(static entry => entry.GetString()!)]);
	}

	private static void CopyTree(string source, string destination)
	{
		Directory.CreateDirectory(destination);
		foreach (string folder in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
		{
			Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, folder)));
		}

		foreach (string file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
		{
			File.Copy(file, Path.Combine(destination, Path.GetRelativePath(source, file)), true);
		}
	}
}
