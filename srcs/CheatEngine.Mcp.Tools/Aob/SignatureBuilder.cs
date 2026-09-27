using CheatEngine.Client.Scanning;
using CheatEngine.Mcp.Core.Contract;

using CorePattern = CheatEngine.Mcp.Core.Values.AobPattern;

namespace CheatEngine.Mcp.Tools.Aob;

/// <summary>Turns scan outcomes and Cheat Engine signatures into the <c>aob_*</c> contract.</summary>
internal static class SignatureBuilder
{
	/// <summary>
	///     Normalizes a signature returned by <c>getUniqueAOB</c>, whose wildcards are <c>*</c> or <c>**</c>, to the
	///     contract's form with <c>??</c>.
	/// </summary>
	/// <param name="pattern">Cheat Engine's signature.</param>
	/// <returns>The normalized pattern, or <see langword="null" /> when it is not a pattern the contract accepts.</returns>
	internal static string? NormalizeCheatEnginePattern(string? pattern)
	{
		if (string.IsNullOrWhiteSpace(pattern))
		{
			return null;
		}

		try
		{
			return CorePattern.Normalize(pattern.Replace('*', '?'), "pattern");
		}
		catch (CheatEngineToolException)
		{
			return null;
		}
	}

	/// <summary>How many byte positions a normalized pattern holds.</summary>
	/// <param name="normalized">A normalized pattern.</param>
	/// <returns>The byte count.</returns>
	internal static int ByteLength(string normalized)
	{
		return (normalized.Length + 1) / 3;
	}

	/// <summary>The contract form of the part of the target a scan covered.</summary>
	/// <param name="scope">The Client's scope.</param>
	/// <returns>The scope.</returns>
	internal static AobScanScope Scope(PatternScanScope scope)
	{
		return scope switch
		{
			PatternScanScope.GlobalHostScan => AobScanScope.GlobalScan,
			PatternScanScope.HostBoundedRange => AobScanScope.BoundedScan,
			PatternScanScope.GlobalHostScanWithManagedFilter => AobScanScope.FilteredGlobalScan,
			_ => AobScanScope.Unknown
		};
	}

	/// <summary>
	///     Whether a successful scan's count is every match in scope: nothing was cut by the limit and every row Cheat
	///     Engine returned was read.
	/// </summary>
	/// <param name="result">The copied result.</param>
	/// <param name="metrics">The scan metrics.</param>
	/// <returns><see langword="true" /> for an exact count.</returns>
	internal static bool IsExact(AobScanResult result, PatternScanMetrics metrics)
	{
		return !result.IsTruncated && metrics.InBoundsCountIsExact;
	}

	/// <summary>The time Cheat Engine's scan and the copy of its result took.</summary>
	/// <param name="metrics">The scan metrics.</param>
	/// <returns>The elapsed milliseconds, rounded and saturated.</returns>
	internal static int ElapsedMilliseconds(PatternScanMetrics metrics)
	{
		double total = (metrics.HostScanElapsed + metrics.MaterializationElapsed).TotalMilliseconds;
		return total >= int.MaxValue ? int.MaxValue : (int) Math.Round(Math.Max(0, total));
	}
}
