namespace CheatEngine.Mcp.Core.Targets;

/// <summary>An explicit target change that a process tool is about to make.</summary>
/// <param name="CurrentProcessId">The selected process, or <see langword="null" /> when none is selected.</param>
/// <param name="RequestedProcessId">
///     The process to select, or <see langword="null" /> when it is not known in advance, such as an attach by name, a
///     launched process or a file opened as a process.
/// </param>
public readonly record struct TargetTransition(int? CurrentProcessId, int? RequestedProcessId)
{
	/// <summary>Whether the request selects the process that is already selected, which changes nothing.</summary>
	public bool IsReselection => RequestedProcessId is > 0 && RequestedProcessId == CurrentProcessId;
}
