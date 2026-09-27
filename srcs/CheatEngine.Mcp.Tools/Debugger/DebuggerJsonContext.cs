using System.Text.Json;
using System.Text.Json.Serialization;

using CheatEngine.Mcp.Core.Jobs;

namespace CheatEngine.Mcp.Tools.Debugger;

/// <summary>Source-generated JSON metadata for the debugger domain and its fixed Lua results.</summary>
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(DebuggerAttachment))]
[JsonSerializable(typeof(DebuggerDetached))]
[JsonSerializable(typeof(DebuggerStatus))]
[JsonSerializable(typeof(DebuggerBreakRequested))]
[JsonSerializable(typeof(DebuggerBreakpointSet))]
[JsonSerializable(typeof(DebuggerBreakpointDeleted))]
[JsonSerializable(typeof(DebuggerBreakpointPage))]
[JsonSerializable(typeof(DebuggerBreakpoint[]))]
[JsonSerializable(typeof(DebuggerExecutionContinued))]
[JsonSerializable(typeof(DebuggerContext))]
[JsonSerializable(typeof(DebuggerRegisterSet))]
[JsonSerializable(typeof(DebuggerThreadIgnoreChanged))]
[JsonSerializable(typeof(DebuggerStackTrace))]
[JsonSerializable(typeof(DebuggerStackFrame[]))]
[JsonSerializable(typeof(DebuggerJobStarted))]
[JsonSerializable(typeof(DebuggerCaptureContext))]
[JsonSerializable(typeof(DebuggerCaptureItem))]
[JsonSerializable(typeof(DebuggerCapturePage))]
[JsonSerializable(typeof(DebuggerTracePage))]
[JsonSerializable(typeof(DebuggerRunToStarted))]
[JsonSerializable(typeof(DebuggerInterface))]
[JsonSerializable(typeof(DebuggerBreakpointTrigger))]
[JsonSerializable(typeof(DebuggerBreakpointMethod))]
[JsonSerializable(typeof(DebuggerStepMode))]
[JsonSerializable(typeof(JobStatus))]
[JsonSerializable(typeof(JobState))]
[JsonSerializable(typeof(Dictionary<string, string>))]
[JsonSerializable(typeof(string[]))]
[JsonSerializable(typeof(int?))]
public sealed partial class DebuggerJsonContext : JsonSerializerContext;
