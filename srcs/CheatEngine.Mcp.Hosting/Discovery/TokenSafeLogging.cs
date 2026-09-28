using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CheatEngine.Mcp.Hosting.Discovery;

/// <summary>
///     Holds the framework categories that handle bearer-authenticated traffic at Information or above, whatever levels
///     a host configures, so no Trace or Debug diagnostic of a transport, an HTTP handler or an MCP message dump can
///     carry a discovery token.
/// </summary>
internal static class TokenSafeLogging
{
	/// <summary>The category prefixes that never log below <see cref="LogLevel.Information" />.</summary>
	internal static readonly string[] GuardedCategories =
		["Microsoft.AspNetCore", "System.Net.Http", "ModelContextProtocol"];

	extension(ILoggingBuilder logging)
	{
		/// <summary>
		///     Raises every configured rule for a guarded category to Information, and adds one guarded rule per provider
		///     that keeps its configured default when that default is already Information or above.
		/// </summary>
		/// <returns>The same builder.</returns>
		internal ILoggingBuilder AddTokenSafeFloor()
		{
			ArgumentNullException.ThrowIfNull(logging);
			// Post-configuration runs after every configuration source and AddFilter call, whatever their order.
			logging.Services.PostConfigure<LoggerFilterOptions>(static options => Apply(options));
			return logging;
		}
	}

	private static void Apply(LoggerFilterOptions options)
	{
		IList<LoggerFilterRule> rules = options.Rules;
		for (int index = 0; index < rules.Count; index++)
		{
			LoggerFilterRule rule = rules[index];
			if (IsGuarded(rule.CategoryName) && (rule.LogLevel ?? LogLevel.Trace) < LogLevel.Information)
			{
				rules[index] = new LoggerFilterRule(rule.ProviderName, rule.CategoryName, LogLevel.Information,
					rule.Filter);
			}
		}

		// The rule selector prefers a provider-specific rule to any provider-less one, so each provider named by a rule
		// gets its own guarded rules; otherwise a provider's default could still reach a guarded category.
		LogLevel fallback = DefaultLevel(rules, null) ?? options.MinLevel;
		foreach (string? provider in rules.Select(static rule => rule.ProviderName).Prepend(null)
					 .Distinct(StringComparer.Ordinal).ToArray())
		{
			LogLevel level = Max(DefaultLevel(rules, provider) ?? fallback, LogLevel.Information);
			foreach (string category in GuardedCategories)
			{
				if (!rules.Any(rule => string.Equals(rule.ProviderName, provider, StringComparison.Ordinal)
									   && string.Equals(rule.CategoryName, category, StringComparison.Ordinal)))
				{
					rules.Add(new LoggerFilterRule(provider, category, level, null));
				}
			}
		}
	}

	private static bool IsGuarded(string? category)
	{
		return category is not null && GuardedCategories.Any(guarded =>
			category.StartsWith(guarded, StringComparison.OrdinalIgnoreCase));
	}

	private static LogLevel? DefaultLevel(IList<LoggerFilterRule> rules, string? provider)
	{
		return rules.LastOrDefault(rule => rule.CategoryName is null
										   && string.Equals(rule.ProviderName, provider, StringComparison.Ordinal))
			?.LogLevel;
	}

	private static LogLevel Max(LogLevel first, LogLevel second)
	{
		return first > second ? first : second;
	}
}
