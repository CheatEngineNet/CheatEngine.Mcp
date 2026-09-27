using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

using CheatEngine.Mcp.Core.Contract;
using CheatEngine.Mcp.Tests.Support;
using CheatEngine.SDK.Engine.Enums;

using Microsoft.Extensions.AI;

using ModelContextProtocol;
using ModelContextProtocol.Protocol;

namespace CheatEngine.Mcp.Tests.Core;

/// <summary>The composed serializer options: resolver order, the legacy enum shim and the strict mode.</summary>
public sealed class CheatEngineMcpJsonTests
{
	private static readonly CheatEngineMcpPrimitiveOptions Manifest = TestMcpPipeline.ProbeManifest;

	[Fact]
	public void StrictOptions_ContainNoReflectionResolver()
	{
		JsonSerializerOptions options = CheatEngineMcpJson.CreateOptions(Manifest, true);

		IJsonTypeInfoResolver[] resolvers = CheatEngineMcpJson.Flatten(options.TypeInfoResolverChain).ToArray();
		Assert.DoesNotContain(resolvers, static resolver => resolver is DefaultJsonTypeInfoResolver);
		Assert.DoesNotContain(options.Converters, static converter => converter is JsonStringEnumConverter);
		Assert.DoesNotContain(options.Converters,
			static converter => converter.GetType().Name == "LegacyEnumConverterFactory");
		Assert.True(options.IsReadOnly);
	}

	[Fact]
	public void DefaultMcpOptions_NestTheReflectionResolverBelowTheTopLevel()
	{
		// Why strict mode must flatten: a top-level scan of the SDK chain never sees the reflection resolver.
		Assert.DoesNotContain(McpJsonUtilities.DefaultOptions.TypeInfoResolverChain,
			static resolver => resolver is DefaultJsonTypeInfoResolver);
		Assert.Contains(CheatEngineMcpJson.Flatten(McpJsonUtilities.DefaultOptions.TypeInfoResolverChain),
			static resolver => resolver is DefaultJsonTypeInfoResolver);
	}

	[Fact]
	public void StrictOptions_UnregisteredType_IsNotResolved()
	{
		JsonSerializerOptions options = CheatEngineMcpJson.CreateOptions(Manifest, true);

		Assert.True(JsonSerializer.IsReflectionEnabledByDefault, "The test must prove strictness with reflection on.");
		Assert.False(options.TryGetTypeInfo(typeof(UnregisteredProbe), out _));
		Assert.True(CheatEngineMcpJson.CreateOptions(Manifest, false)
			.TryGetTypeInfo(typeof(UnregisteredProbe), out _));
	}

	[Fact]
	public void StrictOptions_ContractManifestAndSdkTypes_AreResolved()
	{
		JsonSerializerOptions options = CheatEngineMcpJson.CreateOptions(Manifest, true);

		Assert.True(options.TryGetTypeInfo(typeof(ToolErrorEnvelope), out _));
		Assert.True(options.TryGetTypeInfo(typeof(ContractProbeResult), out _));
		Assert.True(options.TryGetTypeInfo(typeof(CallToolResult), out _));
		Assert.True(options.TryGetTypeInfo(typeof(ChatOptions), out _));
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public void Options_ResolverChain_StartsWithCoreThenTheManifest(bool strict)
	{
		JsonSerializerOptions options = CheatEngineMcpJson.CreateOptions(Manifest, strict);

		Assert.Same(CoreJsonContext.Default, options.TypeInfoResolverChain[0]);
		Assert.Same(TestJsonContext.Default, options.TypeInfoResolverChain[1]);
	}

	[Fact]
	public void LegacyOptions_ClientEnum_KeepsTheSchemaAndWireFormOfTheSdkDefaults()
	{
		JsonSerializerOptions options = CheatEngineMcpJson.CreateOptions(TestComposition.BackendManifest, false);

		JsonElement expected = AIJsonUtilities.CreateJsonSchema(typeof(VariableType),
			serializerOptions: McpJsonUtilities.DefaultOptions);
		JsonElement actual = AIJsonUtilities.CreateJsonSchema(typeof(VariableType), serializerOptions: options);
		Assert.True(JsonNode.DeepEquals(JsonNode.Parse(expected.GetRawText()), JsonNode.Parse(actual.GetRawText())),
			$"{expected.GetRawText()} != {actual.GetRawText()}");
		Assert.Equal(
			JsonSerializer.Serialize(VariableType.Dword, McpJsonUtilities.DefaultOptions.GetTypeInfo<VariableType>()),
			JsonSerializer.Serialize(VariableType.Dword, options.GetTypeInfo<VariableType>()));
		Assert.Equal(VariableType.Qword,
			JsonSerializer.Deserialize("\"Qword\"", options.GetTypeInfo<VariableType>()));
	}

	[Fact]
	public void LegacyOptions_ContractEnum_UsesItsOwnConverterInsteadOfTheCatchAll()
	{
		JsonSerializerOptions options = CheatEngineMcpJson.CreateOptions(TestComposition.BackendManifest, false);

		// The SDK defaults would write the reflection converter's PascalCase name.
		Assert.Equal("\"InvalidArgument\"", JsonSerializer.Serialize(ToolErrorKind.InvalidArgument,
			McpJsonUtilities.DefaultOptions.GetTypeInfo<ToolErrorKind>()));
		Assert.Equal("\"invalid_argument\"", JsonSerializer.Serialize(ToolErrorKind.InvalidArgument,
			options.GetTypeInfo<ToolErrorKind>()));
	}

	[Fact]
	public void AddJsonTypeInfoResolver_SameInstanceTwice_IsRecordedOnce()
	{
		CheatEngineMcpPrimitiveOptions manifest = CheatEngineMcpComposition.CreateManifest(CheatEngineMcpMode.Backend,
			static builder => builder.AddJsonTypeInfoResolver(TestJsonContext.Default)
				.AddJsonTypeInfoResolver(TestJsonContext.Default));

		Assert.Same(TestJsonContext.Default, Assert.Single(manifest.JsonResolvers));
	}

	[Fact]
	public void Catalog_StrictJson_DescribesRegisteredRecordsLikeTheDefaultMode()
	{
		static string Describe(bool strict)
		{
			return JsonSerializer.Serialize(McpPrimitiveCatalog.Create(Manifest, strict).Tools
					.Where(static tool => tool.OutputSchema is not null).ToArray(),
				McpJsonUtilities.DefaultOptions.GetTypeInfo<Tool[]>());
		}

		Assert.Equal(Describe(false), Describe(true));
	}

	/// <summary>A type no resolver of the manifest knows.</summary>
	/// <param name="Value">A value.</param>
	public sealed record UnregisteredProbe(int Value);
}
