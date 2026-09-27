using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

using CheatEngine.Mcp.Core.Features;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace CheatEngine.Mcp.Core.Composition;

/// <summary>Registers a CheatEngine primitive manifest in an MCP server.</summary>
public static class CheatEngineMcpServerBuilderExtensions
{
	private const BindingFlags PrimitiveMethods =
		BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;

	extension(IMcpServerBuilder server)
	{
		/// <summary>
		///     Registers every declared primitive with strict-client-compatible schemas and the composition's serializer
		///     options, adds the Core request filters (error envelope, feature gates, argument repair, resource errors), and
		///     fails startup on duplicate identifiers or contract violations. Live targets are borrowed through the
		///     non-disposing <c>Create(MethodInfo, object)</c> overloads, so the MCP host never owns, constructs or disposes a
		///     primitive instance; a gated tool's requirements are listed in its <c>_meta</c> under
		///     <see cref="McpFeatureGate.RequiresMetaKey" />.
		/// </summary>
		/// <param name="manifest">The primitives a composition declared.</param>
		/// <param name="binding">Where instance targets come from.</param>
		/// <param name="strictJson">
		///     Whether the serializer options keep only source-generated metadata; see
		///     <see cref="CheatEngineMcpJson.CreateOptions" />. Off while legacy tools remain.
		/// </param>
		/// <returns>The same builder.</returns>
		public IMcpServerBuilder WithCheatEnginePrimitives(CheatEngineMcpPrimitiveOptions manifest,
			McpPrimitiveBinding binding, bool strictJson = CheatEngineMcpJson.StrictByDefault)
		{
			ArgumentNullException.ThrowIfNull(server);
			ArgumentNullException.ThrowIfNull(manifest);
			ArgumentNullException.ThrowIfNull(binding);
			// Built once per server: schemas, argument binding and results all use the same options.
			JsonSerializerOptions json = CheatEngineMcpJson.CreateOptions(manifest, strictJson);
			foreach (CheatEngineMcpPrimitive primitive in manifest.Primitives)
			{
				foreach (MethodInfo method in primitive.Type.GetMethods(PrimitiveMethods))
				{
					Register(server.Services, primitive, method, binding, json);
				}
			}

			// The first filter added is the outermost: MapErrors also sees failures of the filters inside it, and a
			// disabled feature is refused before arguments are repaired or bound.
			server.WithRequestFilters(filters =>
			{
				filters.AddCallToolFilter(CheatEngineToolFilters.MapErrors);
				filters.AddCallToolFilter(next => CheatEngineToolFilters.EnforceFeatures(next, binding));
				filters.AddCallToolFilter(CheatEngineToolFilters.NormalizeArguments);
				filters.AddReadResourceFilter(CheatEngineToolFilters.MapResourceErrors);
				filters.AddGetPromptFilter(CheatEngineToolFilters.MapPromptErrors);
			});
			server.Services.AddOptions<McpServerOptions>().ValidateOnStart();
			server.Services.TryAddEnumerable(ServiceDescriptor
				.Singleton<IValidateOptions<McpServerOptions>, McpPrimitiveValidator>());
			return server;
		}
	}

	internal static bool IsPrimitiveMethod(MethodInfo method)
	{
		return method.GetCustomAttribute<McpServerToolAttribute>() is not null
			   || method.GetCustomAttribute<McpServerResourceAttribute>() is not null
			   || method.GetCustomAttribute<McpServerPromptAttribute>() is not null;
	}

	private static void Register(IServiceCollection services, CheatEngineMcpPrimitive primitive, MethodInfo method,
		McpPrimitiveBinding binding, JsonSerializerOptions json)
	{
		switch (primitive.Kind)
		{
			case CheatEngineMcpPrimitiveKind.Tool when method.GetCustomAttribute<McpServerToolAttribute>() is not null:
				services.AddSingleton(provider => CreateTool(provider, primitive.Type, method, binding, json));
				break;
			case CheatEngineMcpPrimitiveKind.Resource
				when method.GetCustomAttribute<McpServerResourceAttribute>() is not null:
				services.AddSingleton(provider => CreateResource(provider, primitive.Type, method, binding, json));
				break;
			case CheatEngineMcpPrimitiveKind.Prompt
				when method.GetCustomAttribute<McpServerPromptAttribute>() is not null:
				services.AddSingleton(provider => CreatePrompt(provider, primitive.Type, method, binding, json));
				break;
		}
	}

	private static McpServerTool CreateTool(IServiceProvider services, Type type, MethodInfo method,
		McpPrimitiveBinding binding, JsonSerializerOptions json)
	{
		McpServerToolCreateOptions options = new()
		{
			Services = services,
			SerializerOptions = json,
			SchemaCreateOptions = SchemaTransform.SchemaCreateOptions,
			Meta = CreateToolMeta(method)
		};
		return method.IsStatic || binding.Targets is not null
			? McpServerTool.Create(method, Target(binding, type, method), options)
			: McpServerTool.Create(method, static _ => throw SchemaOnly(), options);
	}

	/// <summary>Lists a tool's declared feature requirements under <see cref="McpFeatureGate.RequiresMetaKey" />.</summary>
	/// <param name="method">The tool method.</param>
	/// <returns>The seed of the tool's <c>_meta</c>, or <see langword="null" /> for an ungated tool.</returns>
	internal static JsonObject? CreateToolMeta(MethodInfo method)
	{
		JsonNode?[] requires =
		[
			.. method.GetCustomAttributes<RequiresFeatureAttribute>(false)
				.Select(static requirement => requirement.Feature).Distinct().Order()
				.Select(static feature => (JsonNode?) McpFeatureGate.ContractName(feature))
		];
		return requires.Length == 0
			? null
			: new JsonObject { [McpFeatureGate.RequiresMetaKey] = new JsonArray(requires) };
	}

	/// <summary>
	///     Creates a resource with the composition's options and its routing marker. A Local concrete resource (a static
	///     method without parameters) is pure by contract, so it is read once here to publish its <c>size</c>.
	/// </summary>
	internal static McpServerResource CreateResource(IServiceProvider services, Type type, MethodInfo method,
		McpPrimitiveBinding binding, JsonSerializerOptions json)
	{
		McpServerResourceCreateOptions options = new()
		{
			Services = services,
			SerializerOptions = json,
			SchemaCreateOptions = SchemaTransform.SchemaCreateOptions,
			Metadata = McpPrimitiveOrigin.CreateMetadata(CheatEngineMcpPrimitiveKind.Resource, method)
		};
		McpServerResource resource = method.IsStatic || binding.Targets is not null
			? McpServerResource.Create(method, Target(binding, type, method), options)
			: McpServerResource.Create(method, static _ => throw SchemaOnly(), options);
		PublishSize(resource, method);
		return resource;
	}

	/// <summary>Creates a prompt with the composition's options and its routing marker.</summary>
	internal static McpServerPrompt CreatePrompt(IServiceProvider services, Type type, MethodInfo method,
		McpPrimitiveBinding binding, JsonSerializerOptions json)
	{
		McpServerPromptCreateOptions options = new()
		{
			Services = services,
			SerializerOptions = json,
			SchemaCreateOptions = SchemaTransform.SchemaCreateOptions,
			Metadata = McpPrimitiveOrigin.CreateMetadata(CheatEngineMcpPrimitiveKind.Prompt, method)
		};
		return method.IsStatic || binding.Targets is not null
			? McpServerPrompt.Create(method, Target(binding, type, method), options)
			: McpServerPrompt.Create(method, static _ => throw SchemaOnly(), options);
	}

	/// <summary>
	///     Publishes the UTF-8 size of a Local concrete resource. Only a static method without parameters qualifies; its
	///     result must be a string or a <see cref="ReadResourceResult" /> of text or blob contents.
	/// </summary>
	private static void PublishSize(McpServerResource resource, MethodInfo method)
	{
		if (!method.IsStatic || resource.IsTemplated || method.GetParameters().Length != 0 ||
			resource.ProtocolResource is not { Size: null } protocol)
		{
			return;
		}

		protocol.Size = method.Invoke(null, BindingFlags.DoNotWrapExceptions, null, null, null) switch
		{
			string text => Encoding.UTF8.GetByteCount(text),
			ReadResourceResult result => result.Contents.Sum(static contents => contents switch
			{
				TextResourceContents text => Encoding.UTF8.GetByteCount(text.Text),
				BlobResourceContents blob => (long) blob.DecodedData.Length,
				_ => 0L
			}),
			_ => null
		};
	}

	// The instance overloads borrow the activation-owned target; the factory overloads would dispose it after each call.
	internal static object? Target(McpPrimitiveBinding binding, Type type, MethodInfo method)
	{
		return method.IsStatic ? null : binding.Targets!.Get(type);
	}

	private static InvalidOperationException SchemaOnly()
	{
		return new InvalidOperationException("Schema-only primitives cannot be invoked.");
	}
}
