# CheatEngine.Mcp.Prompts

MCP prompts, declared through `AddPrompts()` with `AddPromptType<T>()`, and the `initialize` server instructions.

- `CheatEngineWorkflowPrompts`: the guided workflows, one static (Local) prompt per `CheatEngineWorkflows.All` entry,
  without `instanceId`. The class is one partial type split by theme (session, values, code, structures, runtimes). A
  prompt renders one user message and one resource link per related `cheatengine://docs/...` document, enriched from
  the serving host's own resource listing. The message holds:
  - the workflow title and a fixed, host-neutral preamble (call `instance_list` first when the tools take an
    `instanceId`);
  - a `Needs Mcp:Enable...` line when the body cites gated tools (the same sentence ends the prompt description);
  - the checked inputs, with a note beside some values (such as the `record_create` `variableType` code of a
    `freeze_value` type);
  - the workflow body.
- `Workflows/`: the definitions and the renderer.
  - `WorkflowDefinition`: prompt, body, linked documents, arguments (kind, default, allowed values, notes, the largest
    integer or the longest symbol name the receiving tools accept and the tool parameter that receives the value),
    gates and excerpt selectors.
    The definitions live in one class per theme and `CheatEngineWorkflows.All` lists them in the order of the workflow
    index.
  - `WorkflowValues`: the enumerated argument values, equal to each parameter's `[AllowedValues]`, which drive
    completion. The SDK appends every allowed value that matches the typed prefix without a bound; Core's completion
    bound then returns at most 100 of them, with the whole count as `total` and `hasMore`. `explain_error.tool` takes
    any frozen tool name, far more than 100: `[ToolNameValues]` completes it over `WorkflowValues.ToolNames` in
    ordinal order, and it is checked by its kind, ignoring case, rather than as an enumeration.
  - `WorkflowPrompt`: checks the inputs and renders the prompt, echoing a number in its canonical form (no leading
    zeros, the shortest round-trip speed). A body section headed `### <argument>: <value>` is an argument-conditioned
    excerpt: the renderer keeps only the section of the selected value (or `### <argument>: default`) for the
    definition's selectors, so one body can carry guidance for every choice within the rendered budget.
- Every argument declares a title, `[Display(Name = ...)]` beside its `[Description]`, which Core publishes as the
  argument's `title` on every host (its startup validator refuses a prompt argument without one).
- Invalid arguments are JSON-RPC `-32602` errors with the contract data. Arguments are single tokens except at most one
  free-text (`Prose`) argument, declared last among the required arguments or, when optional, last of all, because
  some clients fill arguments by position.
- `McpServerInstructions`: the gateway and backend variants, built from `CheatEngineToolNames` and
  `McpResourceUris.Doc`; `AddPrompts()` records them in the Core manifest. They state the scope (single-player or
  offline software the user may modify), consent, orientation, `cheatengine://docs/workflows` and the call contract.

The workflow bodies and the documents they link belong to the Resources project (`Knowledge/Workflows/*.md` and
`Knowledge/Documents/*.md`), which embeds them once and serves the bodies as `cheatengine://docs/workflows/{workflow}`;
this project reads them through `CheatEngineKnowledge`. A body has the sections Goal, Steps, Decisions, Pitfalls and
Report, links the documents its definition lists, and cites only served tools and parameters. The tests in
`tests/CheatEngine.Mcp.Tests/Resources/KnowledgeLintTests.cs` enforce this.

**Depends on:** Core and Resources.
