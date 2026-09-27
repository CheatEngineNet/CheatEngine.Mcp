using CheatEngine.Mcp.Hosting.Configuration;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CheatEngine.Mcp.Plugin.Logging;

/// <summary>The plugin log settings, read once while Cheat Engine configures the activation.</summary>
internal sealed class PluginLoggingOptions
{
	/// <summary>The configuration section of the plugin log settings.</summary>
	internal const string SectionName = McpBackendOptions.SectionName + ":Logging";

	/// <summary>
	///     The minimum level written to the plugin log, <see cref="LogLevel.Information" /> by default.
	///     <see cref="LogLevel.Trace" /> can log MCP tool arguments and results; transport and protocol categories never
	///     log below <see cref="LogLevel.Information" />.
	/// </summary>
	public LogLevel MinimumLevel
	{
		get;
		set;
	} = LogLevel.Information;

	/// <summary>Binds the <c>Mcp:Logging</c> section; an absent key keeps the default level.</summary>
	/// <param name="configuration">The activation configuration.</param>
	/// <returns>The settings.</returns>
	/// <exception cref="OptionsValidationException">The level is a number that names no level.</exception>
	/// <exception cref="InvalidOperationException">The level is not a level name or number.</exception>
	internal static PluginLoggingOptions Read(IConfiguration configuration)
	{
		ArgumentNullException.ThrowIfNull(configuration);
		PluginLoggingOptions options =
			configuration.GetSection(SectionName).Get<PluginLoggingOptions>() ?? new PluginLoggingOptions();
		return Enum.IsDefined(options.MinimumLevel)
			? options
			: throw new OptionsValidationException(Options.DefaultName, typeof(PluginLoggingOptions),
				["Mcp:Logging:MinimumLevel must be Trace, Debug, Information, Warning, Error, Critical or None."]);
	}
}
