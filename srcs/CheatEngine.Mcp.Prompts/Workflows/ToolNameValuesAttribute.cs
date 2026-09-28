using System.ComponentModel.DataAnnotations;

namespace CheatEngine.Mcp.Prompts.Workflows;

/// <summary>
///     The <c>[AllowedValues]</c> of a <see cref="WorkflowInputKind.ToolName" /> parameter: every frozen tool name
///     (<see cref="WorkflowValues.ToolNames" />), which the SDK offers as its completion. That list is longer than one
///     completion may return, so the hosts' completion bound returns its first
///     <see cref="McpCompletions.MaximumValues" /> matches with the total and <c>hasMore</c>. The prompt checks the
///     value by its kind, which accepts any case and names the rule when it refuses a value.
/// </summary>
[AttributeUsage(AttributeTargets.Parameter)]
internal sealed class ToolNameValuesAttribute() : AllowedValuesAttribute([.. WorkflowValues.ToolNames]);
