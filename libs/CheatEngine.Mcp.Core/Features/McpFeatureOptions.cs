namespace CheatEngine.Mcp.Core.Features;

/// <summary>
///     The server's exposure switches, bound once per activation from the flat keys of the <c>Mcp</c> section. Every
///     switch
///     defaults to enabled.
/// </summary>
/// <remarks>
///     <para>
///         These are exposure switches, <b>not a sandbox</b>. Turning one off withholds the tools and fixed scripts that
///         need it: the call is refused with <c>capability_disabled</c> before any argument binding or Cheat Engine work.
///         Everything that stays enabled still runs with Cheat Engine's full privileges, and fixed implementation-owned
///         Lua
///         can do anything Cheat Engine can.
///     </para>
///     <para>
///         <see cref="EnableUnsafeLua" /> is a superset of the others: caller-authored Lua can call every Cheat Engine API
///         and read any file the user can, so with it enabled the other switches are advisory.
///     </para>
///     <para>
///         The activation reads the switches once, and the same instance drives both the MCP gates and the Client's own
///         opt-ins (unsafe Lua execution and Auto Assembler patches), so they cannot disagree. Changes apply after the
///         plugin is disabled and enabled again.
///     </para>
/// </remarks>
public sealed class McpFeatureOptions
{
	/// <summary>The configuration section that holds the switches as flat keys.</summary>
	public const string SectionName = "Mcp";

	/// <summary>
	///     Whether callers may run their own Lua (<c>lua_execute</c>, through the Client's unsafe-Lua opt-in) and load
	///     tables that carry Lua. Fixed, implementation-owned Lua used by the other tools is not affected.
	/// </summary>
	public bool EnableUnsafeLua
	{
		get;
		set;
	} = true;

	/// <summary>
	///     Whether callers may check and apply their own Auto Assembler scripts (through the Client's Auto Assembler patch
	///     opt-in). Template generation is not affected.
	/// </summary>
	public bool EnableAutoAssembler
	{
		get;
		set;
	} = true;

	/// <summary>
	///     Whether tools may run code in the target or in Cheat Engine on the caller's behalf: remote and local calls, DLL
	///     and .NET injection, C and C# compilation, Mono attach (which injects the Mono data collector), Mono method
	///     invocation and speedhack.
	/// </summary>
	public bool EnableTargetCodeExecution
	{
		get;
		set;
	} = true;

	/// <summary>
	///     Whether tools may use the DBK kernel driver and DBVM: physical memory, control and model-specific registers, and
	///     kernel-mode reads and writes.
	/// </summary>
	public bool EnableKernelAccess
	{
		get;
		set;
	} = true;
}
