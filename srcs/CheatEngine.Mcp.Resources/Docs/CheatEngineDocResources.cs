using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

using CheatEngine.Mcp.Core.Contract;
using CheatEngine.Mcp.Resources.Knowledge;

using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace CheatEngine.Mcp.Resources.Docs;

/// <summary>
///     The knowledge documents, one static (Local) resource each, plus the workflow bodies as one template. Every backend
///     and the gateway serve them without any instance; the text is the knowledge base of <c>Knowledge/</c> with its
///     relative links rewritten to <c>cheatengine://docs/…</c>.
/// </summary>
[McpServerResourceType]
public sealed class CheatEngineDocResources
{
	private const string Markdown = McpResourceUris.MarkdownMimeType;
	private const string Docs = McpResourceUris.DocsPrefix;

	/// <summary>How long a client may cache a document: the text only changes with the plugin build.</summary>
	internal static readonly TimeSpan DocumentTimeToLive = TimeSpan.FromHours(1);

	/// <summary>Getting started.</summary>
	/// <returns>The document.</returns>
	[McpServerResource(UriTemplate = Docs + "getting-started", Name = "doc_getting_started",
		Title = "Getting started", MimeType = Markdown)]
	[Description("Start here when new: what the plugin and gateway are, the first calls of a session, the settings "
				 + "that gate features, where the docs and prompts live, scope and consent, and how to release what "
				 + "MCP holds.")]
	public static ReadResourceResult GettingStarted()
	{
		return Document("getting-started");
	}

	/// <summary>Session rules and the workflow index.</summary>
	/// <returns>The document.</returns>
	[McpServerResource(UriTemplate = Docs + "workflows", Name = "doc_workflows",
		Title = "Workflows and session rules", MimeType = Markdown)]
	[Description("Read first: scope, session start, ids, scanners, jobs, cleanup, gates, errors, live resources and "
				 + "the index of guided workflows.")]
	public static ReadResourceResult Workflows()
	{
		return Document("workflows");
	}

	/// <summary>Glossary.</summary>
	/// <returns>The document.</returns>
	[McpServerResource(UriTemplate = Docs + "glossary", Name = "doc_glossary",
		Title = "Glossary", MimeType = Markdown)]
	[Description("Short definitions of Cheat Engine and MCP terms, such as AOB, code cave, pointer chain, host "
				 + "effect and job, each with the tool or workflow that uses it.")]
	public static ReadResourceResult Glossary()
	{
		return Document("glossary");
	}

	/// <summary>Address expressions and number formats.</summary>
	/// <returns>The document.</returns>
	[McpServerResource(UriTemplate = Docs + "address-expressions", Name = "doc_address_expressions",
		Title = "Address expressions and number formats", MimeType = Markdown)]
	[Description("Where Cheat Engine address expressions are accepted, their grammar and pointer notations, and the "
				 + "strict number formats some parameters and outputs use instead.")]
	public static ReadResourceResult AddressExpressions()
	{
		return Document("address-expressions");
	}

	/// <summary>Value types and data encodings.</summary>
	/// <returns>The document.</returns>
	[McpServerResource(UriTemplate = Docs + "value-types", Name = "doc_value_types",
		Title = "Value types and data encodings", MimeType = Markdown)]
	[Description("How the scan, memory and record type vocabularies map, and how games store integers, floats, "
				 + "booleans, pointers, tagged engine values, strings and big-endian emulator data in memory.")]
	public static ReadResourceResult ValueTypes()
	{
		return Document("value-types");
	}

	/// <summary>Value types and scan strategy.</summary>
	/// <returns>The document.</returns>
	[McpServerResource(UriTemplate = Docs + "value-scans", Name = "doc_value_scans",
		Title = "Value scan strategy", MimeType = Markdown)]
	[Description("Main versus named scanners, first and next scan comparisons, float precision and rounding, signed "
				 + "compares, narrowing, snapshots and sampling, one-shot value search, and verifying candidates.")]
	public static ReadResourceResult ValueScans()
	{
		return Document("value-scans");
	}

	/// <summary>When a scan finds nothing.</summary>
	/// <returns>The document.</returns>
	[McpServerResource(UriTemplate = Docs + "troubleshooting-scans", Name = "doc_troubleshooting_scans",
		Title = "When a scan finds nothing", MimeType = Markdown)]
	[Description("What to try when a value scan finds nothing or too much, or finds a value that reverts, will not "
				 + "freeze or moves: the Cheat Engine settings main inherits, emulator mapped memory and scan "
				 + "refusals.")]
	public static ReadResourceResult TroubleshootingScans()
	{
		return Document("troubleshooting-scans");
	}

	/// <summary>Pointers and pointer scans.</summary>
	/// <returns>The document.</returns>
	[McpServerResource(UriTemplate = Docs + "pointers", Name = "doc_pointers", Title = "Pointers and pointer scans",
		MimeType = Markdown)]
	[Description("Pointer notation and offset order, reading a chain, manual chains from a writer, pointer maps, "
				 + "path searches, rescans and their limits, ranking chains, storing them in records, and fallbacks.")]
	public static ReadResourceResult Pointers()
	{
		return Document("pointers");
	}

	/// <summary>Debugger methods.</summary>
	/// <returns>The document.</returns>
	[McpServerResource(UriTemplate = Docs + "debugger", Name = "doc_debugger", Title = "Debugger methods",
		MimeType = Markdown)]
	[Description("Debugger interfaces and gates, the four breakpoint slots and methods, find-what-writes captures "
				 + "and data-hit heuristics, traces, registers and stepping, and how to leave the target running.")]
	public static ReadResourceResult Debugger()
	{
		return Document("debugger");
	}

	/// <summary>Auto Assembler syntax and templates.</summary>
	/// <returns>The document.</returns>
	[McpServerResource(UriTemplate = Docs + "auto-assembler", Name = "doc_auto_assembler",
		Title = "Auto Assembler syntax and templates", MimeType = Markdown)]
	[Description("Auto Assembler syntax, commands and directives, how scripts are classified against the gates, "
				 + "what " + CheatEngineToolNames.AsmCheck + " proves, the AOB injection scaffold, code patches, "
				 + "nearby allocation and the check-apply-verify-release cycle.")]
	public static ReadResourceResult AutoAssembler()
	{
		return Document("auto-assembler");
	}

	/// <summary>x64 code injection essentials.</summary>
	/// <returns>The document.</returns>
	[McpServerResource(UriTemplate = Docs + "x64-injection", Name = "doc_x64_injection",
		Title = "x64 code injection essentials", MimeType = Markdown)]
	[Description("Registers, calling convention, flags, SSE floats, jcc encodings and same-length branch patches, "
				 + "jump sizes and nearby allocation, RIP-relative operands and whole-instruction patching for code "
				 + "injected into a game's function.")]
	public static ReadResourceResult X64Injection()
	{
		return Document("x64-injection");
	}

	/// <summary>AOB signatures.</summary>
	/// <returns>The document.</returns>
	[McpServerResource(UriTemplate = Docs + "aob-signatures", Name = "doc_aob_signatures", Title = "AOB signatures",
		MimeType = Markdown)]
	[Description("Byte patterns that survive updates: wildcard and masking rules, generating and proving "
				 + "signatures, finding a value's exact bytes, injection scaffolds and repair after an update.")]
	public static ReadResourceResult AobSignatures()
	{
		return Document("aob-signatures");
	}

	/// <summary>Windows process memory.</summary>
	/// <returns>The document.</returns>
	[McpServerResource(UriTemplate = Docs + "memory-model", Name = "doc_memory_model",
		Title = "Windows process memory", MimeType = Markdown)]
	[Description("Regions, protections, module sections, heaps, stacks and what static means in a Windows process; "
				 + "why a read fails, how to change protection safely, and which tool answers which question about "
				 + "an address.")]
	public static ReadResourceResult MemoryModel()
	{
		return Document("memory-model");
	}

	/// <summary>Structures and dissect.</summary>
	/// <returns>The document.</returns>
	[McpServerResource(UriTemplate = Docs + "structures", Name = "doc_structures", Title = "Structures and dissect",
		MimeType = Markdown)]
	[Description("Structure layouts: finding and identifying a base (RTTI), .NET, Mono, PDB and autoguess sources, "
				 + "reading and retyping fields, recognising common data, comparing instances, rename, C header "
				 + "export and cleanup.")]
	public static ReadResourceResult Structures()
	{
		return Document("structures");
	}

	/// <summary>Disassembly and code analysis.</summary>
	/// <returns>The document.</returns>
	[McpServerResource(UriTemplate = Docs + "code-analysis", Name = "doc_code_analysis",
		Title = "Disassembly and code analysis", MimeType = Markdown)]
	[Description("Reading disassembly, function graphs, dissector references and strings, imports and RIP-relative "
				 + "globals to learn what a value means, where its base comes from and where to patch.")]
	public static ReadResourceResult CodeAnalysis()
	{
		return Document("code-analysis");
	}

	/// <summary>Unity (Mono) and .NET targets.</summary>
	/// <returns>The document.</returns>
	[McpServerResource(UriTemplate = Docs + "mono-and-dotnet", Name = "doc_mono_and_dotnet",
		Title = "Unity (Mono) and .NET targets", MimeType = Markdown)]
	[Description("Choosing between the Mono and .NET collectors, what " + CheatEngineToolNames.MonoAttach + " and "
				 + "the Uses Mono option change, reading classes, fields, statics, instances and methods, and "
				 + "invoking or injecting managed code safely.")]
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
	[Description("Address-list records: types, writing-safe creation, pointer chains and +offset fields, freezing, "
				 + "script records, groups and deletes, loading and saving cheat tables with their switch checks, "
				 + "and robust tables.")]
	public static ReadResourceResult CheatTables()
	{
		return Document("cheat-tables");
	}

	/// <summary>Cheat recipes by goal.</summary>
	/// <returns>The document.</returns>
	[McpServerResource(UriTemplate = Docs + "cheat-recipes", Name = "doc_cheat_recipes",
		Title = "Cheat recipes by goal", MimeType = Markdown)]
	[Description("For each common cheat goal: where games usually store the value, the data and code routes, how to "
				 + "verify the result, the robust form and the scope limits.")]
	public static ReadResourceResult CheatRecipes()
	{
		return Document("cheat-recipes");
	}

	/// <summary>Game engines and runtimes.</summary>
	/// <returns>The document.</returns>
	[McpServerResource(UriTemplate = Docs + "game-engines", Name = "doc_game_engines",
		Title = "Game engines and runtimes", MimeType = Markdown)]
	[Description("How to identify a game's engine or runtime and predict how it stores values, with per-engine "
				 + "notes and the cases that are out of scope.")]
	public static ReadResourceResult GameEngines()
	{
		return Document("game-engines");
	}

	/// <summary>Unity IL2CPP targets.</summary>
	/// <returns>The document.</returns>
	[McpServerResource(UriTemplate = Docs + "unity-il2cpp", Name = "doc_unity_il2cpp",
		Title = "Unity IL2CPP targets", MimeType = Markdown)]
	[Description("Detecting IL2CPP, what CE's collector offers there and its host effects, method addresses, object "
				 + "and class layouts, statics, instance scans, offline dump offsets and when to stop.")]
	public static ReadResourceResult UnityIl2Cpp()
	{
		return Document("unity-il2cpp");
	}

	/// <summary>Unreal Engine 4 and 5 targets.</summary>
	/// <returns>The document.</returns>
	[McpServerResource(UriTemplate = Docs + "unreal-engine", Name = "doc_unreal_engine",
		Title = "Unreal Engine 4 and 5 targets", MimeType = Markdown)]
	[Description("Unreal Engine 4 and 5: version detection, UE5 doubles, UObject, FName and object-array layouts, "
				 + "and tool recipes that find the name pool, GUObjectArray and GWorld; every offset is unverified "
				 + "per build.")]
	public static ReadResourceResult UnrealEngine()
	{
		return Document("unreal-engine");
	}

	/// <summary>Emulator memory.</summary>
	/// <returns>The document.</returns>
	[McpServerResource(UriTemplate = Docs + "emulators", Name = "doc_emulators",
		Title = "Emulator memory", MimeType = Markdown)]
	[Description("Emulated consoles: finding the guest RAM base, letting Cheat Engine scan mapped memory, "
				 + "big-endian search, read and write, guest versus host addresses and pointers, per-emulator notes "
				 + "and dynarec pitfalls.")]
	public static ReadResourceResult Emulators()
	{
		return Document("emulators");
	}

	/// <summary>Lua in CheatEngine.Mcp.</summary>
	/// <returns>The document.</returns>
	[McpServerResource(UriTemplate = Docs + "lua", Name = "doc_lua", Title = "Lua in CheatEngine.Mcp",
		MimeType = Markdown)]
	[Description("Dedicated tools versus " + CheatEngineToolNames.LuaExecute + ": how a chunk runs and fails, how "
				 + "return values map to JSON, main-thread and shared-state rules, why Lua must never bypass a "
				 + "gate, and searching celua.txt with " + CheatEngineToolNames.LuaFindApi + ".")]
	public static ReadResourceResult Lua()
	{
		return Document("lua");
	}

	/// <summary>Cheat Engine Lua API cheat sheet.</summary>
	/// <returns>The document.</returns>
	[McpServerResource(UriTemplate = Docs + "lua-api", Name = "doc_lua_api",
		Title = "Cheat Engine Lua API cheat sheet", MimeType = Markdown)]
	[Description("Cheat Engine 7.7 Lua functions by area for " + CheatEngineToolNames.LuaExecute + " chunks: "
				 + "celua.txt signatures, danger tags, source-checked traps (offset order, breakpoint callback "
				 + "returns, readString bytes), the tool to prefer and what to avoid.")]
	public static ReadResourceResult LuaApi()
	{
		return Document("lua-api");
	}

	/// <summary>Kernel access (DBK/DBVM).</summary>
	/// <returns>The document.</returns>
	[McpServerResource(UriTemplate = Docs + "kernel", Name = "doc_kernel", Title = "Kernel access (DBK/DBVM)",
		MimeType = Markdown)]
	[Description("The DBK driver and DBVM hypervisor tools: why they are usually unavailable, the kernel access "
				 + "switch and its gaps, loading DBVM, physical memory, page watches, hazards and recovery.")]
	public static ReadResourceResult Kernel()
	{
		return Document("kernel");
	}

	/// <summary>Safety and responsible use.</summary>
	/// <returns>The document.</returns>
	[McpServerResource(UriTemplate = Docs + "safety", Name = "doc_safety", Title = "Safety and responsible use",
		MimeType = Markdown)]
	[Description("Read before the first change: allowed targets, consent, backups, what the four gates do and do "
				 + "not stop, what MCP tracks and how to undo the rest, and the DBK/DBVM and Windows-protection "
				 + "rules.")]
	public static ReadResourceResult Safety()
	{
		return Document("safety");
	}

	/// <summary>Failure kinds and recovery.</summary>
	/// <returns>The document.</returns>
	[McpServerResource(UriTemplate = Docs + "errors-and-recovery", Name = "doc_errors_and_recovery",
		Title = "Failure kinds and recovery", MimeType = Markdown)]
	[Description("The error envelope and resource error codes, every failure kind and host effect with its next "
				 + "step, timeouts, stale ids, and releasing or acknowledging owned resources without repeating a "
				 + "side effect.")]
	public static ReadResourceResult ErrorsAndRecovery()
	{
		return Document("errors-and-recovery");
	}

	/// <summary>Connection setup and troubleshooting.</summary>
	/// <returns>The document.</returns>
	[McpServerResource(UriTemplate = Docs + "connection-troubleshooting", Name = "doc_connection_troubleshooting",
		Title = "Connection setup and troubleshooting", MimeType = Markdown)]
	[Description("Plugin, gateway and AI-client setup, and what to check when " + CheatEngineToolNames.InstanceList
				 + " is empty, an instance is unavailable, or a call times out, is busy or is refused by a setting.")]
	public static ReadResourceResult ConnectionTroubleshooting()
	{
		return Document("connection-troubleshooting");
	}

	/// <summary>Configuration reference.</summary>
	/// <returns>The document.</returns>
	[McpServerResource(UriTemplate = Docs + "configuration", Name = "doc_configuration",
		Title = "Configuration reference", MimeType = Markdown)]
	[Description("Every plugin and gateway setting with its default and bounds: precedence, the capability gates "
				 + "and what each refuses, execution limits, file and table roots, environment variables and log "
				 + "files.")]
	public static ReadResourceResult Configuration()
	{
		return Document("configuration");
	}

	/// <summary>Cheat Engine tutorial map.</summary>
	/// <returns>The document.</returns>
	[McpServerResource(UriTemplate = Docs + "ce-tutorial", Name = "doc_ce_tutorial",
		Title = "Cheat Engine tutorial map", MimeType = Markdown)]
	[Description("The tutorial's steps and gtutorial's levels mapped to techniques, workflows and tools, with the "
				 + "step passwords, addresses verified on the CE 7.7 builds and a safe smoke test for a new setup.")]
	public static ReadResourceResult CeTutorial()
	{
		return Document("ce-tutorial");
	}

	/// <summary>Tool map.</summary>
	/// <returns>The document.</returns>
	[McpServerResource(UriTemplate = Docs + "tool-map", Name = "doc_tool_map", Title = "Tool map",
		MimeType = Markdown)]
	[Description("Every tool by domain with its purpose, the setting (gate) it needs and its dispatch class; the "
				 + "running schema stays the authority for parameters and results.")]
	public static ReadResourceResult ToolMap()
	{
		return Document("tool-map");
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
		[AllowedValues("aob-injection", "attach-and-orient", "build-robust-table", "call-game-function",
			"ce-tutorial-walkthrough", "cleanup-session", "compare-snapshots", "dissect-structure", "dotnet-recon",
			"emulator-memory", "engine-triage", "explain-error", "find-code-by-string", "find-entity-list", "find-flag",
			"find-float-value", "find-known-value", "find-position", "find-text", "find-timer", "find-unknown-value",
			"find-writer", "freeze-value", "group-scan", "identify-address", "injection-copy-base", "make-aob-signature",
			"manual-pointer-chain", "nop-patch", "patch-branch", "plan-cheat", "pointer-scan", "repair-after-update",
			"review-aa-script", "session-report", "shared-code-filter", "speedhack", "trace-logic",
			"unity-il2cpp-recon", "unity-mono-recon", "unreal-recon", "use-cheat-table", "write-lua-script")]
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
