using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CheatEngine.Mcp.Tools.Symbol;

/// <summary>The composition entry point of the <c>symbol</c> tool domain (10 tools in the v2 catalog).</summary>
public static class SymbolToolsBuilderExtensions
{
	extension(ICheatEngineMcpBuilder builder)
	{
		/// <summary>
		///     Declares the <c>symbol_*</c> tool containers and their JSON metadata: <see cref="SymbolTools" /> (resolve,
		///     find, module preference, reload, add module, enable sources) and <see cref="SymbolRegistrationTools" />
		///     (register, unregister, list registered), with the activation's <see cref="SymbolRegistrations" /> in backend
		///     mode.
		/// </summary>
		/// <returns>The same builder.</returns>
		public ICheatEngineMcpBuilder AddSymbolTools()
		{
			ArgumentNullException.ThrowIfNull(builder);
			if (builder.Mode is CheatEngineMcpMode.Backend)
			{
				builder.Services.TryAddScoped<SymbolRegistrations>();
			}

			return builder
				.AddJsonTypeInfoResolver(SymbolJsonContext.Default)
				.AddToolType<SymbolTools>()
				.AddToolType<SymbolRegistrationTools>();
		}
	}
}
