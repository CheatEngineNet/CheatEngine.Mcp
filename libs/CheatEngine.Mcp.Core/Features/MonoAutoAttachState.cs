namespace CheatEngine.Mcp.Core.Features;

/// <summary>What Cheat Engine's Mono extension would do on its own, as read by <see cref="MonoAutoAttachProbe" />.</summary>
/// <param name="TableUsesMono">
///     Whether the current table's <c>UsesMono</c> option is set, or could not be read (counted as set).
/// </param>
/// <param name="IgnoreUsesMono">
///     Whether the <c>IgnoreUsesMono</c> setting stops the process-open hook: Cheat Engine attaches only when the setting
///     reads exactly <see langword="false" />, and an unreadable setting counts as not ignored.
/// </param>
/// <param name="ProcessOpen">Whether Cheat Engine has a process open (its <c>process</c> global).</param>
/// <param name="Determined">Whether both the option and the setting were read; otherwise the worst case was assumed.</param>
public sealed record MonoAutoAttachState(bool TableUsesMono, bool IgnoreUsesMono, bool ProcessOpen, bool Determined)
{
	/// <summary>Whether opening a process (attach, create, open a file) would inject the Mono data collector.</summary>
	public bool WouldAttachOnProcessOpen => TableUsesMono && !IgnoreUsesMono;

	/// <summary>Whether loading a table would inject the Mono data collector right after the load.</summary>
	/// <param name="loadedTableUsesMono">
	///     Whether the table being loaded sets <c>UsesMono</c> (see
	///     <see cref="Tables.CheatTableInspection.UsesMono" />).
	/// </param>
	/// <returns>
	///     <see langword="true" /> when a process is open and either table sets the option; Cheat Engine's table-load
	///     hook does not consult <c>IgnoreUsesMono</c>.
	/// </returns>
	public bool WouldAttachOnTableLoad(bool loadedTableUsesMono)
	{
		return ProcessOpen && (TableUsesMono || loadedTableUsesMono);
	}

	/// <summary>
	///     Requires <see cref="McpFeature.TargetCodeExecution" /> when opening a process would inject the Mono data
	///     collector. Call it before the attach.
	/// </summary>
	/// <param name="gate">The activation's switches.</param>
	/// <param name="toolName">The tool that opens the process.</param>
	/// <returns>
	///     <see langword="true" /> when the collector may be injected: report <see cref="MonoAutoAttachProbe.HostEffect" />.
	/// </returns>
	/// <exception cref="Contract.CheatEngineToolException"><c>capability_disabled</c> with <c>not_started</c>.</exception>
	public bool RequireForProcessOpen(McpFeatureGate gate, string toolName)
	{
		if (!WouldAttachOnProcessOpen)
		{
			return false;
		}

		Require(gate, toolName,
			"Cheat Engine would inject its Mono data collector into the process because the table option UsesMono is set");
		return true;
	}

	/// <summary>
	///     Requires <see cref="McpFeature.TargetCodeExecution" /> when loading a table would inject the Mono data
	///     collector. Call it before the load.
	/// </summary>
	/// <param name="gate">The activation's switches.</param>
	/// <param name="toolName">The tool that loads the table.</param>
	/// <param name="loadedTableUsesMono">Whether the table being loaded sets <c>UsesMono</c>.</param>
	/// <returns>
	///     <see langword="true" /> when the collector may be injected: report <see cref="MonoAutoAttachProbe.HostEffect" />.
	/// </returns>
	/// <exception cref="Contract.CheatEngineToolException"><c>capability_disabled</c> with <c>not_started</c>.</exception>
	public bool RequireForTableLoad(McpFeatureGate gate, string toolName, bool loadedTableUsesMono)
	{
		if (!WouldAttachOnTableLoad(loadedTableUsesMono))
		{
			return false;
		}

		Require(gate, toolName,
			"Cheat Engine would inject its Mono data collector into the open process because a table sets UsesMono");
		return true;
	}

	private static void Require(McpFeatureGate gate, string toolName, string reason)
	{
		McpFeatureRequirementsBuilder requirements = new();
		requirements.Add(McpFeature.TargetCodeExecution, reason);
		requirements.Build().Enforce(gate, toolName);
	}
}
