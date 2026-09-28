using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;

using CheatEngine.Mcp.Core.Contract;

namespace CheatEngine.Mcp.Hosting.Discovery;

/// <summary>Discovers only live process activations with literal loopback endpoints.</summary>
public sealed class InstanceRegistry(string directory)
{
	public static string DefaultDirectory => Path.Combine(
		Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
		"CheatEngine.Mcp", "instances");

	internal string DirectoryPath
	{
		get;
	} = RequireAbsoluteDirectory(directory);

	internal IReadOnlyList<InstanceDescriptor> ReadActive()
	{
		return ReadActive(CancellationToken.None);
	}

	internal IReadOnlyList<InstanceDescriptor> ReadActive(CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();
		string[] paths;
		try
		{
			if (!Directory.Exists(DirectoryPath))
			{
				return [];
			}

			// Materialize while the directory exists: its deletion must be equivalent to no active records, rather than
			// leaking a transient filesystem error through discovery.
			paths = [.. Directory.EnumerateFiles(DirectoryPath, "*.json")];
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
		{
			return [];
		}

		List<InstanceDescriptor> instances = [];
		foreach (string path in paths)
		{
			cancellationToken.ThrowIfCancellationRequested();
			try
			{
				using FileStream file = new(path, FileMode.Open, FileAccess.Read,
					FileShare.ReadWrite | FileShare.Delete);
				if (file.Length > 16384)
				{
					continue;
				}

				InstanceDescriptor? instance =
					JsonSerializer.Deserialize(file, HostingJsonContext.Default.InstanceDescriptor);
				if (instance is null || !IsValid(instance)
									 || !string.Equals(Path.GetFileNameWithoutExtension(path),
										 instance.ActivationId.ToString("N"), StringComparison.Ordinal))
				{
					continue;
				}

				switch (GetProcessLiveness(instance))
				{
					case ProcessLiveness.Alive:
						instances.Add(instance);
						break;
					case ProcessLiveness.Expired:
						DeleteExpiredRecord(path, instance);
						break;
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
		return matching.Length == 1
			? matching[0]
			: throw new InvalidOperationException(
				$"Cheat Engine instance '{instanceId}' is unavailable or ambiguous. Call {CheatEngineToolNames.InstanceList} again.");
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
				JsonSerializer.Serialize(file, instance, HostingJsonContext.Default.InstanceDescriptor);
			}

			File.Move(temporaryPath, path, false);
			return path;
		}
		finally
		{
			File.Delete(temporaryPath);
		}
	}

	internal static bool IsValid(InstanceDescriptor instance)
	{
		return instance.ActivationId != Guid.Empty && instance.ProcessId > 0 && instance.ProcessStartUtcTicks > 0
			   && instance.InstanceId == $"ce-{instance.ProcessId}-{instance.ActivationId:N}"
			   && !string.IsNullOrWhiteSpace(instance.Name) && instance.Name.Length <= 128
			   && instance.AccessToken is { Length: 64 } && instance.AccessToken.All(char.IsAsciiHexDigit)
			   && !string.IsNullOrWhiteSpace(instance.PluginVersion)
			   && Uri.TryCreate(instance.Endpoint, UriKind.Absolute, out Uri? endpoint)
			   && endpoint.Scheme == Uri.UriSchemeHttp && endpoint.Host == "127.0.0.1" && endpoint.Port > 0
			   && endpoint.AbsolutePath == "/" && endpoint.Query.Length == 0 && endpoint.Fragment.Length == 0
			   && endpoint.UserInfo.Length == 0;
	}

	private static bool IsProcessAlive(InstanceDescriptor instance)
	{
		return GetProcessLiveness(instance) == ProcessLiveness.Alive;
	}

	private static ProcessLiveness GetProcessLiveness(InstanceDescriptor instance)
	{
		try
		{
			using Process process = Process.GetProcessById(instance.ProcessId);
			return !process.HasExited && process.StartTime.ToUniversalTime().Ticks == instance.ProcessStartUtcTicks
				? ProcessLiveness.Alive
				: ProcessLiveness.Expired;
		}
		catch (ArgumentException)
		{
			return ProcessLiveness.Expired;
		}
		catch (InvalidOperationException)
		{
			// The process exited while its state was being inspected.
			return ProcessLiveness.Expired;
		}
		catch (Win32Exception)
		{
			// Access to another process may be denied. Keep the record until its state is observable.
			return ProcessLiveness.Unknown;
		}
	}

	private static void DeleteExpiredRecord(string path, InstanceDescriptor expected)
	{
		try
		{
			// Re-read immediately before deletion. A concurrent publisher or repair that replaced the file leaves its
			// record alone unless it still describes the exact expired activation we observed.
			using FileStream file = new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
			if (file.Length > 16384)
			{
				return;
			}

			InstanceDescriptor? current =
				JsonSerializer.Deserialize(file, HostingJsonContext.Default.InstanceDescriptor);
			if (current != expected || !IsValid(current) || GetProcessLiveness(current) != ProcessLiveness.Expired)
			{
				return;
			}

			File.Delete(path);
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
		{
			// A publisher or an antivirus can still hold the file. The next discovery will retry the cleanup.
		}
	}

	private enum ProcessLiveness
	{
		Alive,
		Expired,
		Unknown
	}

	private static string RequireAbsoluteDirectory(string path)
	{
		return Path.IsPathFullyQualified(path)
			? Path.GetFullPath(path)
			: throw new ArgumentException("The instance directory must be absolute.", nameof(path));
	}
}
