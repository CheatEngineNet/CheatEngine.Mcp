using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

using CheatEngine.Mcp.Core.Contract;
using CheatEngine.Mcp.Resources.Knowledge;

using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace CheatEngine.Mcp.Resources.Docs;

/// <summary>
///     The knowledge documents, one static (Local) resource each, plus the workflow bodies as one template. Every backend
///     and the gateway serve them without any instance; the text is the operator skill's reference files with their
///     relative links rewritten to <c>cheatengine://docs/…</c>.
/// </summary>
[McpServerResourceType]
public sealed class CheatEngineDocResources
{
	private const string Markdown = McpResourceUris.MarkdownMimeType;
	private const string Docs = McpResourceUris.DocsPrefix;

	/// <summary>How long a client may cache a document: the text only changes with the plugin build.</summary>
	internal static readonly TimeSpan DocumentTimeToLive = TimeSpan.FromHours(1);

	/// <summary>Session rules and the workflow index.</summary>
	/// <returns>The document.</returns>
	[McpServerResource(UriTemplate = Docs + "workflows", Name = "doc_workflows",
		Title = "Workflows and session rules", MimeType = Markdown)]
	[Description("Start here: golden rules, session start, ids, jobs, resources and the index of the 22 guided "
				 + "workflows with their key tools.")]
	public static ReadResourceResult Workflows()
	{
		return Document("workflows");
	}

	/// <summary>Value types and scan strategy.</summary>
	/// <returns>The document.</returns>
	[McpServerResource(UriTemplate = Docs + "value-scans", Name = "doc_value_scans",
		Title = "Value types and scan strategy", MimeType = Markdown)]
	[Description("Choosing value types, first and next scan comparisons, float rounding, narrowing and the main "
				 + "versus named scanners.")]
	public static ReadResourceResult ValueScans()
	{
		return Document("value-scans");
	}

	/// <summary>Pointers and pointer scans.</summary>
	/// <returns>The document.</returns>
	[McpServerResource(UriTemplate = Docs + "pointers", Name = "doc_pointers", Title = "Pointers and pointer scans",
		MimeType = Markdown)]
	[Description("Pointer notation and offset order, manual chains from a writer, pointer maps, path scans, rescans "
				 + "and their limits.")]
	public static ReadResourceResult Pointers()
	{
		return Document("pointers");
	}

	/// <summary>Debugger methods.</summary>
	/// <returns>The document.</returns>
	[McpServerResource(UriTemplate = Docs + "debugger", Name = "doc_debugger", Title = "Debugger methods",
		MimeType = Markdown)]
	[Description("Debugger interfaces, hardware breakpoints, capture and trace jobs, registers and how to leave a "
				 + "target running.")]
	public static ReadResourceResult Debugger()
	{
		return Document("debugger");
	}

	/// <summary>Auto Assembler syntax and templates.</summary>
	/// <returns>The document.</returns>
	[McpServerResource(UriTemplate = Docs + "auto-assembler", Name = "doc_auto_assembler",
		Title = "Auto Assembler syntax and templates", MimeType = Markdown)]
	[Description("Auto Assembler sections and commands, injection templates, the check-apply-verify-release cycle "
				 + "and its gate.")]
	public static ReadResourceResult AutoAssembler()
	{
		return Document("auto-assembler");
	}

	/// <summary>x64 code injection essentials.</summary>
	/// <returns>The document.</returns>
	[McpServerResource(UriTemplate = Docs + "x64-injection", Name = "doc_x64_injection",
		Title = "x64 code injection essentials", MimeType = Markdown)]
	[Description("Registers, calling convention, flags, SSE, jump sizes and RIP-relative operands for code that "
				 + "runs inside a game's function.")]
	public static ReadResourceResult X64Injection()
	{
		return Document("x64-injection");
	}

	/// <summary>AOB signatures.</summary>
	/// <returns>The document.</returns>
	[McpServerResource(UriTemplate = Docs + "aob-signatures", Name = "doc_aob_signatures", Title = "AOB signatures",
		MimeType = Markdown)]
	[Description("Byte patterns that survive updates: wildcard rules, proving uniqueness, module scope and repair "
				 + "after an update.")]
	public static ReadResourceResult AobSignatures()
	{
		return Document("aob-signatures");
	}

	/// <summary>Structures and dissect.</summary>
	/// <returns>The document.</returns>
	[McpServerResource(UriTemplate = Docs + "structures", Name = "doc_structures", Title = "Structures and dissect",
		MimeType = Markdown)]
	[Description("Structure layouts: autoguess, RTTI, PDB and .NET sources, comparing instances and cleaning up "
				 + "CE-owned structures.")]
	public static ReadResourceResult Structures()
	{
		return Document("structures");
	}

	/// <summary>Disassembly and code analysis.</summary>
	/// <returns>The document.</returns>
	[McpServerResource(UriTemplate = Docs + "code-analysis", Name = "doc_code_analysis",
		Title = "Disassembly and code analysis", MimeType = Markdown)]
	[Description("Reading disassembly, functions, references, strings and RIP-relative globals to learn what a "
				 + "value means and where to patch.")]
	public static ReadResourceResult CodeAnalysis()
	{
		return Document("code-analysis");
	}

	/// <summary>Unity (Mono) and .NET targets.</summary>
	/// <returns>The document.</returns>
	[McpServerResource(UriTemplate = Docs + "mono-and-dotnet", Name = "doc_mono_and_dotnet",
		Title = "Unity (Mono) and .NET targets", MimeType = Markdown)]
	[Description("Detecting Mono, IL2CPP and .NET, the host effects of a Mono attach, and reading classes, fields "
				 + "and statics.")]
	public static ReadResourceResult MonoAndDotNet()
	{
		return Document("mono-and-dotnet");
	}

	/// <summary>Speedhack.</summary>
	/// <returns>The document.</returns>
	[McpServerResource(UriTemplate = Docs + "speedhack", Name = "doc_speedhack", Title = "Speedhack",
		MimeType = Markdown)]
	[Description("How the speedhack hooks a game's clocks, its lasting host effects and how to restore normal "
				 + "speed.")]
	public static ReadResourceResult Speedhack()
	{
		return Document("speedhack");
	}

	/// <summary>Address list records and cheat tables.</summary>
	/// <returns>The document.</returns>
	[McpServerResource(UriTemplate = Docs + "cheat-tables", Name = "doc_cheat_tables",
		Title = "Address list records and cheat tables", MimeType = Markdown)]
	[Description("Records, freezing, groups, script records and robust tables that survive restarts and updates.")]
	public static ReadResourceResult CheatTables()
	{
		return Document("cheat-tables");
	}

	/// <summary>Lua in CheatEngine.Mcp.</summary>
	/// <returns>The document.</returns>
	[McpServerResource(UriTemplate = Docs + "lua", Name = "doc_lua", Title = "Lua in CheatEngine.Mcp",
		MimeType = Markdown)]
	[Description("Fixed-script tools versus lua_execute, the unsafe Lua gate and why Lua must never bypass a "
				 + "gate.")]
	public static ReadResourceResult Lua()
	{
		return Document("lua");
	}

	/// <summary>Kernel access (DBK/DBVM).</summary>
	/// <returns>The document.</returns>
	[McpServerResource(UriTemplate = Docs + "kernel", Name = "doc_kernel", Title = "Kernel access (DBK/DBVM)",
		MimeType = Markdown)]
	[Description("The kernel driver and hypervisor tools, their gate and their risks, including the reported BSOD "
				 + "with the query memory region routines setting.")]
	public static ReadResourceResult Kernel()
	{
		return Document("kernel");
	}

	/// <summary>Safety and responsible use.</summary>
	/// <returns>The document.</returns>
	[McpServerResource(UriTemplate = Docs + "safety", Name = "doc_safety", Title = "Safety and responsible use",
		MimeType = Markdown)]
	[Description("Read before the first change: authorized targets, anti-cheat, consent, gates, host effects and "
				 + "restoring what a session changed.")]
	public static ReadResourceResult Safety()
	{
		return Document("safety");
	}

	/// <summary>Failure kinds and recovery.</summary>
	/// <returns>The document.</returns>
	[McpServerResource(UriTemplate = Docs + "errors-and-recovery", Name = "doc_errors_and_recovery",
		Title = "Failure kinds and recovery", MimeType = Markdown)]
	[Description("The error envelope and resource error data, every failure kind with its next step, and recovery "
				 + "without repeating a side effect.")]
	public static ReadResourceResult ErrorsAndRecovery()
	{
		return Document("errors-and-recovery");
	}

	/// <summary>Connection setup and troubleshooting.</summary>
	/// <returns>The document.</returns>
	[McpServerResource(UriTemplate = Docs + "connection-troubleshooting", Name = "doc_connection_troubleshooting",
		Title = "Connection setup and troubleshooting", MimeType = Markdown)]
	[Description("Plugin, gateway and client setup, and what to check when " + CheatEngineToolNames.InstanceList
				 + " is empty or an instance is unavailable.")]
	public static ReadResourceResult ConnectionTroubleshooting()
	{
		return Document("connection-troubleshooting");
	}

	/// <summary>Cheat Engine tutorial map.</summary>
	/// <returns>The document.</returns>
	[McpServerResource(UriTemplate = Docs + "ce-tutorial", Name = "doc_ce_tutorial",
		Title = "Cheat Engine tutorial map", MimeType = Markdown)]
	[Description("The tutorial and gtutorial steps mapped to their techniques, workflows and tools; a safe smoke "
				 + "test.")]
	public static ReadResourceResult CeTutorial()
	{
		return Document("ce-tutorial");
	}

	/// <summary>One workflow body: the text the prompt of the same name embeds.</summary>
	/// <param name="workflow">The kebab-case workflow name.</param>
	/// <returns>The workflow body.</returns>
	[McpServerResource(UriTemplate = McpResourceUris.WorkflowsPrefix + "{workflow}", Name = "doc_workflow",
		Title = "Guided workflow body", MimeType = Markdown)]
	[Description("The steps, decisions, pitfalls and report format of one guided workflow; the prompt of the same "
				 + "name (with underscores) embeds this text.")]
	public static ReadResourceResult Workflow(
		[Description("The workflow name in kebab case, such as find-writer.")]
		[AllowedValues("aob-injection", "attach-and-orient", "build-robust-table", "ce-tutorial-walkthrough",
			"cleanup-session", "dissect-structure", "dotnet-recon", "find-float-value", "find-known-value",
			"find-unknown-value", "find-writer", "freeze-value", "injection-copy-base", "make-aob-signature",
			"manual-pointer-chain", "nop-patch", "pointer-scan", "repair-after-update", "shared-code-filter",
			"speedhack", "trace-logic", "unity-mono-recon")]
		string workflow)
	{
		ArgumentNullException.ThrowIfNull(workflow);
		return CheatEngineKnowledge.TryReadWorkflow(workflow, out string? markdown)
			? Result(McpResourceUris.Workflow(workflow), markdown)
			: throw CheatEngineToolException.NotFound($"There is no workflow named '{Echo(workflow)}'.",
				$"Use one of the names listed by {McpResourceUris.Doc("workflows")}, such as find-writer.");
	}

	private static ReadResourceResult Document(string slug)
	{
		return Result(McpResourceUris.Doc(slug), CheatEngineKnowledge.ReadDocument(slug));
	}

	private static ReadResourceResult Result(string uri, string markdown)
	{
		return new ReadResourceResult
		{
			Contents = [new TextResourceContents { Uri = uri, MimeType = Markdown, Text = markdown }],
			TimeToLive = DocumentTimeToLive,
			CacheScope = CacheScope.Public
		};
	}

	private static string Echo(string value)
	{
		return value.Length <= 64 ? value : string.Concat(value.AsSpan(0, 64), "...");
	}
}
