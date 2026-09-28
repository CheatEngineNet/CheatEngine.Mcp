namespace CheatEngine.Mcp.Core.Composition;

/// <summary>
///     Marks a path variable of a live <c>cheatengine://instance/…</c> resource template whose current values the
///     resource container lists for <c>completion/complete</c>, such as a module or scanner name.
/// </summary>
/// <remarks>
///     <para>
///         The container must implement <see cref="IMcpCompletionSource" />; the startup validator refuses the marker
///         on a Local resource, on a query variable, next to <c>[AllowedValues]</c> (which the SDK completes by itself)
///         and on a container that lists nothing. A live composition answers <c>completion/complete</c> for the marked
///         variables (<c>WithCheatEnginePrimitives</c>), and the gateway forwards those, and only those, to the
///         instance named by the request's <c>context.arguments.instanceId</c>.
///     </para>
///     <para>
///         Mark only variables whose values are cheap and bounded to list: identifiers that expire quickly, such as
///         record ids, or free-form expressions, such as addresses, are not completed.
///     </para>
/// </remarks>
/// <param name="cost">What listing the values costs, which selects the caching policy.</param>
[AttributeUsage(AttributeTargets.Parameter, AllowMultiple = false, Inherited = false)]
public sealed class McpCompletionAttribute(McpCompletionCost cost) : Attribute
{
	/// <summary>What listing the values costs.</summary>
	public McpCompletionCost Cost
	{
		get;
	} = cost;
}
