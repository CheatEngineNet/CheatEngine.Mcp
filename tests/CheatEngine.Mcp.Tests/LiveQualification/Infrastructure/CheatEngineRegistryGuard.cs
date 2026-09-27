// Adapted from CheatEngine.Client; see NOTICE.md and licenses/CheatEngine.Client.LICENSE.

using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

using Microsoft.Win32;

namespace CheatEngine.Mcp.Tests.LiveQualification.Infrastructure;

/// <summary>
///     Backs up the Cheat Engine user state before a session and restores it, verified, afterwards: a recursive snapshot
///     of the registry key to <c>&lt;run&gt;/hkcu-backup.json</c> and a copy of the <c>%APPDATA%</c> folder. Before
///     anything
///     may change, it writes the crash marker <c>registry-restore-pending.json</c> in the run root, naming the backups.
///     The
///     restore deletes the key tree, recreates it and proves it equal to the backup, does the same for the folder, and
///     only
///     then removes the marker. A marker found when a session begins means a previous run crashed: the guard restores
///     that run's backup first, verifies it, and fails the new run so the operator sees what happened.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class CheatEngineRegistryGuard : ICheatEngineUserStateGuard
{
	/// <summary>The marker schema.</summary>
	internal const string MarkerSchema = "cheatengine-client-user-state-marker/v1";

	/// <summary>Indented JSON for every backup file.</summary>
	internal static readonly JsonWriterOptions WriterOptions = new() { Indented = true };

	private readonly CheatEngineUserStateLocations _locations;
	private readonly IReadOnlyList<string> _neutralizedSubKeys;
	private readonly IReadOnlyList<string> _neutralizedValues;

	/// <summary>
	///     Creates a guard of <paramref name="locations" />. During a session it removes the listed subkeys and values of
	///     the key (the operator's plugin list, once the S0 spike has named it), which the restore brings back.
	/// </summary>
	internal CheatEngineRegistryGuard(CheatEngineUserStateLocations locations, IReadOnlyList<string> neutralizedSubKeys,
		IReadOnlyList<string> neutralizedValues)
	{
		ArgumentNullException.ThrowIfNull(locations);
		ArgumentNullException.ThrowIfNull(neutralizedSubKeys);
		ArgumentNullException.ThrowIfNull(neutralizedValues);
		locations.Validate();
		_locations = locations;
		_neutralizedSubKeys = neutralizedSubKeys;
		_neutralizedValues = neutralizedValues;
	}

	/// <inheritdoc />
	public ICheatEngineUserStateScope Begin(SandboxLayout layout)
	{
		ArgumentNullException.ThrowIfNull(layout);
		RecoverLeftover(layout.RestoreMarkerPath);

		RegistryTreeSnapshot registry = RegistrySnapshot.Capture(_locations.RegistrySubKey);
		string registryText = RegistrySnapshot.Serialize(registry);
		File.WriteAllText(layout.RegistryBackupPath, registryText, new UTF8Encoding(false));
		if (!string.Equals(
			    RegistrySnapshot.Serialize(RegistrySnapshot.Parse(File.ReadAllText(layout.RegistryBackupPath))),
			    registryText,
			    StringComparison.Ordinal))
		{
			throw new InvalidOperationException("The registry backup does not read back equal to the key.");
		}

		FileTreeSnapshot appData = FileTreeBackup.Backup(_locations.AppDataDirectory, layout.AppDataBackupDirectory);
		File.WriteAllText(layout.AppDataManifestPath, FileTreeBackup.Serialize(_locations.AppDataDirectory, appData),
			new UTF8Encoding(false));

		Marker marker = new(layout.RunId, _locations.RegistrySubKey, layout.RegistryBackupPath,
			_locations.AppDataDirectory,
			layout.AppDataManifestPath, layout.AppDataBackupDirectory);
		marker.Write(layout.RestoreMarkerPath);
		Neutralize();
		return new Scope(marker, layout.RestoreMarkerPath);
	}

	/// <summary>
	///     The guard of the workstation's real Cheat Engine user state. It neutralizes nothing yet: the plugin-list values
	///     are among the facts the S0 spike records.
	/// </summary>
	internal static CheatEngineRegistryGuard ForWorkstation()
	{
		return new CheatEngineRegistryGuard(CheatEngineUserStateLocations.Workstation, [], []);
	}

	/// <summary>Restores the backups a marker names, verifies them, then removes the marker.</summary>
	private static void RestoreFromMarker(Marker marker, string markerPath)
	{
		RegistryTreeSnapshot registry = RegistrySnapshot.Parse(File.ReadAllText(marker.RegistryBackup));
		if (!string.Equals(registry.SubKey, marker.RegistrySubKey, StringComparison.OrdinalIgnoreCase))
		{
			throw new InvalidDataException(
				$"The registry backup of run {marker.RunId} names another key than its marker.");
		}

		RegistrySnapshot.Restore(registry);
		FileTreeBackup.Restore(marker.AppDataDirectory, marker.AppDataBackup,
			FileTreeBackup.Parse(File.ReadAllText(marker.AppDataManifest)));
		File.Delete(markerPath);
	}

	private void RecoverLeftover(string markerPath)
	{
		if (!File.Exists(markerPath))
		{
			return;
		}

		Marker marker = Marker.Read(markerPath);
		if (!string.Equals(marker.RegistrySubKey, _locations.RegistrySubKey, StringComparison.OrdinalIgnoreCase) ||
		    !string.Equals(Path.GetFullPath(marker.AppDataDirectory), Path.GetFullPath(_locations.AppDataDirectory),
			    StringComparison.OrdinalIgnoreCase))
		{
			throw new InvalidOperationException(
				$"The restore marker '{markerPath}' names another user state than this guard; restore it by hand.");
		}

		try
		{
			RestoreFromMarker(marker, markerPath);
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
			                                  or InvalidOperationException
			                                  or InvalidDataException or JsonException)
		{
			throw new InvalidOperationException(
				$"Run {marker.RunId} ended without restoring the Cheat Engine user state, and restoring its backup failed now; " +
				$"the marker '{markerPath}' is kept. Restore HKCU\\{marker.RegistrySubKey} from '{marker.RegistryBackup}' and " +
				$"'{marker.AppDataDirectory}' from '{marker.AppDataBackup}' before any other session.", exception);
		}

		throw new InvalidOperationException(
			$"Run {marker.RunId} ended without restoring the Cheat Engine user state. Its backup has now been restored and " +
			"verified and its marker removed; this run stops here. Run the session again.");
	}

	private void Neutralize()
	{
		if (_neutralizedSubKeys.Count == 0 && _neutralizedValues.Count == 0)
		{
			return;
		}

		using RegistryKey? key = Registry.CurrentUser.OpenSubKey(_locations.RegistrySubKey, true);
		if (key is null)
		{
			return;
		}

		foreach (string subKey in _neutralizedSubKeys)
		{
			key.DeleteSubKeyTree(subKey, false);
		}

		foreach (string value in _neutralizedValues)
		{
			key.DeleteValue(value, false);
		}
	}

	/// <summary>The crash marker: which run's backups to restore, and where they are.</summary>
	private sealed record Marker(
		string RunId,
		string RegistrySubKey,
		string RegistryBackup,
		string AppDataDirectory,
		string AppDataManifest,
		string AppDataBackup)
	{
		internal static Marker Read(string path)
		{
			using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
			JsonElement root = document.RootElement;
			if (!string.Equals(root.GetProperty("schema").GetString(), MarkerSchema, StringComparison.Ordinal))
			{
				throw new InvalidDataException($"'{path}' does not declare {MarkerSchema}.");
			}

			return new Marker(Text(root, "runId"), Text(root, "registrySubKey"), Text(root, "registryBackup"),
				Text(root, "appDataDirectory"), Text(root, "appDataManifest"), Text(root, "appDataBackup"));
		}

		/// <summary>Writes the marker atomically: a reader sees the whole marker or none.</summary>
		internal void Write(string path)
		{
			using MemoryStream buffer = new();
			using (Utf8JsonWriter json = new(buffer, WriterOptions))
			{
				json.WriteStartObject();
				json.WriteString("schema", MarkerSchema);
				json.WriteString("runId", RunId);
				json.WriteString("registrySubKey", RegistrySubKey);
				json.WriteString("registryBackup", RegistryBackup);
				json.WriteString("appDataDirectory", AppDataDirectory);
				json.WriteString("appDataManifest", AppDataManifest);
				json.WriteString("appDataBackup", AppDataBackup);
				json.WriteEndObject();
			}

			string temporary = path + "." + RandomNumberGenerator.GetHexString(8, true) + ".tmp";
			File.WriteAllBytes(temporary, buffer.ToArray());
			File.Move(temporary, path, false);
		}

		private static string Text(JsonElement root, string name)
		{
			return root.GetProperty(name).GetString() ??
			       throw new InvalidDataException($"The restore marker has no '{name}'.");
		}
	}

	/// <summary>A backed-up session: <see cref="Restore" /> puts the user state back and removes the marker.</summary>
	private sealed class Scope(Marker marker, string markerPath) : ICheatEngineUserStateScope
	{
		private bool _attempted;

		public bool Restored
		{
			get;
			private set;
		}

		public void Restore()
		{
			_attempted = true;
			RestoreFromMarker(marker, markerPath);
			Restored = true;
		}

		public void Dispose()
		{
			if (!_attempted)
			{
				Restore();
			}
		}
	}
}
