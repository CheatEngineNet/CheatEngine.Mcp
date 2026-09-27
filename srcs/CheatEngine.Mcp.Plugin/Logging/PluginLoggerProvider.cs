using Microsoft.Extensions.Logging;

namespace CheatEngine.Mcp.Plugin.Logging;

/// <summary>Writes Microsoft.Extensions.Logging entries to the plugin log through one sink reference.</summary>
/// <remarks>
///     The provider is the lease on the sink: disposing it releases its reference, never blocks and never closes the
///     file for other holders; its loggers then drop entries silently. Transport and protocol categories never log below
///     <see cref="LogLevel.Information" />, whatever the configured level: their debug and trace events can carry request
///     headers, bearer tokens and MCP payloads.
/// </remarks>
internal sealed class PluginLoggerProvider : ILoggerProvider
{
	private readonly PluginLogReference _reference;

	/// <summary>Holds <paramref name="reference" /> until disposed.</summary>
	/// <param name="reference">The sink reference this provider owns.</param>
	/// <param name="minimumLevel">The configured minimum level.</param>
	internal PluginLoggerProvider(PluginLogReference reference, LogLevel minimumLevel)
	{
		ArgumentNullException.ThrowIfNull(reference);
		_reference = reference;
		MinimumLevel = minimumLevel;
	}

	/// <summary>The configured minimum level.</summary>
	internal LogLevel MinimumLevel
	{
		get;
	}

	/// <summary>The log file the provider writes.</summary>
	internal string FilePath => _reference.FilePath;

	/// <inheritdoc />
	public ILogger CreateLogger(string categoryName)
	{
		ArgumentNullException.ThrowIfNull(categoryName);
		return new PluginLogger(_reference, categoryName, MinimumLevelFor(categoryName, MinimumLevel));
	}

	/// <inheritdoc />
	public void Dispose()
	{
		_reference.Dispose();
	}

	/// <summary>The effective minimum level of a category: the configured one, floored for transport categories.</summary>
	/// <param name="category">The logger category.</param>
	/// <param name="configured">The configured minimum level.</param>
	/// <returns>The effective minimum level.</returns>
	internal static LogLevel MinimumLevelFor(string category, LogLevel configured)
	{
		if (configured >= LogLevel.Information)
		{
			return configured;
		}

		bool transport = IsCategoryOf(category, "Microsoft.AspNetCore")
		                 || IsCategoryOf(category, "System.Net.Http")
		                 || IsCategoryOf(category, "ModelContextProtocol");
		return transport ? LogLevel.Information : configured;
	}

	private static bool IsCategoryOf(string category, string root)
	{
		return category.StartsWith(root, StringComparison.Ordinal)
		       && (category.Length == root.Length || category[root.Length] == '.');
	}
}
