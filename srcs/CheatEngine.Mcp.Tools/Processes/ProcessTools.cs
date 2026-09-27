using System.ComponentModel;

using CheatEngine.Client;
using CheatEngine.Client.Processes;
using CheatEngine.Client.Results;
using CheatEngine.Mcp.Core.Contract;
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

	private const string SetPausedScript = """
	                                       assert(getOpenedProcessID() ~= 0, 'No process is attached.')
	                                       if a[1] then pause() else unpause() end
	                                       local observed = isPaused()
	                                       assert(observed == a[1], 'Cheat Engine did not apply the requested pause state.')
	                                       return { paused = observed }
	                                       """;

	private const string ThreadListScript = """
	                                        assert(getOpenedProcessID() ~= 0, 'No process is attached.')
	                                        local threads = getThreadlist()
	                                        local result = {}
	                                        local maximum = a[1]
	                                        for i = 1, math.min(#threads, maximum) do result[i] = threads[i] end
	                                        return { threads = result, truncated = #threads > maximum }
	                                        """;

	private const string PointerSizeScript = """
	                                         setPointerSize(a[1])
	                                         local pointerSize = getPointerSize()
	                                         assert(pointerSize == a[1], 'setPointerSize did not apply the requested size.')
	                                         return { pointerSize = pointerSize }
	                                         """;

	private readonly ToolDispatch _dispatch;
	private readonly McpFilePaths _files;
	private readonly TargetTransitionGuards _guards;
	private readonly TargetResources _resources;

	/// <summary>Creates the process tools without reading or changing Cheat Engine state.</summary>
	public ProcessTools(ToolDispatch dispatch, TargetResources resources, TargetTransitionGuards guards,
		McpFilePaths files)
	{
		ArgumentNullException.ThrowIfNull(dispatch);
		ArgumentNullException.ThrowIfNull(resources);
		ArgumentNullException.ThrowIfNull(guards);
		ArgumentNullException.ThrowIfNull(files);
		_dispatch = dispatch;
		_files = files;
		_resources = resources;
		_guards = guards;
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
		"Attach to a positive process id or an exact process name. This changes Cheat Engine's selected target and is refused while retained state or a running scan blocks the change.")]
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
			ProcessSnapshot attached = byId
				? client.Processes.Attach(new TargetProcessId(requestedId), token)
				: client.Processes.AttachExactName(process, token);
			return new ProcessAttachResult(attached.Id.Value, attached.Name);
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
		"Launch an external executable through Cheat Engine and select it. The validated executable stays pinned against writes, replacement, renaming and deletion until Cheat Engine returns. The target transition is guarded before the process starts; Windows or Cheat Engine can still refuse the launch.")]
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

		using HeldFile executable = _files.OpenRead(path, CheatEngineToolNames.ProcessCreate, 0, "path");
		return _dispatch.Run(CheatEngineToolNames.ProcessCreate, token =>
		{
			_guards.EnsureCanChangeTarget(new TargetTransition(null, null), token);
			return _dispatch.ExecuteLua(CheatEngineToolNames.ProcessCreate, CreateScript,
				ProcessJsonContext.Default.ProcessCreateResult, token, executable.FullPath, parameters, debug,
				breakOnEntryPoint);
		}, cancellationToken);
	}

	/// <summary>Opens a file through Cheat Engine's file-as-process interface.</summary>
	[McpServerTool(Name = CheatEngineToolNames.ProcessOpenFile, Title = "Open a file as a process", ReadOnly = false,
		Destructive = true, Idempotent = false, OpenWorld = true, UseStructuredContent = true)]
	[McpMeta(McpDispatchClass.MetaKey, McpDispatchClass.Short)]
	[Description(
		"Open a host file through Cheat Engine's file-as-process interface. This changes the selected target. The validated file stays pinned against writes, replacement, renaming and deletion until Cheat Engine returns.")]
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
			ProcessOpenFileResult opened = _dispatch.ExecuteLua(CheatEngineToolNames.ProcessOpenFile, OpenFileScript,
				ProcessJsonContext.Default.ProcessOpenFileResult, token, source.FullPath, is64Bit, startAddress);
			return opened with
			{
				InputFileName = FileName(source.FullPath)
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

	/// <summary>Sets the selected target's paused state.</summary>
	[McpServerTool(Name = CheatEngineToolNames.ProcessSetPaused, Title = "Set process paused", ReadOnly = false,
		Destructive = true, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
	[McpMeta(McpDispatchClass.MetaKey, McpDispatchClass.Short)]
	[Description(
		"Pause or resume the currently selected target and report the state Cheat Engine observed after the request.")]
	public ProcessPausedResult SetPaused(
		[Description("True pauses the target; false resumes it.")]
		bool paused,
		CancellationToken cancellationToken = default)
	{
		return _dispatch.RunLua(CheatEngineToolNames.ProcessSetPaused, SetPausedScript,
			ProcessJsonContext.Default.ProcessPausedResult, cancellationToken, paused);
	}

	/// <summary>Lists target threads through a bounded fixed Lua script.</summary>
	[McpServerTool(Name = CheatEngineToolNames.ProcessListThreads, Title = "List target threads", ReadOnly = true,
		Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
	[McpMeta(McpDispatchClass.MetaKey, McpDispatchClass.Short)]
	[Description("List up to 4096 target thread ids reported by Cheat Engine. A larger host list is marked truncated.")]
	public ProcessThreadListResult ListThreads(CancellationToken cancellationToken = default)
	{
		return _dispatch.RunLua(CheatEngineToolNames.ProcessListThreads, ThreadListScript,
			ProcessJsonContext.Default.ProcessThreadListResult, cancellationToken, MaximumThreads);
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
