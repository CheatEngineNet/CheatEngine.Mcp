// Adapted from CheatEngine.Client; see NOTICE.md and licenses/CheatEngine.Client.LICENSE.

using System.Runtime.Versioning;
using System.Text;
using System.Text.Json;

using Microsoft.Win32;

namespace CheatEngine.Mcp.Tests.LiveQualification.Infrastructure;

/// <summary>
///     Takes, writes, reads and restores recursive snapshots of a key of <c>HKEY_CURRENT_USER</c> (schema
///     <c>cheatengine-client-registry-backup/v1</c>): every value name, type and raw data, and every subkey. A value of a
///     type the snapshot cannot restore exactly is refused, so the guard never changes a key it could not put back.
///     Restoring deletes the key tree, recreates it from the snapshot and proves the result equal to it.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class RegistrySnapshot
{
	/// <summary>The backup schema.</summary>
	internal const string Schema = "cheatengine-client-registry-backup/v1";

	private static readonly JsonWriterOptions WriterOptions = new() { Indented = true };

	/// <summary>Captures <paramref name="subKey" /> of <c>HKEY_CURRENT_USER</c>.</summary>
	internal static RegistryTreeSnapshot Capture(string subKey)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(subKey);
		using RegistryKey? key = Registry.CurrentUser.OpenSubKey(subKey, false);
		return new RegistryTreeSnapshot(subKey, key is null ? null : CaptureKey(key, string.Empty));
	}

	/// <summary>
	///     Deletes <see cref="RegistryTreeSnapshot.SubKey" />, recreates it from the snapshot and verifies it. Only the Cheat
	///     Engine key and the test scratch keys can be restored (<see cref="CheatEngineUserStateLocations" />).
	/// </summary>
	internal static void Restore(RegistryTreeSnapshot snapshot)
	{
		ArgumentNullException.ThrowIfNull(snapshot);
		CheatEngineUserStateLocations.RequireGuardedSubKey(snapshot.SubKey);
		Registry.CurrentUser.DeleteSubKeyTree(snapshot.SubKey, false);
		if (snapshot.Root is not null)
		{
			using RegistryKey key = Registry.CurrentUser.CreateSubKey(snapshot.SubKey, true);
			Write(key, snapshot.Root);
		}

		string expected = Serialize(snapshot);
		if (!string.Equals(Serialize(Capture(snapshot.SubKey)), expected, StringComparison.Ordinal))
		{
			throw new InvalidOperationException(
				$"HKEY_CURRENT_USER\\{snapshot.SubKey} differs from its backup after the restore.");
		}
	}

	/// <summary>The snapshot as indented JSON; two snapshots are equal exactly when their texts are.</summary>
	internal static string Serialize(RegistryTreeSnapshot snapshot)
	{
		ArgumentNullException.ThrowIfNull(snapshot);
		using MemoryStream buffer = new();
		using (Utf8JsonWriter json = new(buffer, WriterOptions))
		{
			json.WriteStartObject();
			json.WriteString("schema", Schema);
			json.WriteString("key", "HKEY_CURRENT_USER\\" + snapshot.SubKey);
			json.WriteBoolean("exists", snapshot.Exists);
			if (snapshot.Root is not null)
			{
				json.WritePropertyName("root");
				WriteKey(json, snapshot.Root);
			}

			json.WriteEndObject();
		}

		return Encoding.UTF8.GetString(buffer.ToArray());
	}

	/// <summary>Reads a snapshot written by <see cref="Serialize" />.</summary>
	internal static RegistryTreeSnapshot Parse(string text)
	{
		ArgumentNullException.ThrowIfNull(text);
		using JsonDocument document = JsonDocument.Parse(text);
		JsonElement root = document.RootElement;
		if (!string.Equals(root.GetProperty("schema").GetString(), Schema, StringComparison.Ordinal))
		{
			throw new InvalidDataException($"The registry backup does not declare {Schema}.");
		}

		string key = root.GetProperty("key").GetString() ?? string.Empty;
		const string hive = "HKEY_CURRENT_USER\\";
		if (!key.StartsWith(hive, StringComparison.Ordinal))
		{
			throw new InvalidDataException("The registry backup does not name a key of HKEY_CURRENT_USER.");
		}

		return new RegistryTreeSnapshot(key[hive.Length..],
			root.GetProperty("exists").GetBoolean() ? ReadKey(root.GetProperty("root"), string.Empty) : null);
	}

	private static RegistryKeySnapshot CaptureKey(RegistryKey key, string name)
	{
		List<RegistryValueSnapshot> values = [];
		foreach (string valueName in key.GetValueNames().Order(StringComparer.OrdinalIgnoreCase))
		{
			RegistryValueKind kind = key.GetValueKind(valueName);
			object data = key.GetValue(valueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames)
						  ?? throw new InvalidOperationException(
							  $"The value '{valueName}' of {key.Name} vanished while it was read.");
			values.Add(new RegistryValueSnapshot(valueName, kind, kind switch
			{
				RegistryValueKind.String or RegistryValueKind.ExpandString => (string) data,
				RegistryValueKind.MultiString => (string[]) data,
				RegistryValueKind.DWord => (int) data,
				RegistryValueKind.QWord => (long) data,
				RegistryValueKind.Binary or RegistryValueKind.None => (byte[]) data,
				_ => throw new NotSupportedException(
					$"The value '{valueName}' of {key.Name} has the type {kind}, which the guard cannot restore exactly; no session may start.")
			}));
		}

		List<RegistryKeySnapshot> keys = [];
		foreach (string child in key.GetSubKeyNames().Order(StringComparer.OrdinalIgnoreCase))
		{
			using RegistryKey subKey = key.OpenSubKey(child, false)
									   ?? throw new InvalidOperationException(
										   $"The subkey '{child}' of {key.Name} vanished while it was read.");
			keys.Add(CaptureKey(subKey, child));
		}

		return new RegistryKeySnapshot(name, values, keys);
	}

	private static void Write(RegistryKey key, RegistryKeySnapshot snapshot)
	{
		foreach (RegistryValueSnapshot value in snapshot.Values)
		{
			key.SetValue(value.Name, value.Data, value.Kind);
		}

		foreach (RegistryKeySnapshot child in snapshot.Keys)
		{
			using RegistryKey subKey = key.CreateSubKey(child.Name, true);
			Write(subKey, child);
		}
	}

	private static void WriteKey(Utf8JsonWriter json, RegistryKeySnapshot key)
	{
		json.WriteStartObject();
		json.WriteString("name", key.Name);
		json.WriteStartArray("values");
		foreach (RegistryValueSnapshot value in key.Values)
		{
			json.WriteStartObject();
			json.WriteString("name", value.Name);
			json.WriteString("kind", value.Kind.ToString());
			json.WritePropertyName("data");
			switch (value.Data)
			{
				case string text:
					json.WriteStringValue(text);
					break;
				case string[] lines:
					json.WriteStartArray();
					foreach (string line in lines)
					{
						json.WriteStringValue(line);
					}

					json.WriteEndArray();
					break;
				case int number:
					json.WriteNumberValue(number);
					break;
				case long number:
					json.WriteNumberValue(number);
					break;
				case byte[] bytes:
					json.WriteBase64StringValue(bytes);
					break;
				default:
					throw new NotSupportedException($"Unexpected registry data for '{value.Name}'.");
			}

			json.WriteEndObject();
		}

		json.WriteEndArray();
		json.WriteStartArray("keys");
		foreach (RegistryKeySnapshot child in key.Keys)
		{
			WriteKey(json, child);
		}

		json.WriteEndArray();
		json.WriteEndObject();
	}

	private static RegistryKeySnapshot ReadKey(JsonElement element, string name)
	{
		List<RegistryValueSnapshot> values = [];
		foreach (JsonElement value in element.GetProperty("values").EnumerateArray())
		{
			RegistryValueKind kind = Enum.Parse<RegistryValueKind>(value.GetProperty("kind").GetString()!);
			JsonElement data = value.GetProperty("data");
			values.Add(new RegistryValueSnapshot(value.GetProperty("name").GetString()!, kind, kind switch
			{
				RegistryValueKind.String or RegistryValueKind.ExpandString => data.GetString()!,
				RegistryValueKind.MultiString => data.EnumerateArray().Select(static line => line.GetString()!)
					.ToArray(),
				RegistryValueKind.DWord => data.GetInt32(),
				RegistryValueKind.QWord => data.GetInt64(),
				RegistryValueKind.Binary or RegistryValueKind.None => data.GetBytesFromBase64(),
				_ => throw new InvalidDataException($"The registry backup holds the unsupported type {kind}.")
			}));
		}

		List<RegistryKeySnapshot> keys = [];
		foreach (JsonElement child in element.GetProperty("keys").EnumerateArray())
		{
			keys.Add(ReadKey(child, child.GetProperty("name").GetString()!));
		}

		return new RegistryKeySnapshot(name, values, keys);
	}
}
