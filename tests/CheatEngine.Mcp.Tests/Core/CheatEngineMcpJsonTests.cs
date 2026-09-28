using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

using CheatEngine.Mcp.Core.Contract;
using CheatEngine.Mcp.Tests.Support;

using Microsoft.Extensions.AI;

using ModelContextProtocol;
using ModelContextProtocol.Protocol;

namespace CheatEngine.Mcp.Tests.Core;

/// <summary>The composed serializer options: resolver order and generated metadata only.</summary>
public sealed class CheatEngineMcpJsonTests
{
	private static readonly CheatEngineMcpPrimitiveOptions Manifest = TestMcpPipeline.ProbeManifest;

	[Fact]
	public void Options_ContainNoReflectionResolver()
	{
		JsonSerializerOptions options = CheatEngineMcpJson.CreateOptions(Manifest);

		IJsonTypeInfoResolver[] resolvers = CheatEngineMcpJson.Flatten(options.TypeInfoResolverChain).ToArray();
		Assert.DoesNotContain(resolvers, static resolver => resolver is DefaultJsonTypeInfoResolver);
		Assert.DoesNotContain(options.Converters, static converter => converter is JsonStringEnumConverter);
		Assert.True(options.IsReadOnly);
	}

	[Fact]
	public void DefaultMcpOptions_NestTheReflectionResolverBelowTheTopLevel()
	{
		// Flattening is required because a top-level scan of the SDK chain never sees the reflection resolver.
		Assert.DoesNotContain(McpJsonUtilities.DefaultOptions.TypeInfoResolverChain,
			static resolver => resolver is DefaultJsonTypeInfoResolver);
		Assert.Contains(CheatEngineMcpJson.Flatten(McpJsonUtilities.DefaultOptions.TypeInfoResolverChain),
			static resolver => resolver is DefaultJsonTypeInfoResolver);
	}

	[Fact]
	public void Options_UnregisteredType_IsNotResolved()
	{
		JsonSerializerOptions options = CheatEngineMcpJson.CreateOptions(Manifest);

		Assert.False(options.TryGetTypeInfo(typeof(UnregisteredProbe), out _));
	}

	[Fact]
	public void Options_ContractManifestAndSdkCatalogTypes_AreResolved()
	{
		JsonSerializerOptions options = CheatEngineMcpJson.CreateOptions(Manifest);

		Assert.True(options.TryGetTypeInfo(typeof(ToolErrorEnvelope), out _));
		Assert.True(options.TryGetTypeInfo(typeof(ContractProbeResult), out _));
		Assert.True(options.TryGetTypeInfo(typeof(CallToolResult), out _));
		Assert.True(options.TryGetTypeInfo(typeof(ChatOptions), out _));
		Assert.True(options.TryGetTypeInfo(typeof(Tool), out _));
		Assert.True(options.TryGetTypeInfo(typeof(Prompt), out _));
		Assert.True(options.TryGetTypeInfo(typeof(Resource), out _));
		Assert.True(options.TryGetTypeInfo(typeof(ResourceTemplate), out _));
	}

	[Fact]
	public void Options_ResolverChain_StartsWithCoreThenTheManifest()
	{
		JsonSerializerOptions options = CheatEngineMcpJson.CreateOptions(Manifest);

		Assert.Same(CoreJsonContext.Default, options.TypeInfoResolverChain[0]);
		Assert.Same(TestJsonContext.Default, options.TypeInfoResolverChain[1]);
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
	public void Catalog_DescribesRegisteredRecords()
	{
		string description = JsonSerializer.Serialize(McpPrimitiveCatalog.Create(Manifest).Tools,
			McpJsonUtilities.DefaultOptions.GetTypeInfo<Tool[]>());

		Assert.Contains(ContractProbeTool.ConvertName, description, StringComparison.Ordinal);
	}

	/// <summary>A type no resolver of the manifest knows.</summary>
	/// <param name="Value">A value.</param>
	public sealed record UnregisteredProbe(int Value);
}
