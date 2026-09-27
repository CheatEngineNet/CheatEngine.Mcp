using System.ComponentModel;

namespace CheatEngine.Mcp.Tools.Processes;

/// <summary>One local process available for explicit attachment.</summary>
public sealed record ProcessSummary(
	[property: Description("The positive operating-system process id.")]
	int ProcessId,
	[property: Description("The process image name reported by Cheat Engine, when the host supplied one.")]
	string? ProcessName);

/// <summary>A bounded local-process listing.</summary>
public sealed record ProcessListResult(
	[property: Description("The copied process summaries.")]
	ProcessSummary[] Processes,
	[property: Description("Whether the host had more matching processes than this response contains.")]
	bool Truncated);

/// <summary>The process Cheat Engine selected after an explicit attachment.</summary>
public sealed record ProcessAttachResult(
	[property: Description("The selected process id.")]
	int ProcessId,
	[property: Description("The selected process name, when the host supplied one.")]
	string? ProcessName);

/// <summary>The currently selected Cheat Engine target.</summary>
public sealed record ProcessCurrentResult(
	[property: Description("Whether Cheat Engine currently has a selected target.")]
	bool IsOpen,
	[property: Description("The selected process id, when one is open.")]
	int? ProcessId = null,
	[property: Description("The selected process name, when one is open.")]
	string? ProcessName = null,
	[property: Description("The configured pointer width in bytes, when a target is open.")]
	int? PointerSize = null,
	[property: Description("The target selection epoch, when a target is open.")]
	long? SelectionEpoch = null);

/// <summary>The target selected after Cheat Engine created a process.</summary>
public sealed record ProcessCreateResult(
	[property: Description("The new process id selected by Cheat Engine.")]
	int ProcessId);

/// <summary>The file-as-process target opened by Cheat Engine.</summary>
public sealed record ProcessOpenFileResult(
	[property: Description("The input file name after path-policy validation; it never includes host directories.")]
	string InputFileName,
	[property: Description("Whether Cheat Engine was asked to interpret the input as 64-bit.")]
	bool Requested64Bit,
	[property: Description("The process-like target id after the operation.")]
	int ObservedProcessId,
	[property: Description("The file size Cheat Engine observed after opening it.")]
	long ObservedFileSize);

/// <summary>The completed file-as-process save request.</summary>
public sealed record ProcessSaveFileResult(
	[property: Description("Whether Cheat Engine completed the save request.")]
	bool Saved,
	[property: Description("The published output file name; it never includes host directories.")]
	string? FileName = null);

/// <summary>The outcome when an external Cheat Engine save cannot be atomically published.</summary>
public sealed record ProcessSaveFilePublishFailure(
	[property: Description("Whether the requested destination was published. Always false for this error.")]
	bool DestinationPublished = false,
	[property: Description("Whether MCP confirmed removal of the protected temporary file after the publish failure.")]
	bool TemporaryFileCleanupConfirmed = false);

/// <summary>The observed pause state after a pause or resume request.</summary>
public sealed record ProcessPausedResult(
	[property: Description("Whether the selected target is paused.")]
	bool Paused);

/// <summary>A bounded list of target thread ids.</summary>
public sealed record ProcessThreadListResult(
	[property: Description("Target thread ids reported by Cheat Engine.")]
	long[] Threads,
	[property: Description("Whether more thread ids were present than this response copied.")]
	bool Truncated);

/// <summary>The configured target pointer width after it was read or changed.</summary>
public sealed record ProcessPointerSizeResult(
	[property: Description("The configured target pointer width in bytes.")]
	int PointerSize);
