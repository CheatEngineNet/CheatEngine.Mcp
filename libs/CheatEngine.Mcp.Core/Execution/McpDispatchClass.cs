using System.Diagnostics.CodeAnalysis;

namespace CheatEngine.Mcp.Core.Execution;

/// <summary>
///     How long a tool may hold Cheat Engine's main thread, declared on every v2 tool method as
///     <c>[McpMeta(McpDispatchClass.MetaKey, McpDispatchClass.Short)]</c> and published in the tool's <c>_meta</c>.
/// </summary>
/// <remarks>
///     <para>
///         A running dispatch cannot be interrupted, so the class is a reviewed promise, checked against the measured
///         <see cref="DispatchStatistics" /> in live qualification. Work that may exceed about one second is a job, unless
///         it is a bounded native call listed as <see cref="BlockingNative" /> or <see cref="MayPrompt" /> and announced
///         in
///         the tool's description.
///     </para>
/// </remarks>
public static class McpDispatchClass
{
	/// <summary>The tool <c>_meta</c> key that carries the class.</summary>
	public const string MetaKey = "cheatengine/dispatchClass";

	/// <summary>Bounded work within the dispatch budget (<c>Mcp:Execution:DispatchBudgetMilliseconds</c>, 100 ms).</summary>
	[SuppressMessage("Naming", "CA1720:Identifier contains type name",
		Justification = "The name mirrors the contract value short, not a type.")]
	public const string Short = "short";

	/// <summary>
	///     A host scan whose duration grows with the target, such as a value, AOB or reference scan, which holds Cheat
	///     Engine while it runs and can take seconds on a large target; the tool description states its worst case.
	/// </summary>
	public const string HostScan = "host_scan";

	/// <summary>
	///     One native Cheat Engine call that cannot be split and may block the main thread for seconds; the description
	///     states its worst case.
	/// </summary>
	public const string BlockingNative = "blocking_native";

	/// <summary>
	///     A call that may show a Cheat Engine dialog and wait for the user; the description says so and the tool checks
	///     its known preconditions first.
	/// </summary>
	public const string MayPrompt = "may_prompt";

	/// <summary>Whether a value is one of the four reviewed classes.</summary>
	/// <param name="value">The value published under <see cref="MetaKey" />.</param>
	/// <returns><see langword="true" /> for a reviewed class.</returns>
	public static bool IsDefined(string? value)
	{
		return value is Short or HostScan or BlockingNative or MayPrompt;
	}
}
