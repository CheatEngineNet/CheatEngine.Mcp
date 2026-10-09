using System.ComponentModel;

using CheatEngine.Client;
using CheatEngine.Client.Processes;
using CheatEngine.Client.Results;
using CheatEngine.Mcp.Core.Contract;
using CheatEngine.Mcp.Core.Features;
using CheatEngine.Mcp.Core.Files;
using CheatEngine.SDK.Engine.Inspection;

using ModelContextProtocol.Server;

namespace CheatEngine.Mcp.Tools.Processes;

/// <summary>Explicit target selection and bounded process-control tools.</summary>
[McpServerToolType]
public sealed class ProcessTools
{
	private const int MaximumPathLength = 4096;
	private const int MaximumThreads = 4096;

	private const string CreateScript = """
	                                    local previous = getOpenedProcessID()
	                                    createProcess(a[1], a[2], a[3], a[4])
	                                    local processId = getOpenedProcessID()
	                                    assert(processId ~= nil and processId ~= 0 and processId ~= previous,
	                                      'createProcess did not select a new process.')
	                                    return { processId = processId }
	                                    """;

	private const string OpenFileScript = """
	                                      openFileAsProcess(a[1], a[2], a[3])
	                                      local processId = getOpenedProcessID()
	                                      assert(processId ~= nil and processId ~= 0, 'openFileAsProcess did not select a file target.')
	                                      return { inputFileName = a[1], requested64Bit = a[2], observedProcessId = processId,
	                                        observedFileSize = getOpenedFileSize() }
	                                      """;

	private const string SaveFileScript = """
	                                      saveOpenedFile(a[1])
	                                      return { saved = true }
	                                      """;

	private const string ThreadListScript = """
	                                        assert(getOpenedProcessID() ~= 0, 'No process is attached.')
	                                        local list = createStringList()
	                                        local ok, result = xpcall(function()
	                                          getThreadlist(list)
	                                          local count = list.Count
	                                          assert(type(count) == 'number' and count >= 0, 'getThreadlist returned an invalid count.')
	                                          local threads = {}
	                                          local maximum = a[1]
	                                          for i = 0, math.min(count, maximum) - 1 do
	                                            local threadId = tonumber(list.Strings[i], 16)
	                                            assert(threadId ~= nil, 'getThreadlist returned an invalid hexadecimal thread id.')
	                                            threads[#threads + 1] = threadId
	                                          end
	                                          return { threads = threads, truncated = count > maximum }
	                                        end, function(error) return error end)
	                                        local destroyed, destroyError = pcall(function() list.destroy() end)
	                                        if not destroyed then error(destroyError, 0) end
	                                        if not ok then error(result, 0) end
	                                        return result
	                                        """;

	private const string PointerSizeScript = """
	                                         setPointerSize(a[1])
	                                         local pointerSize = getPointerSize()
	                                         assert(pointerSize == a[1], 'setPointerSize did not apply the requested size.')
	                                         return { pointerSize = pointerSize }
	                                         """;

	/// <summary>The resource kind of a pause that MCP made.</summary>
	internal const string PauseKind = "pause";

	private const string PauseDetail = "Resumes the paused process.";

	private readonly ToolDispatch _dispatch;
	private readonly McpFilePaths _files;
	private readonly TargetTransitionGuards _guards;
	private readonly Lock _pauseLock = new();
	private readonly TargetResources _resources;
	private readonly Lock _threadsLock = new();
	private readonly TimeProvider _time;
	private ITargetResource? _pause;
	private PreparedThreads? _threads;
	private long _threadsPublishedAt;
	private long _threadsObservedEpoch = -1;
	private int _threadsObservedProcessId;

	/// <summary>Creates the process tools without reading or changing Cheat Engine state.</summary>
	public ProcessTools(ToolDispatch dispatch, TargetResources resources, TargetTransitionGuards guards,
		McpFilePaths files, TimeProvider? time = null)
	{
		ArgumentNullException.ThrowIfNull(dispatch);
		ArgumentNullException.ThrowIfNull(resources);
		ArgumentNullException.ThrowIfNull(guards);
		ArgumentNullException.ThrowIfNull(files);
		_dispatch = dispatch;
		_files = files;
		_resources = resources;
		_guards = guards;
		_time = time ?? TimeProvider.System;
	}

	/// <summary>Lists local processes available for an explicit attachment.</summary>
	[McpServerTool(Name = CheatEngineToolNames.ProcessList, Title = "List local processes", ReadOnly = true,
		Destructive = false, Idempotent = true, OpenWorld = true, UseStructuredContent = true)]
	[McpMeta(McpDispatchClass.MetaKey, McpDispatchClass.Short)]
	[Description(
		"List up to 4096 local processes. Filter with a case-insensitive name substring, then attach by the exact id or exact name.")]
	public ProcessListResult List(
		[Description("Maximum copied processes, 1 to 4096.")]
		int maximumResults = 1024,
		[Description("Optional case-insensitive process-name substring.")]
		string? nameContains = null,
		CancellationToken cancellationToken = default)
	{
		if (maximumResults is < 1 or > 4096)
		{
			throw CheatEngineToolException.InvalidArgument("maximumResults", "must be between 1 and 4096.");
		}

		if (nameContains is { Length: > 256 })
		{
			throw CheatEngineToolException.InvalidArgument("nameContains", "must not exceed 256 characters.");
		}

		return _dispatch.Run(CheatEngineToolNames.ProcessList, token =>
		{
			LocalProcessEnumerationResult listed = _dispatch.Client.Processes.GetLocalProcesses(
				new LocalProcessEnumerationRequest(maximumResults, nameContains), token);
			return new ProcessListResult([
				.. listed.Processes.Select(static process =>
					new ProcessSummary(process.Id.Value, process.Name))
			], listed.IsTruncated);
		}, cancellationToken);
	}

	/// <summary>Attaches Cheat Engine to one requested local process.</summary>
	[McpServerTool(Name = CheatEngineToolNames.ProcessAttach, Title = "Attach to a process", ReadOnly = false,
		Destructive = true, Idempotent = false, OpenWorld = true, UseStructuredContent = true)]
	[McpMeta(McpDispatchClass.MetaKey, McpDispatchClass.Short)]
	[Description(
		"Attach to a positive process id or an exact process name. This changes Cheat Engine's selected target and is refused while retained state or a running scan blocks the change. When the table option Uses Mono is set, Cheat Engine would inject its Mono data collector into the process, so Mcp:EnableTargetCodeExecution must be on.")]
	public ProcessAttachResult Attach(
		[Description("A positive process id or exact process name.")]
		string process,
		CancellationToken cancellationToken = default)
	{
		if (string.IsNullOrWhiteSpace(process))
		{
			throw CheatEngineToolException.InvalidArgument("process", "is required.");
		}

		if (process.Length > 1024)
		{
			throw CheatEngineToolException.InvalidArgument("process", "must not exceed 1024 characters.");
		}

		return _dispatch.Run(CheatEngineToolNames.ProcessAttach, token =>
		{
			ICheatEngineClient client = _dispatch.Client;
			bool byId = int.TryParse(process, out int requestedId) && requestedId > 0;
			int? currentId = null;
			if (byId && client.Processes.TryGetCurrentProcess(out ProcessSnapshot current, out _, token))
			{
				if (current.Id.Value == requestedId)
				{
					_resources.ReportEndedResources(token);
					return new ProcessAttachResult(current.Id.Value, current.Name);
				}

				currentId = current.Id.Value;
			}

			_guards.EnsureCanChangeTarget(new TargetTransition(currentId, byId ? requestedId : null), token);
			bool monoAutoAttach = RequireMonoAutoAttach(CheatEngineToolNames.ProcessAttach, token);
			ProcessSnapshot attached = byId
				? client.Processes.Attach(new TargetProcessId(requestedId), token)
				: client.Processes.AttachExactName(process, token);
			return new ProcessAttachResult(attached.Id.Value, attached.Name, monoAutoAttach);
		}, cancellationToken);
	}

	/// <summary>Gets the currently selected target.</summary>
	[McpServerTool(Name = CheatEngineToolNames.ProcessGetCurrent, Title = "Get current process", ReadOnly = true,
		Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
	[McpMeta(McpDispatchClass.MetaKey, McpDispatchClass.Short)]
	[Description("Read the process currently selected in Cheat Engine. isOpen=false is a normal unattached state.")]
	public ProcessCurrentResult GetCurrent(CancellationToken cancellationToken = default)
	{
		return _dispatch.Run(CheatEngineToolNames.ProcessGetCurrent, token => Current(_dispatch.Client, token),
			cancellationToken);
	}

	/// <summary>Launches a process through Cheat Engine and selects it.</summary>
	[McpServerTool(Name = CheatEngineToolNames.ProcessCreate, Title = "Create a process", ReadOnly = false,
		Destructive = true, Idempotent = false, OpenWorld = true, UseStructuredContent = true)]
	[McpMeta(McpDispatchClass.MetaKey, McpDispatchClass.Short)]
	[Description(
		"Launch an external executable through Cheat Engine and select it. The validated executable stays pinned against writes, replacement, renaming and deletion until Cheat Engine returns. The target transition is guarded before the process starts; Windows or Cheat Engine can still refuse the launch. When the table option Uses Mono is set, Cheat Engine would inject its Mono data collector into the process, so Mcp:EnableTargetCodeExecution must be on.")]
	public ProcessCreateResult Create(
		[Description("Absolute local executable path that passes the host-file policy, 1 to 4096 characters.")]
		string path,
		[Description("Optional command-line parameters, at most 4096 characters.")]
		string? parameters = null,
		[Description("Create under Cheat Engine's debugger.")]
		bool debug = false,
		[Description("Break at the entry point; requires debug=true.")]
		bool breakOnEntryPoint = false,
		CancellationToken cancellationToken = default)
	{
		RequirePath(path, "path");
		if (parameters is { Length: > MaximumPathLength })
		{
			throw CheatEngineToolException.InvalidArgument("parameters", "must not exceed 4096 characters.");
		}

		if (breakOnEntryPoint && !debug)
		{
			throw CheatEngineToolException.InvalidArgument("breakOnEntryPoint", "requires debug=true.");
		}

		using HeldFile executable = _files.OpenRead(path, CheatEngineToolNames.ProcessCreate, 0);
		return _dispatch.Run(CheatEngineToolNames.ProcessCreate, token =>
		{
			_guards.EnsureCanChangeTarget(new TargetTransition(null, null), token);
			bool monoAutoAttach = RequireMonoAutoAttach(CheatEngineToolNames.ProcessCreate, token);
			ProcessCreateResult created = _dispatch.ExecuteLua(CheatEngineToolNames.ProcessCreate, CreateScript,
				ProcessJsonContext.Default.ProcessCreateResult, token, executable.FullPath, parameters, debug,
				breakOnEntryPoint);
			return created with
			{
				MonoAutoAttach = monoAutoAttach
			};
		}, cancellationToken);
	}

	/// <summary>Opens a file through Cheat Engine's file-as-process interface.</summary>
	[McpServerTool(Name = CheatEngineToolNames.ProcessOpenFile, Title = "Open a file as a process", ReadOnly = false,
		Destructive = true, Idempotent = false, OpenWorld = true, UseStructuredContent = true)]
	[McpMeta(McpDispatchClass.MetaKey, McpDispatchClass.Short)]
	[Description(
		"Open a host file through Cheat Engine's file-as-process interface. This changes the selected target. The validated file stays pinned against writes, replacement, renaming and deletion until Cheat Engine returns. When the table option Uses Mono is set, Cheat Engine would inject its Mono data collector, so Mcp:EnableTargetCodeExecution must be on.")]
	public ProcessOpenFileResult OpenFile(
		[Description("Absolute local host file path that passes the host-file policy, 1 to 4096 characters.")]
		string filename,
		[Description("Treat the file as 64-bit.")]
		bool is64Bit = true,
		[Description("Optional base address expression accepted by Cheat Engine.")]
		string? startAddress = null,
		CancellationToken cancellationToken = default)
	{
		RequirePath(filename, "filename");
		if (startAddress is { Length: > 1024 })
		{
			throw CheatEngineToolException.InvalidArgument("startAddress", "must not exceed 1024 characters.");
		}

		using HeldFile source = _files.OpenRead(filename, CheatEngineToolNames.ProcessOpenFile, 0, "filename");
		return _dispatch.Run(CheatEngineToolNames.ProcessOpenFile, token =>
		{
			_guards.EnsureCanChangeTarget(new TargetTransition(null, null), token);
			bool monoAutoAttach = RequireMonoAutoAttach(CheatEngineToolNames.ProcessOpenFile, token);
			ProcessOpenFileResult opened = _dispatch.ExecuteLua(CheatEngineToolNames.ProcessOpenFile, OpenFileScript,
				ProcessJsonContext.Default.ProcessOpenFileResult, token, source.FullPath, is64Bit, startAddress);
			return opened with
			{
				InputFileName = FileName(source.FullPath),
				MonoAutoAttach = monoAutoAttach
			};
		}, cancellationToken);
	}

	/// <summary>Saves the currently opened file-as-process target.</summary>
	[McpServerTool(Name = CheatEngineToolNames.ProcessSaveFile, Title = "Save opened file", ReadOnly = false,
		Destructive = true, Idempotent = false, OpenWorld = true, UseStructuredContent = true)]
	[McpMeta(McpDispatchClass.MetaKey, McpDispatchClass.Short)]
	[Description(
		"Save the current file-as-process target to a new explicit host path. The destination must be inside an allowed write root and must not exist. Cheat Engine writes a protected temporary file that is atomically published only after its save succeeds.")]
	public ProcessSaveFileResult SaveFile(
		[Description("Absolute destination path inside Mcp:Files:AllowedRoots, 1 to 4096 characters.")]
		string filename,
		CancellationToken cancellationToken = default)
	{
		RequirePath(filename, "filename");
		using McpFileWrite write = BeginExternalWrite(filename);
		string temporary = PrepareExternalWrite(write);
		ProcessSaveFileResult saved = _dispatch.RunLua(CheatEngineToolNames.ProcessSaveFile, SaveFileScript,
			ProcessJsonContext.Default.ProcessSaveFileResult, cancellationToken, temporary);
		try
		{
			write.Commit();
		}
		catch (IOException exception)
		{
			throw PublishFailure(exception, write.TryAbort());
		}

		return saved with
		{
			FileName = FileName(write.FullPath)
		};
	}

	/// <summary>Sets the selected target's paused state, tracking a pause that MCP makes as a resource.</summary>
	[McpServerTool(Name = CheatEngineToolNames.ProcessSetPaused, Title = "Set process paused", ReadOnly = false,
		Destructive = true, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
	[McpMeta(McpDispatchClass.MetaKey, McpDispatchClass.Short)]
	[Description(
		"Pause or resume the currently selected target and report the state Cheat Engine observed after the " +
		"request. A pause that MCP makes is tracked as a pause resource (resourceId): it blocks a target change " +
		"until runtime_release_resources resumes it or this tool resumes the target. Pausing again while MCP's " +
		"pause of the opened process is tracked reports that resource; a target already paused by anything else, " +
		"the user or an earlier activation, is left untracked. Resuming releases MCP's pause and forgets every " +
		"recorded pause of the opened process. Both directions refuse with invalid_state, changing nothing, when no " +
		"process is attached or the debugger is stopped at a breakpoint. When the process MCP paused is no longer " +
		"the opened one, resuming fails with partial_effect and the pause stays listed for manual recovery.")]
	public ProcessPausedResult SetPaused(
		[Description("True pauses the target; false resumes it.")]
		bool paused,
		CancellationToken cancellationToken = default)
	{
		return _dispatch.Run(CheatEngineToolNames.ProcessSetPaused,
			token => paused ? Pause(token) : Resume(token), cancellationToken);
	}

	/// <summary>Lists target threads through a bounded fixed Lua script.</summary>
	[McpServerTool(Name = CheatEngineToolNames.ProcessListThreads, Title = "List target threads", ReadOnly = true,
		Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
	[McpMeta(McpDispatchClass.MetaKey, McpDispatchClass.BlockingNative)]
	[Description("List up to 4096 target thread ids reported by Cheat Engine. A larger host list is marked truncated. " +
			 "This explicit read prepares the target-thread resource snapshot for 5 seconds. Native thread enumeration " +
			 "can block Cheat Engine for seconds; the copy cap does not limit that enumeration.")]
	public ProcessThreadListResult ListThreads(CancellationToken cancellationToken = default)
	{
		return _dispatch.Run(CheatEngineToolNames.ProcessListThreads, token =>
		{
			ICheatEngineClient client = _dispatch.Client;
			ProcessSnapshot before = client.Processes.GetCurrentProcess(token);
			ProcessThreadListResult listed = _dispatch.ExecuteLua(CheatEngineToolNames.ProcessListThreads,
				ThreadListScript, ProcessJsonContext.Default.ProcessThreadListResult, token, MaximumThreads);
			ProcessSnapshot after = client.Processes.GetCurrentProcess(token);
			EnsureSameThreadTarget(before, after);
			PublishThreads(before, listed);
			return CopyThreads(listed);
		}, cancellationToken);
	}

	/// <summary>Reads the latest explicit target-thread preparation.</summary>
	/// <remarks>This method performs no Lua or native thread enumeration and its preparation expires after five seconds.</remarks>
	/// <param name="cancellationToken">The request's cancellation token.</param>
	/// <returns>A copy of the latest prepared target-thread list.</returns>
	[McpMeta(McpDispatchClass.MetaKey, McpDispatchClass.Short)]
	public ProcessThreadListResult ListPreparedThreads(CancellationToken cancellationToken = default)
	{
		PreparedThreads prepared = _dispatch.Run(CheatEngineToolNames.ProcessListThreads, token =>
		{
			ProcessSnapshot current = _dispatch.Client.Processes.GetCurrentProcess(token);
			return TryGetPreparedThreads(current, out PreparedThreads snapshot)
				? snapshot
				: throw PreparedThreadsMissing();
		}, cancellationToken);
		return new ProcessThreadListResult([.. prepared.Threads], prepared.Truncated);
	}

	/// <summary>Sets Cheat Engine's configured target pointer width.</summary>
	[McpServerTool(Name = CheatEngineToolNames.ProcessSetPointerSize, Title = "Set pointer size", ReadOnly = false,
		Destructive = true, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
	[McpMeta(McpDispatchClass.MetaKey, McpDispatchClass.Short)]
	[Description("Set Cheat Engine's configured target pointer size to 4 or 8 bytes and verify the observed value.")]
	public ProcessPointerSizeResult SetPointerSize(
		[Description("Pointer size in bytes: 4 or 8.")]
		int pointerSize,
		CancellationToken cancellationToken = default)
	{
		if (pointerSize is not (4 or 8))
		{
			throw CheatEngineToolException.InvalidArgument("pointerSize", "must be 4 or 8.");
		}

		return _dispatch.RunLua(CheatEngineToolNames.ProcessSetPointerSize, PointerSizeScript,
			ProcessJsonContext.Default.ProcessPointerSizeResult, cancellationToken, pointerSize);
	}

	private void PublishThreads(ProcessSnapshot target, ProcessThreadListResult listed)
	{
		lock (_threadsLock)
		{
			if (!ObserveThreadTarget(target))
			{
				throw ThreadTargetChanged();
			}

			_threads = new PreparedThreads(target.Id.Value, target.SelectionEpoch, [.. listed.Threads], listed.Truncated);
			_threadsPublishedAt = _time.GetTimestamp();
		}
	}

	private bool TryGetPreparedThreads(ProcessSnapshot target, out PreparedThreads snapshot)
	{
		lock (_threadsLock)
		{
			if (ObserveThreadTarget(target) && _threads is { } current && current.ProcessId == target.Id.Value &&
				current.SelectionEpoch == target.SelectionEpoch && IsThreadsFresh())
			{
				snapshot = current;
				return true;
			}
		}

		snapshot = null!;
		return false;
	}

	private bool ObserveThreadTarget(ProcessSnapshot target)
	{
		if (target.SelectionEpoch < _threadsObservedEpoch ||
			(target.SelectionEpoch == _threadsObservedEpoch && target.Id.Value != _threadsObservedProcessId))
		{
			_threads = null;
			return false;
		}

		if (target.SelectionEpoch > _threadsObservedEpoch)
		{
			_threadsObservedEpoch = target.SelectionEpoch;
			_threadsObservedProcessId = target.Id.Value;
			_threads = null;
		}

		return true;
	}

	private bool IsThreadsFresh()
	{
		return _time.GetElapsedTime(_threadsPublishedAt, _time.GetTimestamp()) < TimeSpan.FromSeconds(5);
	}

	private static ProcessThreadListResult CopyThreads(ProcessThreadListResult result)
	{
		return new ProcessThreadListResult([.. result.Threads], result.Truncated);
	}

	private static void EnsureSameThreadTarget(ProcessSnapshot before, ProcessSnapshot after)
	{
		if (before.Id != after.Id || before.SelectionEpoch != after.SelectionEpoch)
		{
			throw ThreadTargetChanged();
		}
	}

	private static CheatEngineToolException ThreadTargetChanged()
	{
		return new CheatEngineToolException(new ToolError(ToolErrorKind.TargetChanged,
			"The attached process changed while target threads were being read; the completed host read was discarded.",
			CheatEngineToolNames.ProcessListThreads, ToolHostEffect.Completed, false,
			"Repeat process_list_threads for the current target."));
	}

	private static CheatEngineToolException PreparedThreadsMissing()
	{
		return CheatEngineToolException.InvalidState("No current prepared target-thread list is available.",
			$"Run {CheatEngineToolNames.ProcessListThreads} explicitly, then retry within 5 seconds.");
	}

	private sealed record PreparedThreads(int ProcessId, long SelectionEpoch, long[] Threads, bool Truncated);

	/// <summary>
	///     Pauses the opened process inside the dispatch. A new pause is recorded under a fresh id and tracked; while
	///     MCP's tracked pause was recorded for the opened process, the call pauses again without a second record and
	///     reports that one. A tracked pause of another process stays tracked for its own release.
	/// </summary>
	private ProcessPausedResult Pause(CancellationToken cancellationToken)
	{
		string? tracked = TrackedPause()?.Descriptor.Id;
		string id = _resources.NextId(PauseKind);
		ProcessPausedResult observed = _dispatch.ExecuteLua(CheatEngineToolNames.ProcessSetPaused,
			ProcessPauseScripts.Pause, ProcessJsonContext.Default.ProcessPausedResult, cancellationToken,
			_resources.Namespace, id, tracked);
		if (!string.Equals(observed.ResourceId, id, StringComparison.Ordinal))
		{
			// Nothing was recorded: the target was already paused, or MCP's tracked pause was reused.
			return observed with
			{
				ResourceId = string.Equals(observed.ResourceId, tracked, StringComparison.Ordinal) ? tracked : null
			};
		}

		ITargetResource? recorded = null;
		recorded = _resources.TrackState(id, PauseKind, () => ForgetPause(recorded), PauseKind,
			detail: PauseDetail);
		lock (_pauseLock)
		{
			_pause = recorded;
		}

		return observed with
		{
			ResourceId = recorded.Descriptor.Id
		};
	}

	/// <summary>
	///     Resumes the opened process inside the dispatch: once the shared refusals passed, MCP's own pause is released
	///     through its recorded release, which refuses once another process is opened, and then the opened process is
	///     resumed.
	/// </summary>
	private ProcessPausedResult Resume(CancellationToken cancellationToken)
	{
		if (TrackedPause() is { } tracked)
		{
			// A refusal (no process, a stopped debugger) must leave MCP's pause tracked, so check before releasing.
			_dispatch.ExecuteLua(CheatEngineToolNames.ProcessSetPaused, ProcessPauseScripts.ResumeCheck,
				ProcessJsonContext.Default.ProcessPausedResult, cancellationToken);
			ResourceReleaseOutcome released = tracked.Release(cancellationToken);
			// A failed release stays in the Lua state root with its cleanup error, listed for an acknowledgement.
			_resources.Forget(tracked);
			ForgetPause(tracked);
			if (!released.IsComplete)
			{
				throw CheatEngineToolException.PartialEffect(
					"Cheat Engine could not resume the process that MCP paused; the pause stays recorded for manual " +
					"recovery.", released.HostEffect, new ReleasedResource(tracked.Descriptor, released),
					ProcessJsonContext.Default.ReleasedResource, released.IsRetryable,
					"Resume that process in Cheat Engine, then acknowledge the pause with " +
					"runtime_release_resources(acknowledgeIds).");
			}
		}

		return _dispatch.ExecuteLua(CheatEngineToolNames.ProcessSetPaused, ProcessPauseScripts.Resume,
			ProcessJsonContext.Default.ProcessPausedResult, cancellationToken);
	}

	/// <summary>MCP's pause while its handle is active.</summary>
	private ITargetResource? TrackedPause()
	{
		lock (_pauseLock)
		{
			return _pause is { IsEnded: false } pause ? pause : null;
		}
	}

	/// <summary>Drops the reference to a pause once it was released or forgotten.</summary>
	private void ForgetPause(ITargetResource? pause)
	{
		lock (_pauseLock)
		{
			if (pause is not null && ReferenceEquals(_pause, pause))
			{
				_pause = null;
			}
		}
	}

	private static ProcessCurrentResult Current(ICheatEngineClient client, CancellationToken cancellationToken)
	{
		if (!client.Processes.TryGetCurrentProcess(out ProcessSnapshot process, out CheatEngineFailure failure,
				cancellationToken))
		{
			if (failure.Kind is CheatEngineFailureKind.TargetNotAttached)
			{
				return new ProcessCurrentResult(false);
			}

			throw CheatEngineToolException.FromFailure(failure, client.Stopping.IsCancellationRequested);
		}

		return new ProcessCurrentResult(true, process.Id.Value, process.Name, process.ConfiguredPointerSizeBytes,
			process.SelectionEpoch);
	}

	/// <summary>
	///     Requires target-code execution, in the dispatch that opens the process, when Cheat Engine's Mono extension
	///     would inject its data collector into it on its own because the table option UsesMono is set, as table_load
	///     does for a table load.
	/// </summary>
	/// <returns>Whether the collector may be injected, which the result reports as <c>monoAutoAttach</c>.</returns>
	private bool RequireMonoAutoAttach(string toolName, CancellationToken cancellationToken)
	{
		MonoAutoAttachState state = MonoAutoAttachProbe.ReadInDispatch(_dispatch, toolName, cancellationToken);
		return state.RequireForProcessOpen(_dispatch.Features, toolName);
	}

	private McpFileWrite BeginExternalWrite(string filename)
	{
		try
		{
			return _files.BeginWrite(filename, CheatEngineToolNames.ProcessSaveFile, false, "filename");
		}
		catch (IOException exception)
		{
			throw new CheatEngineToolException(new ToolError(ToolErrorKind.InvalidState,
					"The destination file could not be reserved.", CheatEngineToolNames.ProcessSaveFile,
					ToolHostEffect.NotStarted, false,
					"Check that the destination folder is writable and the file does not already exist, then repeat the call."),
				exception);
		}
	}

	private static CheatEngineToolException PublishFailure(IOException exception, bool cleanupConfirmed)
	{
		ToolHostEffect effect = cleanupConfirmed ? ToolHostEffect.Started : ToolHostEffect.CleanupUnconfirmed;
		string hint = cleanupConfirmed
			? "MCP did not replace the destination; inspect the configured write root and any concurrent writer before retrying."
			: "MCP did not replace the destination, but a protected .partial file may remain in the configured write root; inspect it before retrying.";
		CheatEngineToolException partial = CheatEngineToolException.PartialEffect(
			"Cheat Engine saved a protected temporary file, but it could not be atomically published to the requested destination.",
			effect, new ProcessSaveFilePublishFailure(TemporaryFileCleanupConfirmed: cleanupConfirmed),
			ProcessJsonContext.Default.ProcessSaveFilePublishFailure,
			hint: hint);
		return new CheatEngineToolException(partial.Error, exception);
	}

	private static string PrepareExternalWrite(McpFileWrite write)
	{
		try
		{
			return write.PrepareForExternalWrite();
		}
		catch (IOException exception)
		{
			throw new CheatEngineToolException(new ToolError(ToolErrorKind.InvalidState,
				"The protected temporary destination could not be prepared.",
				CheatEngineToolNames.ProcessSaveFile, ToolHostEffect.NotStarted, false,
				"Check that the destination folder is writable, then repeat the call."), exception);
		}
	}

	private static string FileName(string? path)
	{
		if (string.IsNullOrWhiteSpace(path))
		{
			return "unknown";
		}

		string name = Path.GetFileName(path);
		return string.IsNullOrEmpty(name) ? "unknown" : name;
	}

	private static void RequirePath(string? value, string parameter)
	{
		if (string.IsNullOrWhiteSpace(value) || value.Length > MaximumPathLength)
		{
			throw CheatEngineToolException.InvalidArgument(parameter, "must contain 1 to 4096 characters.");
		}
	}
}
