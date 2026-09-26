using NLog;
using NLog.Config;
using NLog.Targets;

namespace CheatEngine.Mcp;

internal sealed class PluginLog : IDisposable
{
	private readonly object _gate = new object();
	private int _references = 1;
	private bool _disposed;
	public PluginLog() : this(McpOptions.ConfigurationDirectory)
	{
	}

	internal PluginLog(string directory)
	{
		LogFilePath = Path.Combine(directory, $"CheatEngine.Mcp.{Environment.ProcessId}.log");
		FileTarget target = new FileTarget("plugin")
		{
			FileName = LogFilePath,
			ArchiveAboveSize = 10 * 1024 * 1024,
			MaxArchiveFiles = 5,
			Layout = "${longdate}|${level:uppercase=true}|${logger}|${message} ${exception:format=tostring}"
		};
		LoggingConfiguration configuration = new LoggingConfiguration();
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
	public IDisposable Acquire()
	{
		lock (_gate)
		{
			ObjectDisposedException.ThrowIf(_references == 0, this);
			_references++;
			return new Lease(this);
		}
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
		public void Dispose() => Interlocked.Exchange(ref _owner, null)?.Release();
	}
}
