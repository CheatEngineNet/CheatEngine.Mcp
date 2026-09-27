namespace CheatEngine.Mcp.Core.Features;

/// <summary>
///     A server-side exposure switch of <see cref="McpFeatureOptions" />. A switch withholds a class of tools and fixed
///     scripts from MCP callers; it is not a sandbox.
/// </summary>
public enum McpFeature
{
	/// <summary>Caller-authored Lua and Lua-bearing Cheat Engine tables: <c>Mcp:EnableUnsafeLua</c>.</summary>
	UnsafeLua,

	/// <summary>Caller-supplied Auto Assembler scripts: <c>Mcp:EnableAutoAssembler</c>.</summary>
	AutoAssembler,

	/// <summary>Running code in the target or in Cheat Engine on the caller's behalf: <c>Mcp:EnableTargetCodeExecution</c>.</summary>
	TargetCodeExecution,

	/// <summary>The DBK kernel driver and DBVM: <c>Mcp:EnableKernelAccess</c>.</summary>
	KernelAccess
}
