using System.Collections.Frozen;
using System.Diagnostics.CodeAnalysis;

namespace CheatEngine.Mcp.Resources.Knowledge;

/// <summary>
///     The knowledge base: the documents of <c>Knowledge/Documents/</c> and the guided workflow bodies of
///     <c>Knowledge/Workflows/</c>, embedded in this assembly and loaded once with their relative links rewritten to
///     <c>cheatengine://docs/…</c>. The Resources project serves both; the Prompts project renders the workflow bodies.
/// </summary>
public static class CheatEngineKnowledge
{
	private static readonly FrozenDictionary<string, string> Documents =
		McpKnowledgeText.Load(typeof(CheatEngineKnowledge).Assembly, McpKnowledgeText.DocumentsLogicalPrefix);

	private static readonly FrozenDictionary<string, string> Workflows =
		McpKnowledgeText.Load(typeof(CheatEngineKnowledge).Assembly, McpKnowledgeText.WorkflowsLogicalPrefix);

	/// <summary>
	///     The served document slugs, in reading order: orientation first, then values, pointers and code, then engines
	///     and runtimes, then reference material. A test keeps them equal to the embedded documents.
	/// </summary>
	public static IReadOnlyList<string> DocumentSlugs
	{
		get;
	} =
	[
		"getting-started", "workflows", "safety", "glossary", "address-expressions", "value-types", "value-scans",
		"troubleshooting-scans", "pointers", "memory-model", "structures", "code-analysis", "debugger",
		"x64-injection", "auto-assembler", "aob-signatures", "cheat-tables", "cheat-recipes", "game-engines",
		"mono-and-dotnet", "unity-il2cpp", "unreal-engine", "emulators", "speedhack", "lua", "lua-api", "kernel",
		"errors-and-recovery", "connection-troubleshooting", "configuration", "ce-tutorial", "tool-map"
	];

	/// <summary>The embedded workflow names (kebab case), ordered; each is the body of the prompt of the same name.</summary>
	public static IReadOnlyList<string> WorkflowNames
	{
		get;
	} = [.. Workflows.Keys.Order(StringComparer.Ordinal)];

	/// <summary>The names of every embedded document, whether or not it is served.</summary>
	internal static IReadOnlyCollection<string> EmbeddedDocuments => Documents.Keys;

	/// <summary>Returns a document's Markdown with its links rewritten.</summary>
	/// <param name="slug">The document slug.</param>
	/// <returns>The Markdown.</returns>
	/// <exception cref="KeyNotFoundException">No document has that slug.</exception>
	public static string ReadDocument(string slug)
	{
		ArgumentNullException.ThrowIfNull(slug);
		return Documents[slug];
	}

	/// <summary>Returns a workflow body with its links rewritten.</summary>
	/// <param name="workflow">The kebab-case workflow name.</param>
	/// <param name="markdown">The Markdown, when the workflow exists.</param>
	/// <returns><see langword="true" /> when the workflow exists.</returns>
	public static bool TryReadWorkflow(string workflow,
		[NotNullWhen(true)] out string? markdown)
	{
		ArgumentNullException.ThrowIfNull(workflow);
		return Workflows.TryGetValue(workflow, out markdown);
	}
}
