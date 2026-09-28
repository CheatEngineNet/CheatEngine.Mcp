using System.Collections;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

using CheatEngine.Mcp.Tests.Support;

using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace CheatEngine.Mcp.Tests.Contract;

/// <summary>
///     Results omit null members (<c>WhenWritingNull</c>), and clients such as the TypeScript SDK validate
///     <c>structuredContent</c> against the output schema: a nullable member must therefore never be required.
/// </summary>
public sealed class OutputSchemaContractTests
{
	private static readonly NullabilityInfoContext Nullability = new();

	[Fact]
	public void ToolOutputSchemas_NeverRequireANullableMember()
	{
		McpPrimitiveCatalog catalog = McpPrimitiveCatalog.Create(TestComposition.BackendManifest);
		Dictionary<string, MethodInfo> methods = ToolMethods();
		List<string> violations = [];
		int checkedTools = 0;
		foreach (Tool tool in catalog.Tools)
		{
			if (tool.OutputSchema is not { } outputSchema || !methods.TryGetValue(tool.Name, out MethodInfo? method))
			{
				continue;
			}

			checkedTools++;
			JsonObject schema = JsonNode.Parse(outputSchema.GetRawText())!.AsObject();
			Walk(tool.Name, ResultType(method.ReturnType), schema, violations, new HashSet<Type>());
		}

		Assert.Empty(violations);
		Assert.True(checkedTools > 0 || methods.Count == 0, "No v2 tool output schema was checked.");
	}

	private static Dictionary<string, MethodInfo> ToolMethods()
	{
		Dictionary<string, MethodInfo> methods = new(StringComparer.Ordinal);
		foreach (Type type in TestComposition.BackendManifest.Primitives
					 .Where(static primitive => primitive.Kind == CheatEngineMcpPrimitiveKind.Tool)
					 .Select(static primitive => primitive.Type))
		{
			foreach (MethodInfo method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance |
														  BindingFlags.Static | BindingFlags.DeclaredOnly))
			{
				if (method.GetCustomAttribute<McpServerToolAttribute>() is { Name: { } name })
				{
					methods[name] = method;
				}
			}
		}

		return methods;
	}

	private static Type ResultType(Type type)
	{
		if (type.IsGenericType && (type.GetGenericTypeDefinition() == typeof(Task<>) ||
								   type.GetGenericTypeDefinition() == typeof(ValueTask<>)))
		{
			return type.GetGenericArguments()[0];
		}

		return type;
	}

	private static void Walk(string path, Type type, JsonObject schema, List<string> violations, HashSet<Type> seen)
	{
		type = Nullable.GetUnderlyingType(type) ?? type;
		if (schema.ContainsKey("$ref"))
		{
			return;
		}

		if (ElementType(type) is { } element)
		{
			if (schema["items"] is JsonObject items)
			{
				Walk(path + "[]", element, items, violations, seen);
			}

			return;
		}

		if (!IsObjectContract(type) || !seen.Add(type) || schema["properties"] is not JsonObject properties)
		{
			return;
		}

		HashSet<string> required = schema["required"] is JsonArray list
			? list.Select(static member => member!.GetValue<string>()).ToHashSet(StringComparer.Ordinal)
			: [];
		foreach (PropertyInfo property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
		{
			if (property.GetCustomAttribute<JsonIgnoreAttribute>() is { Condition: JsonIgnoreCondition.Always })
			{
				continue;
			}

			string name = property.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name ??
						  JsonNamingPolicy.CamelCase.ConvertName(property.Name);
			bool nullable = Nullable.GetUnderlyingType(property.PropertyType) is not null ||
							Nullability.Create(property).ReadState == NullabilityState.Nullable;
			if (nullable && required.Contains(name))
			{
				violations.Add($"{path}.{name} is nullable but required");
			}

			if (properties[name] is JsonObject child)
			{
				Walk($"{path}.{name}", property.PropertyType, child, violations, seen);
			}
		}

		seen.Remove(type);
	}

	private static Type? ElementType(Type type)
	{
		if (type == typeof(string))
		{
			return null;
		}

		if (type.IsArray)
		{
			return type.GetElementType();
		}

		return type.IsGenericType && typeof(IEnumerable).IsAssignableFrom(type) &&
			   !typeof(IDictionary).IsAssignableFrom(type) && type.GetGenericArguments().Length == 1
			? type.GetGenericArguments()[0]
			: null;
	}

	private static bool IsObjectContract(Type type)
	{
		return type is { IsPrimitive: false, IsEnum: false } && type != typeof(string) && type != typeof(decimal) &&
			   type != typeof(object) && (type.IsClass || type.IsValueType) &&
			   type.Namespace?.StartsWith("CheatEngine.Mcp", StringComparison.Ordinal) == true;
	}
}
