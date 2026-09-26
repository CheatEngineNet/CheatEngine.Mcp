using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;

namespace CheatEngine.Mcp.Instances;

/// <summary>Discovers only live process activations with literal loopback endpoints.</summary>
internal sealed class InstanceRegistry(string directory)
{
	private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
	internal static string DefaultDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
		"CheatEngine.Mcp", "instances");
	internal string DirectoryPath { get; } = RequireAbsoluteDirectory(directory);

	internal IReadOnlyList<InstanceDescriptor> ReadActive() => ReadActive(CancellationToken.None);

	internal IReadOnlyList<InstanceDescriptor> ReadActive(CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();
		if (!Directory.Exists(DirectoryPath))
		{
			return [];
		}
		List<InstanceDescriptor> instances = [];
		foreach (string path in Directory.EnumerateFiles(DirectoryPath, "*.json"))
		{
			cancellationToken.ThrowIfCancellationRequested();
			try
			{
				using FileStream file = new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
				if (file.Length > 16384)
				{
					continue;
				}
				InstanceDescriptor? instance = JsonSerializer.Deserialize<InstanceDescriptor>(file, JsonOptions);
				if (instance is not null && IsValid(instance)
					&& string.Equals(Path.GetFileNameWithoutExtension(path), instance.ActivationId.ToString("N"), StringComparison.Ordinal)
					&& IsProcessAlive(instance))
				{
					instances.Add(instance);
				}
			}
			catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
			{
				// A publisher may disappear mid-read. Malformed records are not connection instructions.
			}
		}
		return instances.OrderBy(instance => instance.Name, StringComparer.Ordinal)
			.ThenBy(instance => instance.InstanceId, StringComparer.Ordinal).ToArray();
	}

	internal InstanceDescriptor Find(string instanceId)
	{
		InstanceDescriptor[] matching = ReadActive().Where(instance => instance.InstanceId == instanceId).ToArray();
		return matching.Length == 1 ? matching[0]
			: throw new InvalidOperationException($"Cheat Engine instance '{instanceId}' is unavailable or ambiguous. Call list_instances again.");
	}

	internal string Publish(InstanceDescriptor instance)
	{
		if (!IsValid(instance) || !IsProcessAlive(instance))
		{
			throw new InvalidOperationException("Cannot publish an invalid or expired Cheat Engine instance.");
		}
		Directory.CreateDirectory(DirectoryPath);
		string path = Path.Combine(DirectoryPath, instance.ActivationId.ToString("N") + ".json");
		string temporaryPath = path + ".tmp";
		try
		{
			using (FileStream file = new(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
			{
				JsonSerializer.Serialize(file, instance, JsonOptions);
			}
			File.Move(temporaryPath, path, overwrite: false);
			return path;
		}
		finally
		{
			File.Delete(temporaryPath);
		}
	}

	internal static bool IsValid(InstanceDescriptor instance) =>
		instance.ActivationId != Guid.Empty && instance.ProcessId > 0 && instance.ProcessStartUtcTicks > 0
		&& instance.InstanceId == $"ce-{instance.ProcessId}-{instance.ActivationId:N}"
		&& !string.IsNullOrWhiteSpace(instance.Name) && instance.Name.Length <= 128
		&& instance.AccessToken is { Length: 64 } && instance.AccessToken.All(char.IsAsciiHexDigit)
		&& !string.IsNullOrWhiteSpace(instance.PluginVersion)
		&& Uri.TryCreate(instance.Endpoint, UriKind.Absolute, out Uri? endpoint)
		&& endpoint.Scheme == Uri.UriSchemeHttp && endpoint.Host == "127.0.0.1" && endpoint.Port > 0
		&& endpoint.AbsolutePath == "/" && endpoint.Query.Length == 0 && endpoint.Fragment.Length == 0
		&& endpoint.UserInfo.Length == 0;

	private static bool IsProcessAlive(InstanceDescriptor instance)
	{
		try
		{
			using Process process = Process.GetProcessById(instance.ProcessId);
			return !process.HasExited && process.StartTime.ToUniversalTime().Ticks == instance.ProcessStartUtcTicks;
		}
		catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or Win32Exception)
		{
			return false;
		}
	}

	private static string RequireAbsoluteDirectory(string path) => Path.IsPathFullyQualified(path)
		? Path.GetFullPath(path) : throw new ArgumentException("The instance directory must be absolute.", nameof(path));
}
