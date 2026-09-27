using CheatEngine.Mcp.Core.Execution;

namespace CheatEngine.Mcp.Core.Features;

/// <summary>
///     Reads whether Cheat Engine's Mono extension would inject its data collector on its own, so that tools which open a
///     process or load a table can require <see cref="McpFeature.TargetCodeExecution" /> first and report
///     <see cref="HostEffect" />.
/// </summary>
/// <remarks>
///     <para>
///         Cheat Engine 7.7's <c>autorun\monoscript.lua</c> creates the table option <c>UsesMono</c> and the setting
///         <c>IgnoreUsesMono</c> (<c>mono_initialize</c>, lines 7215-7218, run at startup from line 7293), then:
///     </para>
///     <list type="bullet">
///         <item>
///             when a process is opened (<c>mono_OnProcessOpened</c>, line 6209) it calls
///             <c>LaunchMonoDataCollector()</c> if <c>getSettingsOption('IgnoreUsesMono')==false</c> and
///             <c>getTableOption('UsesMono')</c> (celua.txt:418 and :432), for the first process or one it attached
///             before;
///         </item>
///         <item>
///             after a table load (<c>onTableLoad</c>, lines 7222-7248) it does so whenever
///             <c>getTableOption('UsesMono')</c> and a process is open, without consulting <c>IgnoreUsesMono</c>;
///         </item>
///         <item>
///             a successful attach sets <c>UsesMono</c> when <c>EnableUsesMonoOptionOnAttach</c> is on, its default
///             (lines 1484-1485 and 7217), so one Mono attach arms every later attach and table load.
///         </item>
///     </list>
///     <para>
///         The probe's fixed script reads the option, the setting and Cheat Engine's <c>process</c> global. It
///         over-approximates: it ignores the "first or previously attached process" condition, and a missing or failing
///         Cheat Engine function counts as "would attach". Run it in the same dispatch as the attach or load it guards
///         (<see cref="ReadInDispatch" />) so the option cannot change in between.
///     </para>
/// </remarks>
public static class MonoAutoAttachProbe
{
	/// <summary>The host effect a tool reports when Cheat Engine may inject the Mono data collector because of it.</summary>
	public const string HostEffect = "mono_auto_attach";

	/// <summary>
	///     The fixed body. It must never name a gated Cheat Engine API (<see cref="LuaFeatureScan" /> reads comments too):
	///     it only reads the option, the setting and the <c>process</c> global.
	/// </summary>
	internal const string Script = """
	                               local function read(f, name)
	                               	if type(f) ~= 'function' then return false, nil end
	                               	return pcall(f, name)
	                               end
	                               local usesKnown, uses = read(getTableOption, 'UsesMono')
	                               local ignoreKnown, ignore = read(getSettingsOption, 'IgnoreUsesMono')
	                               return {
	                               	tableUsesMono = (not usesKnown) or (uses ~= nil and uses ~= false),
	                               	ignoreUsesMono = ignoreKnown and ignore ~= false,
	                               	processOpen = process ~= nil and process ~= false,
	                               	determined = usesKnown and ignoreKnown
	                               }
	                               """;

	/// <summary>Reads Cheat Engine's state in a dispatch of its own.</summary>
	/// <param name="dispatch">The activation's dispatch facade.</param>
	/// <param name="operation">The calling tool, used for statistics and errors.</param>
	/// <param name="cancellationToken">The MCP request's token.</param>
	/// <returns>The state.</returns>
	/// <exception cref="Contract.CheatEngineToolException">The dispatch failed.</exception>
	public static MonoAutoAttachState Read(ToolDispatch dispatch, string operation,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(dispatch);
		return dispatch.RunLua(operation, Script, FeaturesJsonContext.Default.MonoAutoAttachState,
			cancellationToken);
	}

	/// <summary>
	///     Reads Cheat Engine's state inside the current <see cref="ToolDispatch.Run{T}" /> body, before the attach or load
	///     it guards.
	/// </summary>
	/// <param name="dispatch">The activation's dispatch facade.</param>
	/// <param name="operation">The calling tool, used for errors.</param>
	/// <param name="cancellationToken">The token the enclosing body received.</param>
	/// <returns>The state.</returns>
	/// <exception cref="Contract.CheatEngineToolException">The script failed.</exception>
	public static MonoAutoAttachState ReadInDispatch(ToolDispatch dispatch, string operation,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(dispatch);
		return dispatch.ExecuteLua(operation, Script, FeaturesJsonContext.Default.MonoAutoAttachState,
			cancellationToken);
	}
}
