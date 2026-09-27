using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization.Metadata;

using CheatEngine.Mcp.Core.Execution;
using CheatEngine.Mcp.Core.Features;
using CheatEngine.Mcp.Core.Jobs;
using CheatEngine.Mcp.Core.Targets;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace CheatEngine.Mcp.Core.Composition;

/// <summary>Declares primitive types on a CheatEngine MCP composition.</summary>
public static class CheatEngineMcpBuilderExtensions
{
	extension(ICheatEngineMcpBuilder builder)
	{
		/// <summary>
		///     Registers the source-generated JSON metadata of a primitive project's argument and result types, in both
		///     modes, so the backend and the catalog serialize and describe them identically without reflection.
		///     Registering the same resolver instance again is ignored.
		/// </summary>
		/// <param name="resolver">The resolver, usually the project's <c>JsonSerializerContext.Default</c>.</param>
		/// <returns>The same builder.</returns>
		public ICheatEngineMcpBuilder AddJsonTypeInfoResolver(IJsonTypeInfoResolver resolver)
		{
			ArgumentNullException.ThrowIfNull(builder);
			ArgumentNullException.ThrowIfNull(resolver);
			builder.Services.Configure<CheatEngineMcpPrimitiveOptions>(options => options.AddJsonResolver(resolver));
			return builder;
		}

		/// <summary>
		///     Declares the <c>initialize</c> instructions, in both modes: the gateway advertises <paramref name="gateway" />
		///     and every backend <paramref name="backend" />. A composition declares them once; a second declaration fails
		///     validation when the manifest is resolved.
		/// </summary>
		/// <param name="gateway">The gateway's instructions, which route every call through <c>instance_list</c>.</param>
		/// <param name="backend">The instructions of a backend serving one instance.</param>
		/// <returns>The same builder.</returns>
		public ICheatEngineMcpBuilder SetServerInstructions(string gateway, string backend)
		{
			ArgumentNullException.ThrowIfNull(builder);
			ArgumentException.ThrowIfNullOrWhiteSpace(gateway);
			ArgumentException.ThrowIfNullOrWhiteSpace(backend);
			builder.Services.Configure<CheatEngineMcpPrimitiveOptions>(options =>
				options.SetServerInstructions(gateway, backend));
			builder.Services.TryAddEnumerable(ServiceDescriptor
				.Singleton<IValidateOptions<CheatEngineMcpPrimitiveOptions>, CheatEngineMcpManifestValidator>());
			return builder;
		}

		/// <summary>
		///     Registers the activation-scoped execution services that v2 primitives inject: <see cref="ToolDispatch" />,
		///     <see cref="DispatchStatistics" /> and <see cref="McpFeatureGate" />, the validated
		///     <see cref="McpExecutionOptions" /> and the system clock, then the target services: the Lua
		///     <see cref="McpStateLedger" />, <see cref="TargetResources" /> (also registered as an
		///     <see cref="ITargetTransitionGuard" />), <see cref="TargetTransitionGuards" /> over every registered guard, and
		///     <see cref="JobRegistry" />. It does nothing in <see cref="CheatEngineMcpMode.Catalog" /> mode, which never
		///     constructs a primitive, and repeating it is harmless.
		/// </summary>
		/// <remarks>
		///     The composition root binds <see cref="McpExecutionOptions" /> from <see cref="McpExecutionOptions.SectionName" />
		///     and registers the one <see cref="McpFeatureOptions" /> instance that also drove the Client opt-ins; without
		///     them the defaults apply. The options are validated when the activation first resolves them, which a
		///     primitive's constructor does while the activation is built, so invalid limits fail the enable.
		/// </remarks>
		/// <returns>The same builder.</returns>
		public ICheatEngineMcpBuilder AddExecutionServices()
		{
			ArgumentNullException.ThrowIfNull(builder);
			if (builder.Mode is CheatEngineMcpMode.Backend)
			{
				builder.Services.AddOptions<McpExecutionOptions>();
				builder.Services.AddOptions<McpFeatureOptions>();
				builder.Services.TryAddEnumerable(ServiceDescriptor
					.Singleton<IValidateOptions<McpExecutionOptions>, McpExecutionOptionsValidator>());
				builder.Services.TryAddSingleton(TimeProvider.System);
				builder.Services.TryAddScoped<McpFeatureGate>();
				builder.Services.TryAddScoped<DispatchStatistics>();
				builder.Services.TryAddScoped<ToolDispatch>();
				builder.Services.TryAddScoped<McpStateLedger>();
				builder.Services.TryAddScoped<TargetResources>();
				builder.Services.TryAddEnumerable(
					ServiceDescriptor.Scoped<ITargetTransitionGuard, TargetResources>(static services =>
						services.GetRequiredService<TargetResources>()));
				builder.Services.TryAddScoped<TargetTransitionGuards>();
				builder.Services.TryAddScoped<JobRegistry>();
			}

			return builder;
		}

		/// <summary>Declares a container type whose <c>McpServerTool</c> methods become MCP tools.</summary>
		/// <typeparam name="TTool">The tool container type.</typeparam>
		/// <returns>The same builder.</returns>
		public ICheatEngineMcpBuilder AddToolType<[DynamicallyAccessedMembers(CheatEngineMcpPrimitive.Members)] TTool>()
			where TTool : class
		{
			return builder.AddPrimitive(CheatEngineMcpPrimitiveKind.Tool, typeof(TTool));
		}

		/// <summary>Declares a container type whose <c>McpServerResource</c> methods become MCP resources.</summary>
		/// <typeparam name="TResource">The resource container type.</typeparam>
		/// <returns>The same builder.</returns>
		public ICheatEngineMcpBuilder AddResourceType<
			[DynamicallyAccessedMembers(CheatEngineMcpPrimitive.Members)]
			TResource>()
			where TResource : class
		{
			return builder.AddPrimitive(CheatEngineMcpPrimitiveKind.Resource, typeof(TResource));
		}

		/// <summary>Declares a container type whose <c>McpServerPrompt</c> methods become MCP prompts.</summary>
		/// <typeparam name="TPrompt">The prompt container type.</typeparam>
		/// <returns>The same builder.</returns>
		public ICheatEngineMcpBuilder AddPromptType<
			[DynamicallyAccessedMembers(CheatEngineMcpPrimitive.Members)]
			TPrompt>()
			where TPrompt : class
		{
			return builder.AddPrimitive(CheatEngineMcpPrimitiveKind.Prompt, typeof(TPrompt));
		}

		private ICheatEngineMcpBuilder AddPrimitive(CheatEngineMcpPrimitiveKind kind,
			[DynamicallyAccessedMembers(CheatEngineMcpPrimitive.Members)]
			Type type)
		{
			ArgumentNullException.ThrowIfNull(builder);
			if (builder.Mode is CheatEngineMcpMode.Backend)
			{
				// One instance per activation scope, validated by Client Hosting and disposed after its lease drain.
				builder.Services.TryAddScoped(type);
			}

			CheatEngineMcpPrimitive primitive = new(kind, type);
			builder.Services.Configure<CheatEngineMcpPrimitiveOptions>(options => options.Add(primitive));
			return builder;
		}
	}
}
