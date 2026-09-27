using System.Reflection;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

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
		///     Registers every declared primitive with strict-client-compatible schemas, and fails startup on duplicate
		///     identifiers. Live targets are borrowed through the non-disposing <c>Create(MethodInfo, object)</c> overloads, so
		///     the MCP host never owns, constructs or disposes a primitive instance.
		/// </summary>
		/// <param name="manifest">The primitives a composition declared.</param>
		/// <param name="binding">Where instance targets come from.</param>
		/// <returns>The same builder.</returns>
		public IMcpServerBuilder WithCheatEnginePrimitives(CheatEngineMcpPrimitiveOptions manifest,
			McpPrimitiveBinding binding)
		{
			ArgumentNullException.ThrowIfNull(server);
			ArgumentNullException.ThrowIfNull(manifest);
			ArgumentNullException.ThrowIfNull(binding);
			foreach (CheatEngineMcpPrimitive primitive in manifest.Primitives)
			{
				foreach (MethodInfo method in primitive.Type.GetMethods(PrimitiveMethods))
				{
					Register(server.Services, primitive, method, binding);
				}
			}

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
		McpPrimitiveBinding binding)
	{
		switch (primitive.Kind)
		{
			case CheatEngineMcpPrimitiveKind.Tool when method.GetCustomAttribute<McpServerToolAttribute>() is not null:
				services.AddSingleton(provider => CreateTool(provider, primitive.Type, method, binding));
				break;
			case CheatEngineMcpPrimitiveKind.Resource
				when method.GetCustomAttribute<McpServerResourceAttribute>() is not null:
				services.AddSingleton(provider => CreateResource(provider, primitive.Type, method, binding));
				break;
			case CheatEngineMcpPrimitiveKind.Prompt
				when method.GetCustomAttribute<McpServerPromptAttribute>() is not null:
				services.AddSingleton(provider => CreatePrompt(provider, primitive.Type, method, binding));
				break;
		}
	}

	private static McpServerTool CreateTool(IServiceProvider services, Type type, MethodInfo method,
		McpPrimitiveBinding binding)
	{
		McpServerToolCreateOptions options = new()
		{
			Services = services, SchemaCreateOptions = SchemaTransform.SchemaCreateOptions
		};
		return method.IsStatic || binding.Targets is not null
			? McpServerTool.Create(method, Target(binding, type, method), options)
			: McpServerTool.Create(method, static _ => throw SchemaOnly(), options);
	}

	private static McpServerResource CreateResource(IServiceProvider services, Type type, MethodInfo method,
		McpPrimitiveBinding binding)
	{
		McpServerResourceCreateOptions options = new()
		{
			Services = services, SchemaCreateOptions = SchemaTransform.SchemaCreateOptions
		};
		return method.IsStatic || binding.Targets is not null
			? McpServerResource.Create(method, Target(binding, type, method), options)
			: McpServerResource.Create(method, static _ => throw SchemaOnly(), options);
	}

	private static McpServerPrompt CreatePrompt(IServiceProvider services, Type type, MethodInfo method,
		McpPrimitiveBinding binding)
	{
		McpServerPromptCreateOptions options = new()
		{
			Services = services, SchemaCreateOptions = SchemaTransform.SchemaCreateOptions
		};
		return method.IsStatic || binding.Targets is not null
			? McpServerPrompt.Create(method, Target(binding, type, method), options)
			: McpServerPrompt.Create(method, static _ => throw SchemaOnly(), options);
	}

	// The instance overloads borrow the activation-owned target; the factory overloads would dispose it after each call.
	private static object? Target(McpPrimitiveBinding binding, Type type, MethodInfo method)
	{
		return method.IsStatic ? null : binding.Targets!.Get(type);
	}

	private static InvalidOperationException SchemaOnly()
	{
		return new InvalidOperationException("Schema-only primitives cannot be invoked.");
	}
}
