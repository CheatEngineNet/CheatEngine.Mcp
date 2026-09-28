using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;

using CheatEngine.Mcp.Core.Execution;
using CheatEngine.Mcp.Core.Features;

using ModelContextProtocol.Server;

namespace CheatEngine.Mcp.Core.Composition;

/// <summary>
///     The tool a live resource projects, resolved once from its <see cref="McpSourceToolAttribute" /> when the resource
///     is created and carried in the resource's <c>Metadata</c>: the validator checks it and the read filter re-checks its
///     feature gates without reflection per read.
/// </summary>
/// <param name="ToolName">The declared tool name.</param>
/// <param name="ToolType">The declared tool container type.</param>
/// <param name="Method">The one method of <paramref name="ToolType" /> published under that name, if any.</param>
/// <param name="Requires">The switches the tool method requires, ordered and distinct.</param>
/// <param name="ReadOnly">Whether the tool is declared read-only.</param>
/// <param name="OpenWorld">Whether the tool is declared open-world.</param>
/// <param name="DispatchClass">The tool's declared dispatch class, if it publishes one as a string.</param>
internal sealed record McpResourceSource(
	string ToolName,
	Type ToolType,
	MethodInfo? Method,
	IReadOnlyList<McpFeature> Requires,
	bool ReadOnly,
	bool OpenWorld,
	string? DispatchClass)
{
	private const BindingFlags ToolMethods = BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static;

	/// <summary>Resolves the source tool a resource method declares.</summary>
	/// <param name="resourceMethod">The resource method.</param>
	/// <returns>The source, or <see langword="null" /> when the method declares none.</returns>
	internal static McpResourceSource? Resolve(MethodInfo resourceMethod)
	{
		ArgumentNullException.ThrowIfNull(resourceMethod);
		if (resourceMethod.GetCustomAttribute<McpSourceToolAttribute>(false) is not { } declared)
		{
			return null;
		}

		string name = declared.ToolName;
		MethodInfo[] candidates =
		[
			.. declared.ToolType.GetMethods(ToolMethods).Where(method =>
				string.Equals(method.GetCustomAttribute<McpServerToolAttribute>(false)?.Name, name,
					StringComparison.Ordinal))
		];
		MethodInfo? tool = candidates.Length == 1 ? candidates[0] : null;
		McpServerToolAttribute? attribute = tool?.GetCustomAttribute<McpServerToolAttribute>(false);
		McpFeature[] requires = tool is null
			? []
			:
			[
				.. tool.GetCustomAttributes<RequiresFeatureAttribute>(false)
					.Select(static requirement => requirement.Feature).Distinct().Order()
			];
		return new McpResourceSource(name, declared.ToolType, tool, requires, attribute?.ReadOnly ?? false,
			attribute?.OpenWorld ?? true, tool is null ? null : DispatchClassOf(tool));
	}

	/// <summary>Reads the resolved source a created resource carries.</summary>
	/// <param name="resource">A resource created by <c>WithCheatEnginePrimitives</c>.</param>
	/// <returns>The source, or <see langword="null" /> when the resource declares none.</returns>
	internal static McpResourceSource? Of(McpServerResource resource)
	{
		ArgumentNullException.ThrowIfNull(resource);
		return resource.Metadata.OfType<McpResourceSource>().FirstOrDefault();
	}

	/// <summary>The seed of the resource's <c>_meta</c>: the source tool name under the published key.</summary>
	/// <param name="source">The resolved source, if any.</param>
	/// <returns>The seed, or <see langword="null" /> without a source.</returns>
	internal static JsonObject? CreateMeta(McpResourceSource? source)
	{
		return source is null ? null : new JsonObject { [McpSourceToolAttribute.MetaKey] = source.ToolName };
	}

	private static string? DispatchClassOf(MethodInfo tool)
	{
		foreach (McpMetaAttribute meta in tool.GetCustomAttributes<McpMetaAttribute>(false))
		{
			if (!string.Equals(meta.Name, McpDispatchClass.MetaKey, StringComparison.Ordinal) ||
				string.IsNullOrWhiteSpace(meta.JsonValue))
			{
				continue;
			}

			try
			{
				return JsonNode.Parse(meta.JsonValue) is JsonValue value && value.TryGetValue(out string? text)
					? text
					: null;
			}
			catch (JsonException)
			{
				return null;
			}
		}

		return null;
	}
}
