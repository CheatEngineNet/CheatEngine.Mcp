using System.Text.Json.Serialization;

namespace CheatEngine.Mcp.Core.Contract;

/// <summary>Classifies a failed tool call; the wire value is the <c>snake_case</c> member name.</summary>
[JsonConverter(typeof(ContractEnumConverter<ToolErrorKind>))]
public enum ToolErrorKind
{
	/// <summary>An argument is missing, malformed or out of range; nothing was attempted.</summary>
	InvalidArgument,

	/// <summary>The target or Cheat Engine is not in a state that allows the operation.</summary>
	InvalidState,

	/// <summary>The addressed object, symbol, record or resource does not exist.</summary>
	NotFound,

	/// <summary>No process is attached.</summary>
	NotAttached,

	/// <summary>A competing operation or retained resource blocks the call; it may succeed later.</summary>
	Busy,

	/// <summary>The operation did not finish in time; its outcome is unknown.</summary>
	Timeout,

	/// <summary>The caller's cancellation was observed.</summary>
	Cancelled,

	/// <summary>The server configuration disables this tool or option.</summary>
	CapabilityDisabled,

	/// <summary>This Cheat Engine build, target or runtime does not support the operation.</summary>
	Unsupported,

	/// <summary>The attached process changed or its identity could not be confirmed.</summary>
	TargetChanged,

	/// <summary>Cheat Engine refused the operation or its Lua reported an error.</summary>
	HostRefused,

	/// <summary>Target memory could not be read.</summary>
	MemoryReadFailed,

	/// <summary>Target memory could not be written.</summary>
	MemoryWriteFailed,

	/// <summary>A size, count or result limit would be exceeded.</summary>
	LimitExceeded,

	/// <summary>A composite operation completed only some of its steps; <c>details</c> says which.</summary>
	PartialEffect,

	/// <summary>The plugin activation is stopping or has ended.</summary>
	Stopping,

	/// <summary>The gateway could not reach or verify the requested Cheat Engine instance.</summary>
	InstanceUnavailable,

	/// <summary>An unexpected fault; the outcome is unknown.</summary>
	Internal
}
