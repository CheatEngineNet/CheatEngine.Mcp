namespace CheatEngine.Mcp.Core.Features;

/// <summary>
///     Declares that an MCP tool method needs an exposure switch. The Core call-tool filter refuses the call with
///     <c>capability_disabled</c> before argument binding and dispatch when the switch is off, and the tool's
///     <c>_meta</c> lists the requirement under <see cref="McpFeatureGate.RequiresMetaKey" />.
/// </summary>
/// <remarks>
///     Apply it once per switch. A requirement that depends on an argument value is checked imperatively with
///     <see cref="McpFeatureGate.Require" /> before the first host effect instead.
/// </remarks>
/// <param name="feature">The switch the tool needs.</param>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
public sealed class RequiresFeatureAttribute(McpFeature feature) : Attribute
{
	/// <summary>The switch the tool needs.</summary>
	public McpFeature Feature
	{
		get;
	} = feature;
}
