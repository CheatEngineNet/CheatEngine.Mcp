# CheatEngine.Mcp.Prompts

`CheatEngine.Mcp.Prompts` turns the repository's guided workflows into MCP prompts and supplies the server instructions returned during initialization.
It keeps prompt behavior, argument validation, capability guidance, and linked documentation consistent across the plugin backend and the gateway.

The project is a .NET 10 class library that references [CheatEngine.Mcp.Core](../../libs/CheatEngine.Mcp.Core/README.md) and [CheatEngine.Mcp.Resources](../CheatEngine.Mcp.Resources/README.md).
The Resources project owns the embedded workflow Markdown; this project defines how that content is selected, validated, and rendered as prompts.

## Composition

`ICheatEngineMcpBuilder.AddPrompts()` is the project's only public composition entry point.
It registers `CheatEngineWorkflowPrompts` with `AddPromptType<T>()` and records the backend and gateway variants of `McpServerInstructions` in the Core manifest.

The prompt methods are static local prompts.
They do not accept `instanceId`, so the same prompt can be listed and rendered by either host.
When a workflow will call instance-scoped tools, its preamble directs the client to list instances, confirm the intended target with the user when necessary, and retain that instance ID throughout the workflow.

## What a workflow prompt contains

Each prompt is backed by one `WorkflowDefinition` and one Markdown body from `Resources/Knowledge/Workflows/`.
The renderer produces a user message containing the following material:

- A host-neutral preamble covering target selection, authorization, consent, and documentation links.
- Any required capability settings for tools used by the workflow.
- Validated inputs in their canonical display form, including relevant value notes.
- The selected workflow body, including any argument-specific excerpt.
- Resource links to the related `cheatengine://docs/...` documents.

The server's own resource listing enriches those links with resource metadata when it is available.
The prompt still renders a usable link when it is invoked without a server object.

`McpServerInstructions` provides the wider session contract for each host mode.
It is built from the reviewed tool and resource inventories, so initialization guidance remains aligned with the MCP surface that the host exposes.

## Input and rendering contract

Prompt parameters use `Display` and `Description` metadata because titles and descriptions are part of the published prompt schema.
The startup validator rejects an argument that does not supply the required title metadata.

`WorkflowArgument` describes each parameter's validation rule, default, allowed values, value notes, downstream tool target, and any tighter limit imposed by that tool.
Supported input kinds cover text, prose, positive and non-negative integers, speed values, symbols, and canonical tool names.

Enumerated values use `[AllowedValues]` and participate in MCP completion.
The `ToolNameValues` attribute completes from the reviewed tool-name inventory without publishing an unbounded enumeration.
The host limits completions to a bounded response.

A workflow body can contain sections headed `### argument: value`.
For a declared selector, the renderer retains the matching section or its `default` section and omits the alternatives.
This lets the source body guide distinct choices without producing an oversized prompt.

The renderer rejects invalid arguments before the body is returned.
It normalizes numeric inputs and tool names, enforces declared limits, and returns the MCP `invalid_argument` contract for invalid values.

## Project layout

| Path | Responsibility |
| --- | --- |
| `CheatEnginePromptsBuilderExtensions.cs` | Registers prompts and server instructions. |
| `CheatEngineWorkflowPrompts*.cs` | Declares the public prompt methods, split by workflow domain. |
| `McpServerInstructions.cs` | Builds initialization instructions for backend and gateway hosts. |
| `Workflows/CheatEngineWorkflows.cs` | Provides the ordered workflow definitions used by the prompt catalog. |
| `Workflows/*Workflows.cs` | Defines the workflows for session, value, code, structure, and runtime domains. |
| `Workflows/WorkflowDefinition.cs` | Defines workflow metadata, arguments, capability gates, and body selectors. |
| `Workflows/WorkflowPrompt.cs` | Validates arguments, loads bodies from Resources, selects excerpts, and renders MCP prompt messages. |
| `Workflows/WorkflowValues.cs` | Holds reviewed shared argument vocabularies and tool-name values. |

## Adding or changing a workflow

Keep a workflow's public prompt name in snake case and its Markdown body name in kebab case.
Define its metadata in the domain-appropriate `*Workflows.cs` file, include it in `CheatEngineWorkflows.All`, and declare the public prompt method in the matching partial `CheatEngineWorkflowPrompts` file.

Write the body in [`../CheatEngine.Mcp.Resources/Knowledge/Workflows`](../CheatEngine.Mcp.Resources/Knowledge/Workflows) and keep its document links, tool calls, parameters, capability gates, and report format synchronized with the public contract.
Use a selector only when an argument genuinely changes the guidance.

Every argument must have a clear display title and description.
Use a bounded vocabulary when the receiving tool has one, declare a target when the workflow passes the value to a tool, and keep free prose as the final argument so positional clients can supply parameters reliably.

Treat any change to a prompt name, schema, rendered text, capability line, or server instruction as a public contract change.
Review the corresponding golden snapshots and resource documentation as part of the same change.

## Verification

[`WorkflowPromptTests`](../../tests/CheatEngine.Mcp.Tests/Prompts/WorkflowPromptTests.cs) checks the prompt catalog, metadata, argument validation, completions, rendered content, links, gates, and length limits.
[`KnowledgeLintTests`](../../tests/CheatEngine.Mcp.Tests/Resources/KnowledgeLintTests.cs) verifies that prompt workflows remain synchronized with the Resources knowledge base and the served MCP contract.
[`ServerInstructionsTests`](../../tests/CheatEngine.Mcp.Tests/Contract/ServerInstructionsTests.cs) protects the initialization guidance.

Run the portable suite from the repository root:

```powershell
dotnet test --solution CheatEngine.Mcp.slnx --no-restore --no-build --filter-not-trait Category=LiveQualification --filter-not-trait Category=NativeLua --fail-skips on
```

See [CONTRIBUTING.md](../../CONTRIBUTING.md) for the full test, formatting, and golden-file requirements.
