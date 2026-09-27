using CheatEngine.Mcp.Hosting.Backend;

using Microsoft.Extensions.Logging;

using NLog;
using NLog.Config;
using NLog.Extensions.Logging;
using NLog.Targets;

using LogLevel = NLog.LogLevel;

namespace CheatEngine.Mcp.Plugin;

internal sealed class PluginLog : IMcpBackendLogging, IDisposable
{
	private readonly object _gate = new();
	private bool _disposed;
	private int _references = 1;

	internal PluginLog(string directory)
	{
		LogFilePath = Path.Combine(directory, $"CheatEngine.Mcp.{Environment.ProcessId}.log");
		FileTarget target = new("plugin")
		{
			FileName = LogFilePath,
			ArchiveAboveSize = 10 * 1024 * 1024,
			MaxArchiveFiles = 5,
			Layout = "${longdate}|${level:uppercase=true}|${logger}|${message} ${exception:format=tostring}"
		};
		LoggingConfiguration configuration = new();
		configuration.AddRule(LogLevel.Info, LogLevel.Fatal, target);
		Factory = new LogFactory { Configuration = configuration };
	}

	public string LogFilePath
	{
		get;
	}

	public LogFactory Factory
	{
		get;
	}

	public void Dispose()
	{
		lock (_gate)
		{
			if (_disposed)
			{
				return;
			}

			_disposed = true;
			Release();
		}
	}

	public IDisposable Acquire()
	{
		lock (_gate)
		{
			ObjectDisposedException.ThrowIf(_references == 0, this);
			_references++;
			return new Lease(this);
		}
	}

	public ILoggerProvider CreateProvider()
	{
		return new NLogLoggerProvider(new NLogProviderOptions { ShutdownOnDispose = false }, Factory);
	}

	private void Release()
	{
		lock (_gate)
		{
			if (--_references == 0)
			{
				Factory.Dispose();
			}
		}
	}

	private sealed class Lease(PluginLog owner) : IDisposable
	{
		private PluginLog? _owner = owner;

		public void Dispose()
		{
			Interlocked.Exchange(ref _owner, null)?.Release();
		}
	}
}
