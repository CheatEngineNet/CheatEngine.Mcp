using System.Reflection;

using Microsoft.Extensions.DependencyInjection;

namespace CheatEngine.Mcp.Core.Composition;

/// <summary>
///     The activation's primitive instances, resolved eagerly from its scope so every constructor runs while the
///     activation
///     is built and no container is consulted per request. The activation scope owns and disposes them.
/// </summary>
public sealed class McpPrimitiveTargets
{
	private readonly Dictionary<Type, object> _targets;

	private McpPrimitiveTargets(Dictionary<Type, object> targets)
	{
		_targets = targets;
	}

	/// <summary>Resolves one instance of every declared type that has an instance primitive method.</summary>
	/// <param name="services">The activation scope's services.</param>
	/// <param name="manifest">The primitives the composition declared.</param>
	/// <returns>The resolved targets.</returns>
	public static McpPrimitiveTargets Resolve(IServiceProvider services, CheatEngineMcpPrimitiveOptions manifest)
	{
		ArgumentNullException.ThrowIfNull(services);
		ArgumentNullException.ThrowIfNull(manifest);
		Dictionary<Type, object> targets = [];
		foreach (CheatEngineMcpPrimitive primitive in manifest.Primitives)
		{
			if (!targets.ContainsKey(primitive.Type) && primitive.Type
				    .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
				    .Any(CheatEngineMcpServerBuilderExtensions.IsPrimitiveMethod))
			{
				targets.Add(primitive.Type, services.GetRequiredService(primitive.Type));
			}
		}

		return new McpPrimitiveTargets(targets);
	}

	/// <summary>Returns the resolved instance of a declared primitive type.</summary>
	/// <param name="type">The primitive container type.</param>
	/// <returns>The activation-owned instance.</returns>
	public object Get(Type type)
	{
		ArgumentNullException.ThrowIfNull(type);
		return _targets.TryGetValue(type, out object? target)
			? target
			: throw new InvalidOperationException($"No activation target was resolved for {type.FullName}.");
	}
}
