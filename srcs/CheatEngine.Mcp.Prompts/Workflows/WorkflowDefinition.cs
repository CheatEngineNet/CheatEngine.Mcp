using CheatEngine.Mcp.Core.Features;

namespace CheatEngine.Mcp.Prompts.Workflows;

/// <summary>
///     One guided workflow: its prompt, its embedded body, the documents it links, its arguments, the settings its
///     gated steps need and the arguments that select an excerpt of its body.
/// </summary>
/// <param name="Prompt">The prompt name (snake case).</param>
/// <param name="Workflow">The workflow body's name (kebab case), also its <c>cheatengine://docs/workflows/…</c> slug.</param>
/// <param name="Documents">The knowledge documents the prompt links, by slug, most relevant first.</param>
internal sealed record WorkflowDefinition(string Prompt, string Workflow, IReadOnlyList<string> Documents)
{
	/// <summary>
	///     The prompt's arguments in declaration order; the prompt method passes one value per argument, in this order.
	/// </summary>
	public IReadOnlyList<WorkflowArgument> Arguments
	{
		get;
		init;
	} = [];

	/// <summary>
	///     The settings that the tools the body cites require (their <c>cheatengine/requires</c> metadata), in enum order.
	///     The prompt's description ends with <see cref="WorkflowPrompt.GateLine" /> of them, and the renderer adds that
	///     line before the inputs; a test keeps them equal to the cited tools' requirements.
	/// </summary>
	public IReadOnlyList<McpFeature> Gates
	{
		get;
		init;
	} = [];

	/// <summary>
	///     The enumerated arguments whose value selects one <c>### name: value</c> section of the body: the renderer keeps
	///     the section of the selected value (or the <c>### name: default</c> section) and drops the others.
	/// </summary>
	public IReadOnlyList<string> Selectors
	{
		get;
		init;
	} = [];
}

/// <summary>One prompt argument as the workflow body names it, with its checks.</summary>
/// <param name="Name">The argument name, which the body cites as <c>{Name}</c>.</param>
/// <param name="Kind">How the value is checked.</param>
/// <param name="Required">Whether the prompt refuses a missing or blank value.</param>
internal sealed record WorkflowArgument(
	string Name,
	WorkflowInputKind Kind = WorkflowInputKind.Text,
	bool Required = false)
{
	/// <summary>The value the workflow assumes when none is sent.</summary>
	public string? Default
	{
		get;
		init;
	}

	/// <summary>The only accepted values, when the argument is an enumeration; matched without regard to case.</summary>
	public IReadOnlyList<string>? Allowed
	{
		get;
		init;
	}

	/// <summary>A note rendered after an accepted value, such as the record type code that a value type maps to.</summary>
	public IReadOnlyDictionary<string, string>? ValueNotes
	{
		get;
		init;
	}

	/// <summary>
	///     The largest value an integer argument accepts, when the tools that receive it accept less than
	///     <see cref="int.MaxValue" />: the smallest upper limit among them, so the prompt refuses a value the workflow
	///     could not pass on before any step runs. A test keeps it equal to that tool limit.
	/// </summary>
	public int? Maximum
	{
		get;
		init;
	}

	/// <summary>
	///     The longest name a <see cref="WorkflowInputKind.Symbol" /> argument accepts, when a step passes a longer
	///     name built from it (such as <c>{symbolName}Hook</c>) to a tool: the tool's limit minus what the step
	///     appends, so the prompt refuses a name the workflow could not pass on. Without it, a symbol is at most
	///     <see cref="WorkflowPrompt.MaximumSymbolLength" /> characters. A test keeps it equal to that tool limit.
	/// </summary>
	public int? MaximumLength
	{
		get;
		init;
	}

	/// <summary>
	///     The tool parameter that receives the value, so a test can keep <see cref="Allowed" /> within the vocabulary the
	///     tool accepts; the value <c>auto</c> never reaches the tool, because the workflow resolves it first.
	/// </summary>
	public WorkflowTarget? Target
	{
		get;
		init;
	}
}

/// <summary>The tool parameter that a workflow passes an argument's value to.</summary>
/// <param name="Tool">The tool name.</param>
/// <param name="Parameter">The parameter name in the tool's input schema.</param>
internal readonly record struct WorkflowTarget(string Tool, string Parameter);

/// <summary>How a prompt argument is checked before it is echoed into the prompt.</summary>
internal enum WorkflowInputKind
{
	/// <summary>One free-text token on one line, such as an address expression, a module name or a path.</summary>
	Text,

	/// <summary>
	///     Several words of free text on one line, such as a description, a goal or a question. A prompt declares at
	///     most one, last, because some clients fill arguments by position from a whitespace-separated command line.
	/// </summary>
	Prose,

	/// <summary>
	///     A decimal integer from 1 to the argument's <see cref="WorkflowArgument.Maximum" /> (else 2147483647),
	///     rendered without leading zeros.
	/// </summary>
	PositiveInteger,

	/// <summary>
	///     A decimal integer from 0 to the argument's <see cref="WorkflowArgument.Maximum" /> (else 2147483647),
	///     rendered without leading zeros: a record id (Cheat Engine numbers records from 0) or a bound whose tool
	///     parameter accepts 0.
	/// </summary>
	NonNegativeInteger,

	/// <summary>A speed multiplier between 0.01 and 1000, rendered in its shortest round-trip form.</summary>
	Speed,

	/// <summary>
	///     A Cheat Engine symbol name: a letter or underscore, then letters, digits or underscores, at most the
	///     argument's <see cref="WorkflowArgument.MaximumLength" /> (else
	///     <see cref="WorkflowPrompt.MaximumSymbolLength" />) characters.
	/// </summary>
	Symbol,

	/// <summary>
	///     A frozen tool name, matched without regard to case and rendered in its canonical case. It is checked by its
	///     kind rather than as an enumeration of <see cref="WorkflowValues.ToolNames" />, which would try every name
	///     in every worst case and list them all in a refusal; its parameter completes through
	///     <see cref="ToolNameValuesAttribute" />, which the hosts bound to
	///     <see cref="McpCompletions.MaximumValues" /> values.
	/// </summary>
	ToolName
}
