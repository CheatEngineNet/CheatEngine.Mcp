using System.ComponentModel.DataAnnotations;

namespace CheatEngine.Mcp.Core.Execution;

/// <summary>Dispatch and job limits of one activation, bound once from the <c>Mcp:Execution</c> section.</summary>
public sealed class McpExecutionOptions : IValidatableObject
{
	/// <summary>The configuration section that holds the execution limits.</summary>
	public const string SectionName = "Mcp:Execution";

	/// <summary>
	///     How long one dispatch may hold Cheat Engine's main thread, in milliseconds, before it is logged as over budget
	///     (event 3001). A running dispatch is never interrupted; fixed scripts read the budget through
	///     <c>mcp.expired()</c> to stop cooperatively.
	/// </summary>
	[BoundedInteger(1, 10000,
		ErrorMessage = "Mcp:Execution:DispatchBudgetMilliseconds must be between 1 and 10000.")]
	public int DispatchBudgetMilliseconds
	{
		get;
		set;
	} = 100;

	/// <summary>
	///     How many tool dispatches of the activation may be admitted at once; a further call is refused as <c>busy</c>
	///     without starting.
	/// </summary>
	[BoundedInteger(1, 64, ErrorMessage = "Mcp:Execution:MaxConcurrentDispatches must be between 1 and 64.")]
	public int MaxConcurrentDispatches
	{
		get;
		set;
	} = 4;

	/// <summary>How many jobs the activation may hold at once.</summary>
	[BoundedInteger(1, 64, ErrorMessage = "Mcp:Execution:MaxJobs must be between 1 and 64.")]
	public int MaxJobs
	{
		get;
		set;
	} = 16;

	/// <summary>The lifetime of a job, in seconds, when its caller sets none.</summary>
	[BoundedInteger(1, 300, ErrorMessage = "Mcp:Execution:JobDefaultTtlSeconds must be between 1 and 300.")]
	public int JobDefaultTtlSeconds
	{
		get;
		set;
	} = 120;

	/// <summary>The longest lifetime a caller may give a job, in seconds; target-affecting jobs never exceed 300.</summary>
	[BoundedInteger(1, 300, ErrorMessage = "Mcp:Execution:JobMaxTtlSeconds must be between 1 and 300.")]
	public int JobMaxTtlSeconds
	{
		get;
		set;
	} = 300;

	/// <summary>How many items a job buffers for polling; beyond it the oldest items are evicted and counted.</summary>
	[BoundedInteger(1, 65536, ErrorMessage = "Mcp:Execution:JobBufferLimit must be between 1 and 65536.")]
	public int JobBufferLimit
	{
		get;
		set;
	} = 4096;

	/// <inheritdoc />
	public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
	{
		if (JobDefaultTtlSeconds > JobMaxTtlSeconds)
		{
			yield return new ValidationResult(
				"Mcp:Execution:JobDefaultTtlSeconds must not exceed Mcp:Execution:JobMaxTtlSeconds.",
				[nameof(JobDefaultTtlSeconds), nameof(JobMaxTtlSeconds)]);
		}
	}
}
