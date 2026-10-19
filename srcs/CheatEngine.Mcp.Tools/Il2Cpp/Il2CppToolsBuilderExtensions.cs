using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CheatEngine.Mcp.Tools.Il2Cpp;

/// <summary>Composition entry point for the activation-local offline IL2CPP dump tools.</summary>
public static class Il2CppToolsBuilderExtensions
{
	extension(ICheatEngineMcpBuilder builder)
	{
		/// <summary>Registers immutable dump storage and the <see cref="Il2CppTools" /> container.</summary>
		public ICheatEngineMcpBuilder AddIl2CppTools()
		{
			ArgumentNullException.ThrowIfNull(builder);
			if (builder.Mode is CheatEngineMcpMode.Backend)
			{
				builder.Services.TryAddScoped<Il2CppDumpStore>();
			}

			return builder.AddJsonTypeInfoResolver(Il2CppJsonContext.Default).AddToolType<Il2CppTools>();
		}
	}
}
