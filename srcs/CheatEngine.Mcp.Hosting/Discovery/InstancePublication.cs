using System.Diagnostics;
using System.Security.Cryptography;

namespace CheatEngine.Mcp.Hosting.Discovery;

/// <summary>Publishes one activation and withdraws only its own discovery record.</summary>
public sealed class InstancePublication : IDisposable
{
	private readonly Lock _gate = new();
	private readonly InstanceRegistry _registry;
	private string? _publishedPath;
	private bool _disposed;

	public InstancePublication(InstanceRegistry registry, string name, string? pluginVersion = null)
	{
		_registry = registry;
		using Process process = Process.GetCurrentProcess();
		Guid activation = Guid.NewGuid();
		Descriptor = new InstanceDescriptor($"ce-{process.Id}-{activation:N}", name, activation, process.Id,
			process.StartTime.ToUniversalTime().Ticks, "http://127.0.0.1:0/", RandomNumberGenerator.GetHexString(64),
			pluginVersion ?? typeof(InstancePublication).Assembly.GetName().Version?.ToString() ?? "2.0.0");
	}

	public InstanceDescriptor Descriptor
	{
		get;
		private set;
	}

	public void Dispose()
	{
		string? path;
		lock (_gate)
		{
			_disposed = true;
			path = _publishedPath;
		}

		if (path is not null)
		{
			File.Delete(path);
			lock (_gate)
			{
				if (string.Equals(_publishedPath, path, StringComparison.Ordinal))
				{
					_publishedPath = null;
				}
			}
		}
	}

	public void Publish(string endpoint)
	{
		lock (_gate)
		{
			ObjectDisposedException.ThrowIf(_disposed, this);
			if (_publishedPath is not null)
			{
				throw new InvalidOperationException("The activation is already published.");
			}

			Descriptor = Descriptor with
			{
				Endpoint = new Uri(endpoint).AbsoluteUri
			};
			_publishedPath = _registry.Publish(Descriptor);
		}
	}
}
