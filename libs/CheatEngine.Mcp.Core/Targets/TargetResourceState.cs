using System.Text.Json.Serialization;

using CheatEngine.Mcp.Core.Contract;

namespace CheatEngine.Mcp.Core.Targets;

/// <summary>Where an MCP-owned resource stands; the wire value is the <c>snake_case</c> member name.</summary>
[JsonConverter(typeof(ContractEnumConverter<TargetResourceState>))]
public enum TargetResourceState
{
	/// <summary>It holds state in Cheat Engine or the target, such as a lease, a running job or a recorded effect.</summary>
	Active,

	/// <summary>It ended and holds no host state, such as a finished job whose results stay pollable until its TTL.</summary>
	Ended,

	/// <summary>A stop was requested and its work has not ended yet.</summary>
	StopPending,

	/// <summary>Its cleanup failed; something may remain that only manual recovery can remove.</summary>
	CleanupFailed
}
