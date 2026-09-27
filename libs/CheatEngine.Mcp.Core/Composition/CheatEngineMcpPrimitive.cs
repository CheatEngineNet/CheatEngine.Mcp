using System.Diagnostics.CodeAnalysis;

namespace CheatEngine.Mcp.Core.Composition;

/// <summary>One primitive container type declared by a composition.</summary>
/// <param name="Kind">The primitive kind its marked methods contribute.</param>
/// <param name="Type">The container type whose marked methods become primitives.</param>
public sealed record CheatEngineMcpPrimitive(
	CheatEngineMcpPrimitiveKind Kind,
	[property: DynamicallyAccessedMembers(CheatEngineMcpPrimitive.Members)]
	Type Type)
{
	/// <summary>The members reflected to discover primitive methods and construct instance targets.</summary>
	internal const DynamicallyAccessedMemberTypes Members = DynamicallyAccessedMemberTypes.PublicMethods
	                                                        | DynamicallyAccessedMemberTypes.NonPublicMethods |
	                                                        DynamicallyAccessedMemberTypes.PublicConstructors;
}
