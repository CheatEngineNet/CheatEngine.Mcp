using System.Diagnostics;
using System.Globalization;
using System.Reflection.PortableExecutable;
using System.Runtime.Versioning;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

using CheatEngine.Mcp.Tests.LiveQualification.Infrastructure;

namespace CheatEngine.Mcp.Tests.LiveQualification;

/// <summary>Owns two isolated CE hosts and targets beneath one guarded live-test run.</summary>
[SupportedOSPlatform("windows")]
internal sealed partial class LiveSandboxSession : IAsyncDisposable
{
	private const string PluginFileName = "CheatEngine.Mcp.dll";
	internal const string MonoCollectorSha256 = "37339066F2E0386C4DFF690CF8C774CC34408656EBEA77A2C7D24AF2B150AC86";

	private static readonly string[] RemovedEnvironmentPrefixes =
		["DOTNET_", "MSBUILD", "TESTINGPLATFORM", "VSTEST", "CHEATENGINE_", "CE_SDK_", "CECLIENT_", "MCP_"];

	private static readonly JsonSerializerOptions ReportJson = new() { WriteIndented = true };
	private readonly Dictionary<string, OwnedHost> _hosts = new(StringComparer.Ordinal);
	private readonly SandboxLayout _layout;
	private readonly Dictionary<string, object?> _report = new(StringComparer.Ordinal);
	private readonly FileStream _runLock;
	private readonly SemaphoreSlim _disposeGate = new(1, 1);
	private readonly LiveSandboxOptions _options;
	private long _debuggerTransitionProbeSequence;
	private DebugOutputCapture? _debugOutput;
	private bool _disposed;
	private InstallationFingerprint? _fingerprint;
	private bool _passed;
	private CheatEngineProfile? _profile;
	private string? _runtimeHash;
	private string? _repository;
	private string? _targetHostPath;
	private string? _targetPath;
	private string? _source;
	private CheatEngineRegistryGuard? _stateGuard;
	private ICheatEngineUserStateScope? _userState;
	private LiveSymbolSourceContainment? _symbolSourceContainment;
	private readonly LiveSymbolSourceCleanupGate _symbolSourceCleanupGate = new();

	private LiveSandboxSession(SandboxLayout layout, FileStream runLock, LiveSandboxOptions options)
	{
		_layout = layout;
		_runLock = runLock;
		_options = options;
		_report["schema"] = "cheatengine-mcp-live-multi-instance/v1";
		_report["runId"] = layout.RunId;
		InstanceDirectory = Path.Combine(layout.RunDirectory, "instances");
		GatewayExecutablePath = Path.Combine(layout.RunDirectory, "gateway", "CheatEngine.Mcp.Gateway.exe");
	}

	public string InstanceDirectory
	{
		get;
	}

	public string GatewayExecutablePath
	{
		get;
	}

	public string TargetArchitecture { get; private set; } = "x64";

	public string RunDirectory => _layout.RunDirectory;

	internal string TargetImagePath => _targetHostPath ?? _targetPath ??
		throw new InvalidOperationException("The owned target image has not been prepared.");

	internal string TargetManagedAssemblyPath
	{
		get
		{
			string path = Path.ChangeExtension(_targetPath ??
				throw new InvalidOperationException("The owned target has not been prepared."), ".dll");
			if (!Path.IsPathFullyQualified(path) || !File.Exists(path))
			{
				throw new FileNotFoundException("The owned target's managed assembly is missing.", path);
			}
			return path;
		}
	}

	public LiveCompilerRoots CompilerA => CompilerRoots("A");

	public LiveCompilerRoots CompilerB => CompilerRoots("B");

	public LiveSandboxHost HostA => GetHost("A");
	public LiveSandboxHost HostB => GetHost("B");
	internal NativeSymbolFixture NativeSymbolFixture => _nativeSymbolFixture ?? throw new InvalidOperationException("The native symbol fixture was not prepared.");
	private NativeSymbolFixture? _nativeSymbolFixture;
}
