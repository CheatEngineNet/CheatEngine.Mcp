namespace CheatEngine.Mcp.Core.Execution;

/// <summary>What the activation's tool dispatches cost Cheat Engine's main thread so far.</summary>
/// <param name="BudgetMs">The configured dispatch budget, in milliseconds.</param>
/// <param name="Count">The dispatches whose body ran.</param>
/// <param name="OverBudget">The dispatches that held the main thread longer than the budget.</param>
/// <param name="Rejected">The calls refused as <c>busy</c> because the concurrency limit was reached.</param>
/// <param name="MaxExecutedMs">The longest time one body held the main thread, in milliseconds.</param>
/// <param name="MaxOperation">The operation of that longest dispatch, or <see langword="null" /> before the first.</param>
/// <param name="MaxQueuedMs">The longest wait for the main thread before a body started, in milliseconds.</param>
public sealed record DispatchSummary(
	int BudgetMs,
	int Count,
	int OverBudget,
	int Rejected,
	int MaxExecutedMs,
	string? MaxOperation,
	int MaxQueuedMs);
