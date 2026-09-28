using System.Diagnostics.CodeAnalysis;
using System.Reflection;

using CheatEngine.Mcp.Core.Features;

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

	private McpPrimitiveTargets(Dictionary<Type, object> targets, McpFeatureGate? gate)
	{
		_targets = targets;
		Gate = gate;
	}

	/// <summary>
	///     The activation's exposure switches, borrowed like the targets so the MCP host registers no domain service; the
	///     call-tool filter refuses a gated tool through it. <see langword="null" /> when the activation registers none,
	///     which only a composition without gated tools may do.
	/// </summary>
	public McpFeatureGate? Gate
	{
		get;
	}

	/// <summary>Resolves one instance of every declared type that has an instance primitive method, and the gate.</summary>
	/// <param name="services">The activation scope's services.</param>
	/// <param name="manifest">The primitives the composition declared.</param>
	/// <returns>The resolved targets.</returns>
	/// <exception cref="InvalidOperationException">
	///     A declared tool requires a feature, but the activation registers no <see cref="McpFeatureGate" />.
	/// </exception>
	public static McpPrimitiveTargets Resolve(IServiceProvider services, CheatEngineMcpPrimitiveOptions manifest)
	{
		ArgumentNullException.ThrowIfNull(services);
		ArgumentNullException.ThrowIfNull(manifest);
		Dictionary<Type, object> targets = [];
		bool gated = false;
		foreach (CheatEngineMcpPrimitive primitive in manifest.Primitives)
		{
			MethodInfo[] methods = primitive.Type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic |
															 BindingFlags.Instance | BindingFlags.Static);
			gated |= methods.Any(static method => method.IsDefined(typeof(RequiresFeatureAttribute), false));
			if (!targets.ContainsKey(primitive.Type) && methods.Any(static method =>
					!method.IsStatic && CheatEngineMcpServerBuilderExtensions.IsPrimitiveMethod(method)))
			{
				targets.Add(primitive.Type, services.GetRequiredService(primitive.Type));
			}
		}

		McpFeatureGate? gate = services.GetService<McpFeatureGate>();
		if (gated && gate is null)
		{
			throw new InvalidOperationException(
				"The composition declares tools that require MCP features, but the activation registers no feature " +
				"gate; call AddExecutionServices().");
		}

		return new McpPrimitiveTargets(targets, gate);
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

	/// <summary>Returns the resolved instance of a declared primitive type, when the activation resolved one.</summary>
	/// <param name="type">The primitive container type.</param>
	/// <param name="target">The activation-owned instance, when resolved.</param>
	/// <returns><see langword="true" /> when the type has an instance primitive method and was resolved.</returns>
	internal bool TryGet(Type type, [NotNullWhen(true)] out object? target)
	{
		ArgumentNullException.ThrowIfNull(type);
		return _targets.TryGetValue(type, out target);
	}
}
