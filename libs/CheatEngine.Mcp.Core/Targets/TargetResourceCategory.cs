using System.Text.Json.Serialization;

using CheatEngine.Mcp.Core.Contract;

namespace CheatEngine.Mcp.Core.Targets;

/// <summary>How an MCP-owned resource is held; the wire value is the <c>snake_case</c> member name.</summary>
[JsonConverter(typeof(ContractEnumConverter<TargetResourceCategory>))]
public enum TargetResourceCategory
{
	/// <summary>A Client lease, such as an allocation, a patch, a value-scan session or a registered symbol.</summary>
	ClientLease,

	/// <summary>A job: work that runs across calls and is polled.</summary>
	Job,

	/// <summary>An effect recorded in Cheat Engine's Lua state, such as a speedhack, a pause or a breakpoint.</summary>
	LuaState
}
