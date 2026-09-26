using System.ComponentModel;

using CheatEngine.Client;

using ModelContextProtocol.Server;

namespace CheatEngine.Mcp.Tools;

/// <summary>Exposes process and speed controls supplied by Cheat Engine Lua but not the high-level Client.</summary>
[McpServerToolType]
public sealed class LuaProcessTool
{
	private const int MaximumPathLength = 4096;
	private readonly ICheatEngineClient _client;
	private readonly TargetResources? _targetResources;
	private readonly LuaDebuggerCaptureGuard? _debuggerGuard;

	public LuaProcessTool(ICheatEngineClient client, TargetResources? targetResources = null, LuaDebuggerCaptureGuard? debuggerGuard = null)
	{
		ArgumentNullException.ThrowIfNull(client);
		_client = client;
		_targetResources = targetResources;
		_debuggerGuard = debuggerGuard;
	}

	[McpServerTool(Name = "create_process"), Description("Launch and open a process through Cheat Engine. This starts an external executable.")]
	public object CreateProcess([Description("Executable path, up to 4096 characters.")] string path,
		[Description("Optional command-line parameters.")] string? parameters = null,
		[Description("Create under Cheat Engine's debugger.")] bool debug = false,
		[Description("Break at entry point; requires debug=true.")] bool breakOnEntryPoint = false)
	{
		if (!IsPath(path))
		{
			return ToolExecution.Error("path must contain 1 to 4096 characters.");
		}

		if (parameters?.Length > MaximumPathLength)
		{
			return ToolExecution.Error("parameters must not exceed 4096 characters.");
		}

		if (breakOnEntryPoint && !debug)
		{
			return ToolExecution.Error("breakOnEntryPoint requires debug=true.");
		}

		return ToolExecution.Run(_client, () =>
		{
			object? preparation = _debuggerGuard?.PrepareForTransition() ?? _targetResources?.PrepareForTargetChange();
			if (preparation is not null)
			{
				return preparation;
			}

			return LuaToolRuntime.Invoke(_client, "createProcess", "local previous = getOpenedProcessID(); createProcess(a[1], a[2], a[3], a[4]); local processId = getOpenedProcessID(); assert(processId ~= nil and processId ~= 0 and processId ~= previous, 'createProcess did not select a new process.'); return { created = true, processId = processId }", path, parameters, debug, breakOnEntryPoint);
		});
	}

	[McpServerTool(Name = "pause_process"), Description("Pause the currently opened Cheat Engine target process.")]
	public object PauseProcess() => LuaToolRuntime.Invoke(_client, "pause", "pause(); local paused = isPaused(); assert(paused, 'pause did not pause the current process.'); return { paused = paused }");

	[McpServerTool(Name = "resume_process"), Description("Resume the currently opened Cheat Engine target process.")]
	public object ResumeProcess() => LuaToolRuntime.Invoke(_client, "unpause", "unpause(); local paused = isPaused(); assert(not paused, 'unpause did not resume the current process.'); return { paused = paused }");

	[McpServerTool(Name = "get_process_state"), Description("Get the currently opened target process ID, pause state, and age.")]
	public object GetProcessState() => LuaToolRuntime.Invoke(_client, "getOpenedProcessID", "local pid = getOpenedProcessID(); assert(pid ~= 0, 'No process is attached.'); return { processId = pid, paused = isPaused(), ageMilliseconds = getProcessAge() }");

	[McpServerTool(Name = "get_thread_list"), Description("List target thread IDs reported by Cheat Engine, capped at 4096 entries.")]
	public object GetThreadList() => LuaToolRuntime.Invoke(_client, "getThreadlist", "local threads = getThreadlist(); local result = {}; for i = 1, math.min(#threads, 4096) do result[i] = threads[i] end; return { threads = result, truncated = #threads > 4096 }");

	[McpServerTool(Name = "set_pointer_size"), Description("Set Cheat Engine's configured target pointer size to 4 or 8 bytes.")]
	public object SetPointerSize([Description("Pointer size in bytes: 4 or 8.")] int size)
	{
		if (size is not (4 or 8))
		{
			return ToolExecution.Error("size must be 4 or 8.");
		}

		return LuaToolRuntime.Invoke(_client, "setPointerSize", "setPointerSize(a[1]); local pointerSize = getPointerSize(); assert(pointerSize == a[1], 'setPointerSize did not apply the requested size.'); return { pointerSize = pointerSize }", size);
	}

	[McpServerTool(Name = "get_pointer_size"), Description("Get Cheat Engine's configured target pointer size.")]
	public object GetPointerSize() => ToolExecution.Run(_client, () => new { success = true, pointerSize = _client.Processes.GetCurrentProcess(_client.Stopping).ConfiguredPointerSizeBytes });

	[McpServerTool(Name = "set_speedhack_speed"), Description("Enable Cheat Engine speedhack and set a finite positive target speed.")]
	public object SetSpeedhackSpeed([Description("Finite positive speed multiplier.")] double speed)
	{
		if (!double.IsFinite(speed) || speed <= 0)
		{
			return ToolExecution.Error("speed must be finite and greater than zero.");
		}

		return LuaToolRuntime.Invoke(_client, "speedhack_setSpeed", "speedhack_setSpeed(a[1]); return { speed = speedhack_getSpeed() }", speed);
	}

	[McpServerTool(Name = "get_speedhack_speed"), Description("Get Cheat Engine's last configured speedhack speed.")]
	public object GetSpeedhackSpeed() => LuaToolRuntime.Invoke(_client, "speedhack_getSpeed", "return { speed = speedhack_getSpeed() }");

	[McpServerTool(Name = "open_file_as_process"), Description("Open a file through Cheat Engine's process-like memory interface.")]
	public object OpenFileAsProcess([Description("File path, up to 4096 characters.")] string filename,
		[Description("Treat the file as 64-bit.")] bool is64Bit = true,
		[Description("Optional base address expression or number.")] string? startAddress = null)
	{
		if (!IsPath(filename))
		{
			return ToolExecution.Error("filename must contain 1 to 4096 characters.");
		}

		return ToolExecution.Run(_client, () =>
		{
			object? preparation = _debuggerGuard?.PrepareForTransition() ?? _targetResources?.PrepareForTargetChange();
			if (preparation is not null)
			{
				return preparation;
			}

			return LuaToolRuntime.Invoke(_client, "openFileAsProcess", "openFileAsProcess(a[1], a[2], a[3]); return { requestedFilename = a[1], requested64Bit = a[2], observedProcessId = getOpenedProcessID(), observedFileSize = getOpenedFileSize(), requestCompleted = true }", filename, is64Bit, startAddress);
		});
	}

	[McpServerTool(Name = "get_opened_file_size"), Description("Get the size of the currently opened file-as-process target.")]
	public object GetOpenedFileSize() => LuaToolRuntime.Invoke(_client, "getOpenedFileSize", "return { size = getOpenedFileSize() }");

	[McpServerTool(Name = "save_opened_file"), Description("Save the open file-as-process target, optionally to an explicit filename.")]
	public object SaveOpenedFile([Description("Optional destination filename, up to 4096 characters.")] string? filename = null)
	{
		if (filename is { Length: > MaximumPathLength })
		{
			return ToolExecution.Error("filename must not exceed 4096 characters.");
		}

		return LuaToolRuntime.Invoke(_client, "saveOpenedFile", "saveOpenedFile(a[1]); return { filename = a[1] }", filename);
	}

	private static bool IsPath(string? value) => !string.IsNullOrWhiteSpace(value) && value.Length <= MaximumPathLength;
}
