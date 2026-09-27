using System.Diagnostics.CodeAnalysis;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CheatEngine.Mcp.Core.Composition;

/// <summary>Declares primitive types on a CheatEngine MCP composition.</summary>
public static class CheatEngineMcpBuilderExtensions
{
	extension(ICheatEngineMcpBuilder builder)
	{
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
