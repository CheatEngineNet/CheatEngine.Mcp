using System.Diagnostics;
using System.Security.Cryptography;

namespace CheatEngine.Mcp.Instances;

/// <summary>Publishes one activation and withdraws only its own discovery record.</summary>
internal sealed class InstancePublication : IDisposable
{
	private readonly InstanceRegistry _registry;
	private string? _publishedPath;

	internal InstancePublication(InstanceRegistry registry, string name)
	{
		_registry = registry;
		using Process process = Process.GetCurrentProcess();
		Guid activation = Guid.NewGuid();
		Descriptor = new($"ce-{process.Id}-{activation:N}", name, activation, process.Id,
			process.StartTime.ToUniversalTime().Ticks, "http://127.0.0.1:0/", RandomNumberGenerator.GetHexString(64),
			typeof(InstancePublication).Assembly.GetName().Version?.ToString() ?? "2.0.0");
	}

	internal InstanceDescriptor Descriptor
	{
		get; private set;
	}

	internal void Publish(string endpoint)
	{
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

	public void Dispose()
	{
		string? path = Interlocked.Exchange(ref _publishedPath, null);
		if (path is not null)
		{
			File.Delete(path);
		}
	}
}
