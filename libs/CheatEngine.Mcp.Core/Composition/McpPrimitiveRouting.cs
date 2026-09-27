namespace CheatEngine.Mcp.Core.Composition;

/// <summary>Where a primitive is served: decided only by whether its method is static.</summary>
public enum McpPrimitiveRouting
{
	/// <summary>
	///     A static method: instance-free content such as knowledge documents and prompts. Every backend and the gateway
	///     serve it themselves; the gateway never routes it.
	/// </summary>
	Local,

	/// <summary>
	///     An instance method: it reads or changes one Cheat Engine instance, so the gateway routes it by
	///     <c>instanceId</c>. Every tool is routed this way.
	/// </summary>
	Instance
}
