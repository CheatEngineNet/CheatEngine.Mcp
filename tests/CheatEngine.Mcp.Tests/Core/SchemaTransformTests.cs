using System.Globalization;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;

using Microsoft.Extensions.AI;

using Xunit.Sdk;

namespace CheatEngine.Mcp.Tests.Core;

public sealed class SchemaTransformTests
{
	[Fact]
	public void SchemaTransform_CollapsesNullableTypeArrays()
	{
		MethodInfo method = GetSampleMethod(nameof(SchemaSamples.NullableParameters));

		JsonNode untransformed = CreateFunctionSchema(method, AIJsonSchemaCreateOptions.Default);
		AssertContainsNullableTypeArray(untransformed);

		JsonNode transformed = CreateFunctionSchema(method, SchemaTransform.SchemaCreateOptions);
		AssertNoNullableTypeArrays(transformed);
	}

	[Fact]
	public void SchemaTransform_RemovesOversizedNumericKeywords()
	{
		MethodInfo method = GetSampleMethod(nameof(SchemaSamples.OversizedDefault));

		string untransformed = CreateFunctionSchema(method, AIJsonSchemaCreateOptions.Default).ToJsonString();
		Assert.Contains(ulong.MaxValue.ToString(CultureInfo.InvariantCulture), untransformed);

		string transformed = CreateFunctionSchema(method, SchemaTransform.SchemaCreateOptions).ToJsonString();
		Assert.False(
			transformed.Contains(ulong.MaxValue.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal),
			"Schema still contains ulong.MaxValue, which breaks signed-64-bit schema consumers.");
	}

	private static MethodInfo GetSampleMethod(string name)
	{
		return typeof(SchemaSamples).GetMethod(name, BindingFlags.Public | BindingFlags.Static)
		       ?? throw new InvalidOperationException($"Missing sample method {name}.");
	}

	private static JsonNode CreateFunctionSchema(MethodInfo method, AIJsonSchemaCreateOptions options)
	{
		JsonElement schema = AIJsonUtilities.CreateFunctionJsonSchema(
			method,
			method.Name,
			"test schema",
			null,
			options);

		return JsonNode.Parse(schema.GetRawText())
		       ?? throw new InvalidOperationException("Generated schema was empty.");
	}

	private static void AssertContainsNullableTypeArray(JsonNode? node)
	{
		if (ContainsNullableTypeArray(node))
		{
			return;
		}

		throw new XunitException("Expected the untransformed schema to contain a nullable JSON Schema type array.");
	}

	private static void AssertNoNullableTypeArrays(JsonNode? node)
	{
		if (node is JsonObject obj)
		{
			if (obj["type"] is JsonArray typeArray)
			{
				bool hasNull = typeArray.Any(member => member?.GetValue<string>() == "null");
				int nonNullCount = typeArray.Count(member => member?.GetValue<string>() != "null");

				Assert.False(
					hasNull && nonNullCount == 1,
					$"Found nullable type array that should have been collapsed: {typeArray.ToJsonString()}");
			}

			foreach (KeyValuePair<string, JsonNode?> property in obj)
			{
				AssertNoNullableTypeArrays(property.Value);
			}
		}
		else if (node is JsonArray array)
		{
			foreach (JsonNode? item in array)
			{
				AssertNoNullableTypeArrays(item);
			}
		}
	}

	private static bool ContainsNullableTypeArray(JsonNode? node)
	{
		if (node is JsonObject obj)
		{
			if (obj["type"] is JsonArray typeArray)
			{
				bool hasNull = typeArray.Any(member => member?.GetValue<string>() == "null");
				int nonNullCount = typeArray.Count(member => member?.GetValue<string>() != "null");

				if (hasNull && nonNullCount == 1)
				{
					return true;
				}
			}

			return obj.Any(property => ContainsNullableTypeArray(property.Value));
		}

		return node is JsonArray array && array.Any(ContainsNullableTypeArray);
	}

	private static class SchemaSamples
	{
		public static void NullableParameters(string? text = null, int? count = null, bool? enabled = null)
		{
		}

		public static void OversizedDefault(ulong stopAddress = ulong.MaxValue)
		{
		}
	}
}
