using System.Collections.Frozen;
using System.Diagnostics.CodeAnalysis;

namespace CheatEngine.Mcp.Resources.Knowledge;

/// <summary>
///     The embedded knowledge documents and workflow bodies, loaded once with their relative links rewritten to
///     <c>cheatengine://docs/…</c>. The files come from <c>skills/cheatengine-mcp/references/</c> by MSBuild link.
/// </summary>
public static class CheatEngineKnowledge
{
	private static readonly FrozenDictionary<string, string> Documents =
		McpKnowledgeText.Load(typeof(CheatEngineKnowledge).Assembly, McpKnowledgeText.DocumentsLogicalPrefix);

	private static readonly FrozenDictionary<string, string> Workflows =
		McpKnowledgeText.Load(typeof(CheatEngineKnowledge).Assembly, McpKnowledgeText.WorkflowsLogicalPrefix);

	/// <summary>The served document slugs, in reading order; the project file embeds exactly these.</summary>
	public static IReadOnlyList<string> DocumentSlugs
	{
		get;
	} =
	[
		"workflows", "value-scans", "pointers", "debugger", "auto-assembler", "x64-injection", "aob-signatures",
		"structures", "code-analysis", "mono-and-dotnet", "speedhack", "cheat-tables", "lua", "kernel", "safety",
		"errors-and-recovery", "connection-troubleshooting", "ce-tutorial"
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
