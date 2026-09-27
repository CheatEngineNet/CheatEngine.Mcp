namespace CheatEngine.Mcp.Prompts.Workflows;

/// <summary>One guided workflow: its prompt, its embedded body and the documents it links.</summary>
/// <param name="Prompt">The prompt name (snake case).</param>
/// <param name="Workflow">The workflow body's name (kebab case), also its <c>cheatengine://docs/workflows/…</c> slug.</param>
/// <param name="Documents">The knowledge documents the prompt links, by slug, most relevant first.</param>
internal sealed record WorkflowDefinition(string Prompt, string Workflow, IReadOnlyList<string> Documents);

/// <summary>One prompt argument as the workflow body names it, with its checks.</summary>
/// <param name="Name">The argument name, which the body cites as <c>{Name}</c>.</param>
/// <param name="Value">The value the client sent, or <see langword="null" />.</param>
/// <param name="Kind">How the value is checked.</param>
/// <param name="Default">The value the workflow assumes when none is sent.</param>
/// <param name="Allowed">The only accepted values, when the argument is an enumeration.</param>
/// <param name="Required">Whether a supplied value must contain non-whitespace text.</param>
internal readonly record struct WorkflowInput(
	string Name,
	string? Value,
	WorkflowInputKind Kind = WorkflowInputKind.Text,
	string? Default = null,
	IReadOnlyList<string>? Allowed = null,
	bool Required = false);

/// <summary>How a prompt argument is checked before it is echoed into the prompt.</summary>
internal enum WorkflowInputKind
{
	/// <summary>Free text on one line, such as a description, an address expression or a path.</summary>
	Text,

	/// <summary>A positive decimal integer.</summary>
	PositiveInteger,

	/// <summary>A speed multiplier between 0.01 and 1000.</summary>
	Speed,

	/// <summary>A Cheat Engine symbol name: a letter or underscore, then letters, digits or underscores.</summary>
	Symbol
}
