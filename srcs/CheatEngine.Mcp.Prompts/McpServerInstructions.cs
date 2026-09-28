using CheatEngine.Mcp.Core.Contract;

namespace CheatEngine.Mcp.Prompts;

/// <summary>
///     The <c>initialize</c> instructions: the scope, consent and calling rules a client needs before its first
///     call, under two hundred words each. They cite tool names only through <see cref="CheatEngineToolNames" /> and
///     documents only through <see cref="McpResourceUris.Doc" />, so a renamed tool or document breaks the build or
///     its tests.
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

	/// <summary>The rules both hosts share: scope, consent, orientation, the guided workflows and the calls.</summary>
	private static string Rules
	{
		get;
	} =
		"Use it only on single-player or offline software the user may modify; stop at online games and anti-cheat. " +
		"Explain each change to the game or to Cheat Engine and get the user's consent first. Start with " +
		CheatEngineToolNames.RuntimeGetInfo + " and " + CheatEngineToolNames.RuntimeGetOverview + ", then " +
		CheatEngineToolNames.ProcessList + " and " + CheatEngineToolNames.ProcessAttach + "; call " +
		CheatEngineToolNames.RuntimeReleaseResources + " before switching processes. Read " +
		McpResourceUris.Doc("workflows") + " first: the session rules and the guided workflows, which are also " +
		"prompts. Address inputs are Cheat Engine expressions such as game.exe+1A2B or 7FF6A1B2C3D0. Outputs use " +
		"uppercase hex, spaced hex bytes, and strings for 64-bit integers and memory values; pointer offsets follow " +
		"dereference order. Most lists page with offset and limit. Long operations are jobs that return a jobId; poll " +
		"it with afterSequence and stop it with " + CheatEngineToolNames.RuntimeStopJob + ". Failures set isError and " +
		"carry error.kind, hostEffect and hint (" + McpResourceUris.Doc("errors-and-recovery") + "). Never repeat a " +
		"mutation after a timeout or when hostEffect is started or unknown; inspect state first. Server settings can " +
		"disable some tools (capability_disabled).";

	/// <summary>The stdio gateway's instructions.</summary>
	public static string Gateway
	{
		get;
	} = GatewayRouting + Rules;

	/// <summary>The instructions of a backend serving one instance.</summary>
	public static string Backend
	{
		get;
	} = BackendScope + Rules;
}
