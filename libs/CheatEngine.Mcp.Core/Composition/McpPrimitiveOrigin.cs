using System.Reflection;

using ModelContextProtocol.Server;

namespace CheatEngine.Mcp.Core.Composition;

/// <summary>
///     The routing marker of a resource or prompt, carried in the SDK primitive's <c>Metadata</c> next to the method and
///     its attributes, so the catalog and the gateway can tell Local from Instance primitives without reflection.
/// </summary>
/// <param name="Kind">The primitive kind.</param>
/// <param name="Routing">Local for a static method, Instance for an instance method.</param>
public sealed record McpPrimitiveOrigin(CheatEngineMcpPrimitiveKind Kind, McpPrimitiveRouting Routing)
{
	/// <summary>The routing of a primitive, decided only by whether its method is static.</summary>
	/// <param name="method">The primitive method.</param>
	/// <returns>Local for a static method; Instance otherwise.</returns>
	public static McpPrimitiveRouting RoutingOf(MethodInfo method)
	{
		ArgumentNullException.ThrowIfNull(method);
		return method.IsStatic ? McpPrimitiveRouting.Local : McpPrimitiveRouting.Instance;
	}

	/// <summary>Reads the routing marker of a created primitive.</summary>
	/// <param name="primitive">A resource or prompt created by <c>WithCheatEnginePrimitives</c> or the local helpers.</param>
	/// <returns>The marker, or <see langword="null" /> for a primitive created elsewhere.</returns>
	public static McpPrimitiveOrigin? Of(IMcpServerPrimitive primitive)
	{
		ArgumentNullException.ThrowIfNull(primitive);
		foreach (object item in primitive.Metadata)
		{
			if (item is McpPrimitiveOrigin origin)
			{
				return origin;
			}
		}

		return null;
	}

	/// <summary>
	///     Replicates the SDK's default metadata (the method, then its declaring type's attributes, then its own) and
	///     appends the routing marker. The SDK derives its default only when the options carry no metadata, so both must
	///     be supplied together.
	/// </summary>
	/// <param name="kind">The primitive kind.</param>
	/// <param name="method">The primitive method.</param>
	/// <returns>The metadata list.</returns>
	internal static IReadOnlyList<object> CreateMetadata(CheatEngineMcpPrimitiveKind kind, MethodInfo method)
	{
		List<object> metadata = [method];
		if (method.DeclaringType is { } declaringType)
		{
			metadata.AddRange(declaringType.GetCustomAttributes());
		}

		metadata.AddRange(method.GetCustomAttributes());
		metadata.Add(new McpPrimitiveOrigin(kind, RoutingOf(method)));
		return metadata.AsReadOnly();
	}

	/// <summary>The primitive method recorded first in a created primitive's metadata.</summary>
	/// <param name="primitive">The primitive.</param>
	/// <returns>The method, or <see langword="null" /> when the metadata does not start with one.</returns>
	internal static MethodInfo? MethodOf(IMcpServerPrimitive primitive)
	{
		return primitive.Metadata is [MethodInfo method, ..] ? method : null;
	}
}
