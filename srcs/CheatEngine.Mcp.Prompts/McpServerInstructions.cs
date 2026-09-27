using CheatEngine.Mcp.Core.Contract;

namespace CheatEngine.Mcp.Prompts;

/// <summary>
///     The <c>initialize</c> instructions: the rules a client needs before its first call, about 130 words each. They
///     cite tool names only through <see cref="CheatEngineToolNames" />, so a renamed tool breaks the build.
/// </summary>
public static class McpServerInstructions
{
	/// <summary>What the gateway says first: every call is routed by an explicit instance id.</summary>
	private const string GatewayRouting =
		"Controls local Cheat Engine 7.7 instances through one gateway. Call " + CheatEngineToolNames.InstanceList +
		" first and pass its instanceId to every other tool; there is no default instance and no fallback. ";

	/// <summary>What a backend says first: it serves exactly one instance.</summary>
	private const string BackendScope =
		"This server controls one Cheat Engine 7.7 instance. Every client of this instance shares its attached process " +
		"and owned resources. ";

	/// <summary>The rules both hosts share.</summary>
	private const string Rules =
		"Orient with " + CheatEngineToolNames.RuntimeGetOverview + ", then " + CheatEngineToolNames.ProcessList +
		" and " + CheatEngineToolNames.ProcessAttach + "; call " + CheatEngineToolNames.RuntimeReleaseResources +
		" before switching processes. Address inputs are Cheat Engine expressions such as game.exe+1A2B or " +
		"7FF6A1B2C3D0. Outputs use " +
		"uppercase hex addresses without 0x, spaced hex bytes, and strings for 64-bit integers and memory values; " +
		"pointer offsets follow dereference order. Lists page with offset and limit. Long operations are jobs: *_start_* " +
		"tools return a jobId; poll it with afterSequence and stop it with " + CheatEngineToolNames.RuntimeStopJob +
		". Failures set isError and carry error.kind, hostEffect and hint. Never repeat a mutation after a timeout or " +
		"when hostEffect is started or unknown; inspect state first. Server settings can disable some tools " +
		"(capability_disabled).";

	/// <summary>The stdio gateway's instructions.</summary>
	public const string Gateway = GatewayRouting + Rules;

	/// <summary>The instructions of a backend serving one instance.</summary>
	public const string Backend = BackendScope + Rules;
}
