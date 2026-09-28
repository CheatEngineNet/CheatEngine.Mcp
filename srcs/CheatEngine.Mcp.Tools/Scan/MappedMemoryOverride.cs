using System.Text.Json.Serialization.Metadata;

using CheatEngine.Client.Results;
using CheatEngine.Mcp.Core.Contract;

namespace CheatEngine.Mcp.Tools.Scan;

/// <summary>
///     The activation's owner of Cheat Engine's <c>MEM_MAPPED</c> scan override, through which a scan with
///     <c>includeMapped</c> also covers mapped memory (file views and shared sections, such as an emulator's guest
///     RAM). Every MCP scan that lists memory regions runs through it: <c>scan_first</c>, and <c>aob_find</c> and
///     <c>aob_find_value</c>.
/// </summary>
/// <remarks>
///     <para>
///         Cheat Engine 7.7's <c>setSpecialScanOptionsOverride</c> keeps one Cheat Engine-wide override per region
///         kind (<c>MEM_PRIVATE</c>, <c>MEM_IMAGE</c> and <c>MEM_MAPPED</c>): an entry in its table saves the setting
///         the first time and replaces it, and an absent entry restores the saved setting. It has no getter, so the end
///         (<see cref="EndScript" />, <c>{}</c>) returns every region kind to Cheat Engine's Scan Settings, which also
///         ends an override that a table or <c>lua_execute</c> set earlier.
///     </para>
///     <para>
///         A synchronous scan waits in Cheat Engine's <c>waitTillDone</c>, which on the main thread runs the dispatches
///         that other requests queued meanwhile, nested inside the wait. The outermost scan with <c>includeMapped</c>
///         therefore sets the override and ends it, a nested one shares it, and a nested scan of the other kind, whose
///         regions the override would change, is refused as <c>busy</c> before any effect. Only dispatch bodies, which
///         all run on Cheat Engine's main thread, read or change the counts. Work that lists regions on Cheat Engine's
///         own threads, such as a main scan that is still running or a pointer scan job, is not counted: the override
///         applies to it too while it is on.
///     </para>
///     <para>
///         The end ignores the caller's cancellation, but a stopping activation refuses every Client call, the end
///         included: a scan that the stop interrupts reports <c>partial_effect</c> with <c>cleanup_unconfirmed</c>, and
///         the override stays on until a later scan with <c>includeMapped</c> ends it or Cheat Engine restarts.
///     </para>
/// </remarks>
public sealed class MappedMemoryOverride
{
	/// <summary>
	///     The shared description of what <c>includeMapped</c> does to Cheat Engine, for the parameter of each scan
	///     tool that has one.
	/// </summary>
	internal const string IncludeMappedEffect =
		"Cheat Engine's MEM_MAPPED override is Cheat Engine-wide: this scan turns it on and then ends every scan-region override (setSpecialScanOptionsOverride), including one that a table or lua_execute set. Scans that run in parallel with it must use the same includeMapped, or are refused as busy.";

	/// <summary>
	///     Turns the <c>MEM_MAPPED</c> override on, or refuses as <c>unsupported</c> before any effect when this Cheat
	///     Engine has no <c>setSpecialScanOptionsOverride</c>; a Lua error of the call is <c>host_refused</c>.
	/// </summary>
	internal const string SetScript = """
	                                  local override = setSpecialScanOptionsOverride
	                                  if type(override) ~= 'function' then
	                                    return mcp.err('unsupported', 'This Cheat Engine has no ' ..
	                                      'setSpecialScanOptionsOverride, so a scan cannot include mapped memory.',
	                                      'not_started', 'Omit includeMapped, or ask the user to tick MEM_MAPPED ' ..
	                                      'under Edit > Settings > Scan Settings.')
	                                  end
	                                  local ok, message = pcall(override, {MEM_MAPPED=true})
	                                  if not ok then
	                                    return mcp.err('host_refused', 'Cheat Engine refused the MEM_MAPPED ' ..
	                                      'scan override: ' .. string.sub(tostring(message), 1, 512), 'unknown',
	                                      'Omit includeMapped, or ask the user to tick MEM_MAPPED under Edit > ' ..
	                                      'Settings > Scan Settings.')
	                                  end
	                                  return true
	                                  """;

	/// <summary>
	///     Ends every scan-region override, so that each region kind follows Cheat Engine's Scan Settings again. It is
	///     a harmless no-op when no override is set or this Cheat Engine has no <c>setSpecialScanOptionsOverride</c>,
	///     so a cleanup after an uncertain start may always run it; a Lua error of the call is <c>host_refused</c>.
	/// </summary>
	internal const string EndScript = """
	                                  local override = setSpecialScanOptionsOverride
	                                  if type(override) ~= 'function' then return true end
	                                  local ok, message = pcall(override, {})
	                                  if not ok then
	                                    return mcp.err('host_refused', 'Cheat Engine did not end the scan-region ' ..
	                                      'override: ' .. string.sub(tostring(message), 1, 512), 'unknown')
	                                  end
	                                  return true
	                                  """;

	private const string RepeatHint =
		"Cheat Engine's scans may still include mapped memory: run a scan with includeMapped=true again, such as scan_first on a named scanner, which ends the override when it returns, or ask the user to restart Cheat Engine.";

	private const string BusyHint = "Repeat the call after the running scan returns.";
	private const string Override = "Cheat Engine's MEM_MAPPED scan override";
	private const string UnexpectedFault = "an unexpected fault";

	private readonly ToolDispatch _dispatch;
	private int _following;
	private int _including;

	/// <summary>Creates the activation's owner without changing Cheat Engine.</summary>
	/// <param name="dispatch">The activation's dispatch facade.</param>
	public MappedMemoryOverride(ToolDispatch dispatch)
	{
		ArgumentNullException.ThrowIfNull(dispatch);
		_dispatch = dispatch;
	}

	/// <summary>
	///     Runs, inside the current dispatch body, a scan that follows Cheat Engine's <c>MEM_MAPPED</c> setting.
	/// </summary>
	/// <typeparam name="T">The scan's result.</typeparam>
	/// <param name="operation">The tool name, used in the refusal.</param>
	/// <param name="scan">The scan.</param>
	/// <returns>The scan's result.</returns>
	/// <exception cref="CheatEngineToolException">
	///     A scan with <c>includeMapped</c> is running, which would add mapped memory to this one (<c>busy</c>,
	///     <c>not_started</c>), or the scan failed.
	/// </exception>
	internal T Follow<T>(string operation, Func<T> scan)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(operation);
		ArgumentNullException.ThrowIfNull(scan);
		if (_including > 0)
		{
			throw CheatEngineToolException.Busy(
				"A scan with includeMapped is running, and its MEM_MAPPED override would add mapped memory to this " +
				$"scan; {operation} was not started.", BusyHint);
		}

		_following++;
		try
		{
			return scan();
		}
		finally
		{
			_following--;
		}
	}

	/// <summary>
	///     Runs, inside the current dispatch body, a scan that also covers mapped memory, and ends the override on
	///     every path once the scan has returned or failed, the caller's cancellation included.
	/// </summary>
	/// <typeparam name="T">The scan's result.</typeparam>
	/// <typeparam name="TDetails">The details of a <c>partial_effect</c>.</typeparam>
	/// <param name="operation">The tool name, used in the Lua chunk names and errors.</param>
	/// <param name="scan">The scan.</param>
	/// <param name="details">
	///     The <c>partial_effect</c> details, from the scan's result or <see langword="null" /> when it has none.
	/// </param>
	/// <param name="detailsType">The source-generated metadata of <typeparamref name="TDetails" />.</param>
	/// <param name="cancellationToken">The token of the enclosing dispatch body, for turning the override on.</param>
	/// <returns>The scan's result.</returns>
	/// <exception cref="CheatEngineToolException">
	///     A scan that follows the setting is running (<c>busy</c>, <c>not_started</c>), this Cheat Engine has no
	///     override (<c>unsupported</c>, <c>not_started</c>), Cheat Engine refused it (<c>host_refused</c>), the scan
	///     failed, or the override could not be removed (<c>partial_effect</c>, <c>cleanup_unconfirmed</c>).
	/// </exception>
	internal T Include<T, TDetails>(string operation, Func<T> scan, Func<T?, TDetails> details,
		JsonTypeInfo<TDetails> detailsType, CancellationToken cancellationToken)
		where T : class
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(operation);
		ArgumentNullException.ThrowIfNull(scan);
		ArgumentNullException.ThrowIfNull(details);
		ArgumentNullException.ThrowIfNull(detailsType);
		if (_following > 0)
		{
			throw CheatEngineToolException.Busy(
				"A scan without includeMapped is running, and the MEM_MAPPED override would add mapped memory to " +
				$"it; {operation} was not started.", BusyHint);
		}

		if (_including > 0)
		{
			// A nested scan shares the running scan's override, which ends when that outermost scan ends.
			_including++;
			try
			{
				return scan();
			}
			finally
			{
				_including--;
			}
		}

		try
		{
			_dispatch.ExecuteLua(operation, SetScript, ScanJsonContext.Default.Boolean, cancellationToken);
		}
		catch (Exception failure) when (!IsRefusedBeforeAnyEffect(failure))
		{
			End(operation, $"{Override} could not be set ({Reason(failure) ?? UnexpectedFault}), and it", failure,
				() => details(null), detailsType);
			throw;
		}

		T result;
		_including = 1;
		try
		{
			result = scan();
		}
		catch (Exception failure)
		{
			_including = 0;
			string outcome = Reason(failure) is { } cause
				? $"The scan failed ({cause}), and {Override}"
				: $"The scan did not complete, and {Override}";
			End(operation, outcome, failure, () => details(null), detailsType);
			throw;
		}

		_including = 0;
		End(operation, $"The scan completed, but {Override}", null, () => details(result), detailsType);
		return result;
	}

	/// <summary>Ends the override, or reports that Cheat Engine's scans may still include mapped memory.</summary>
	/// <param name="operation">The tool name.</param>
	/// <param name="outcome">The start of the failure message, which names what happened before the end.</param>
	/// <param name="failure">
	///     The failure that ended the scan, or <see langword="null" /> after a completed scan.
	/// </param>
	/// <param name="details">The <c>partial_effect</c> details.</param>
	/// <param name="detailsType">The source-generated metadata of <typeparamref name="TDetails" />.</param>
	/// <exception cref="CheatEngineToolException">
	///     The end failed (<c>partial_effect</c> with <c>cleanup_unconfirmed</c>); the failure that ended the scan, if
	///     any, is its inner exception.
	/// </exception>
	private void End<TDetails>(string operation, string outcome, Exception? failure, Func<TDetails> details,
		JsonTypeInfo<TDetails> detailsType)
	{
		try
		{
			// No token: the caller's cancellation must not leave the override on. A stopping activation still
			// refuses the call, which reports cleanup_unconfirmed.
			_dispatch.ExecuteLua(operation, EndScript, ScanJsonContext.Default.Boolean, CancellationToken.None);
		}
		catch (Exception endFailure)
		{
			CheatEngineToolException partial = CheatEngineToolException.PartialEffect(
				$"{outcome} could not be removed: {Reason(endFailure) ?? UnexpectedFault}",
				ToolHostEffect.CleanupUnconfirmed, details(), detailsType, false, RepeatHint);
			throw new CheatEngineToolException(partial.Error, failure ?? endFailure);
		}
	}

	/// <summary>
	///     Whether a failure proves that its call changed nothing: a script's or the Client's refusal before any
	///     effect, such as a missing override function, a cancellation observed before dispatch or a stopping
	///     activation.
	/// </summary>
	private static bool IsRefusedBeforeAnyEffect(Exception failure)
	{
		return failure switch
		{
			CheatEngineToolException tool => tool.Error.HostEffect == ToolHostEffect.NotStarted,
			CheatEngineClientException client => client.Failure.HostEffect == CheatEngineHostEffect.NotStarted,
			CheatEngineOperationCanceledException cancelled =>
				cancelled.Failure.HostEffect == CheatEngineHostEffect.NotStarted,
			_ => false
		};
	}

	/// <summary>The contract message of a failure, or <see langword="null" /> for a cancellation or a fault.</summary>
	private static string? Reason(Exception failure)
	{
		return failure switch
		{
			CheatEngineToolException tool => tool.Error.Message,
			CheatEngineClientException client => ToolFailureMapping.Map(client.Failure, false).Message,
			_ => null
		};
	}
}
