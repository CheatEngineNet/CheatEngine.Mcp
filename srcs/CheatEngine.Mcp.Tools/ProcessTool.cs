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
	private readonly TargetTransitionGuards _guards;
	private readonly McpRuntimeInfo _runtime;
	private readonly TargetResources? _targetResources;

	/// <param name="client">The activation's Client.</param>
	/// <param name="runtime">The loaded plugin identity.</param>
	/// <param name="targetResources">The activation's resources, which report ended resources on a reselection.</param>
	/// <param name="guards">Every target-transition guard; without it, only <paramref name="targetResources" /> guards.</param>
	public ProcessTool(ICheatEngineClient client, McpRuntimeInfo runtime, TargetResources? targetResources = null,
		TargetTransitionGuards? guards = null)
	{
		ArgumentNullException.ThrowIfNull(client);
		ArgumentNullException.ThrowIfNull(runtime);
		_client = client;
		_runtime = runtime;
		_targetResources = targetResources;
		_guards = guards ?? new TargetTransitionGuards(targetResources is null ? [] : [targetResources]);
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

			bool byId = int.TryParse(process, out int requestedProcessId) && requestedProcessId > 0;
			int? currentProcessId = null;
			if (byId && _client.Processes.TryGetCurrentProcess(out ProcessSnapshot current, out _))
			{
				if (current.Id.Value == requestedProcessId)
				{
					// A reselection keeps every resource; it only forgets and reports those that already ended.
					_targetResources?.ReportEndedResources(_client.Stopping);
					return new { success = true, processId = current.Id.Value, processName = current.Name };
				}

				currentProcessId = current.Id.Value;
			}

			// Every guard refuses as a CheatEngineToolException, which this transition Run reports in band.
			_guards.EnsureCanChangeTarget(new TargetTransition(currentProcessId, byId ? requestedProcessId : null),
				_client.Stopping);

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
