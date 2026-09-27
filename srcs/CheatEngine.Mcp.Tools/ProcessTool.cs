using System.ComponentModel;

using CheatEngine.Client;
using CheatEngine.Client.Processes;
using CheatEngine.Client.Results;
using CheatEngine.SDK.Engine.Inspection;

using ModelContextProtocol.Server;

namespace CheatEngine.Mcp.Tools;

[McpServerToolType]
public sealed class ProcessTool
{
	private readonly ICheatEngineClient _client;
	private readonly LuaDebuggerCaptureGuard? _debuggerGuard;
	private readonly McpRuntimeInfo _runtime;
	private readonly ScanTool? _scans;
	private readonly TargetResources? _targetResources;

	public ProcessTool(ICheatEngineClient client, McpRuntimeInfo runtime, TargetResources? targetResources = null,
		LuaDebuggerCaptureGuard? debuggerGuard = null, ScanTool? scans = null)
	{
		ArgumentNullException.ThrowIfNull(client);
		ArgumentNullException.ThrowIfNull(runtime);
		_client = client;
		_runtime = runtime;
		_targetResources = targetResources;
		_debuggerGuard = debuggerGuard;
		_scans = scans;
	}

	[McpServerTool(Name = "get_plugin_version")]
	[Description("Get the loaded plugin version and assembly path.")]
	public object GetPluginVersion()
	{
		return ToolExecution.Run(_client,
			() => new
			{
				success = true,
				version = _runtime.Version,
				location = _runtime.Location,
				runtimeLocation = _runtime.RuntimeLocation
			});
	}

	[McpServerTool(Name = "get_process_list")]
	[Description("List a bounded set of local processes.")]
	public object GetProcessList([Description("Maximum copied processes (1-4096).")] int maximumResults = 1024,
		[Description("Optional case-insensitive process-name substring.")]
		string? nameContains = null)
	{
		return ToolExecution.Run(_client, () =>
		{
			if (maximumResults is < 1 or > 4096)
			{
				return ToolExecution.Error("maximumResults must be between 1 and 4096.");
			}

			LocalProcessEnumerationResult result = _client.Processes.GetLocalProcesses(
				new LocalProcessEnumerationRequest(maximumResults, nameContains));
			object[] processes = result.Processes.Select(process => (object) new
			{
				processId = process.Id.Value, processName = process.Name, executablePath = process.ExecutablePath
			}).ToArray();
			return new { success = true, processes, truncated = result.IsTruncated };
		});
	}

	[McpServerTool(Name = "open_process")]
	[Description("Attach Cheat Engine to a process ID or exact process name.")]
	public object OpenProcess([Description("A positive process ID or an exact process name.")] string process)
	{
		return ToolExecution.Run(_client, () =>
		{
			if (string.IsNullOrWhiteSpace(process))
			{
				return ToolExecution.Error("process is required.");
			}

			if (int.TryParse(process, out int requestedProcessId) && requestedProcessId > 0 &&
			    _client.Processes.TryGetCurrentProcess(out ProcessSnapshot current, out _) &&
			    current.Id.Value == requestedProcessId)
			{
				object? endedResources = _targetResources?.ReportEndedResources();
				if (endedResources is not null)
				{
					return endedResources;
				}

				return new { success = true, processId = current.Id.Value, processName = current.Name };
			}

			object? preparation = _debuggerGuard?.PrepareForTransition() ?? _targetResources?.PrepareForTargetChange()
				?? _scans?.PrepareForTargetChange();
			if (preparation is not null)
			{
				return preparation;
			}

			ProcessSnapshot snapshot = requestedProcessId > 0
				? _client.Processes.Attach(new TargetProcessId(requestedProcessId))
				: _client.Processes.AttachExactName(process);
			return new { success = true, processId = snapshot.Id.Value, processName = snapshot.Name };
		});
	}

	[McpServerTool(Name = "get_current_process")]
	[Description("Get the process currently selected in Cheat Engine.")]
	public object GetCurrentProcess()
	{
		return ToolExecution.Run(_client, () =>
		{
			if (!_client.Processes.TryGetCurrentProcess(out ProcessSnapshot snapshot, out CheatEngineFailure failure))
			{
				if (failure.Kind != CheatEngineFailureKind.TargetNotAttached)
				{
					return ToolExecution.Failure(failure);
				}

				return new { success = true, isOpen = false };
			}

			return new
			{
				success = true,
				isOpen = true,
				processId = snapshot.Id.Value,
				processName = snapshot.Name,
				executablePath = snapshot.ExecutablePath,
				pointerSize = snapshot.ConfiguredPointerSizeBytes,
				selectionEpoch = snapshot.SelectionEpoch
			};
		});
	}
}
