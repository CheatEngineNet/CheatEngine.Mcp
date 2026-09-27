using System.Collections.Frozen;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;

namespace CheatEngine.Mcp.Core.Composition;

/// <summary>
///     Reads the operator skill's knowledge files embedded in a primitive assembly and rewrites their relative Markdown
///     links to <c>cheatengine://docs/…</c> URIs, so the same files work from disk (the skill) and over MCP (resources and
///     prompts).
/// </summary>
/// <remarks>
///     The files are embedded by MSBuild link from <c>skills/cheatengine-mcp/references/</c> with the logical names
///     <see cref="DocumentsLogicalPrefix" /><c>{slug}.md</c> and <see cref="WorkflowsLogicalPrefix" /><c>{workflow}.md</c>
///     , mirroring the two folders the relative links assume.
/// </remarks>
public static partial class McpKnowledgeText
{
	/// <summary>The logical-name prefix of an embedded knowledge document (<c>references/*.md</c>).</summary>
	public const string DocumentsLogicalPrefix = "CheatEngine.Mcp.Knowledge/docs/";

	/// <summary>The logical-name prefix of an embedded workflow body (<c>references/workflows/*.md</c>).</summary>
	public const string WorkflowsLogicalPrefix = "CheatEngine.Mcp.Knowledge/workflows/";

	private const string MarkdownExtension = ".md";
	private const string WorkflowsFolder = "workflows/";

	/// <summary>
	///     Loads every embedded file under one logical prefix, keyed by file name without extension, with its links
	///     rewritten for the folder the prefix mirrors.
	/// </summary>
	/// <param name="assembly">The assembly that embeds the files.</param>
	/// <param name="logicalPrefix"><see cref="DocumentsLogicalPrefix" /> or <see cref="WorkflowsLogicalPrefix" />.</param>
	/// <returns>The rewritten Markdown by name, with LF line endings.</returns>
	/// <exception cref="ArgumentException">The prefix is neither knowledge prefix.</exception>
	public static FrozenDictionary<string, string> Load(Assembly assembly, string logicalPrefix)
	{
		ArgumentNullException.ThrowIfNull(assembly);
		bool workflows = logicalPrefix switch
		{
			DocumentsLogicalPrefix => false,
			WorkflowsLogicalPrefix => true,
			_ => throw new ArgumentException($"'{logicalPrefix}' is not a knowledge prefix.", nameof(logicalPrefix))
		};
		Dictionary<string, string> files = new(StringComparer.Ordinal);
		foreach (string name in assembly.GetManifestResourceNames())
		{
			if (!name.StartsWith(logicalPrefix, StringComparison.Ordinal) ||
				!name.EndsWith(MarkdownExtension, StringComparison.Ordinal))
			{
				continue;
			}

			using Stream stream = assembly.GetManifestResourceStream(name)!;
			using StreamReader reader = new(stream, Encoding.UTF8, true);
			string key = name[logicalPrefix.Length..^MarkdownExtension.Length];
			// LF only: the served text must not depend on how the checkout stored line endings.
			files.Add(key, RewriteLinks(reader.ReadToEnd().ReplaceLineEndings("\n"), workflows));
		}

		return files.ToFrozenDictionary(StringComparer.Ordinal);
	}

	/// <summary>
	///     Rewrites relative Markdown links to knowledge files: <c>](slug.md#part)</c> becomes
	///     <c>](cheatengine://docs/slug#part)</c> and <c>](workflows/name.md)</c> becomes
	///     <c>](cheatengine://docs/workflows/name)</c>. A link that leaves the two knowledge folders, and every absolute
	///     link, is kept unchanged.
	/// </summary>
	/// <param name="markdown">The file's Markdown.</param>
	/// <param name="inWorkflowsFolder">Whether the file lives in <c>references/workflows/</c>.</param>
	/// <returns>The Markdown with its knowledge links rewritten.</returns>
	public static string RewriteLinks(string markdown, bool inWorkflowsFolder)
	{
		ArgumentNullException.ThrowIfNull(markdown);
		return RelativeMarkdownLink().Replace(markdown, match =>
		{
			string? uri = Resolve(match.Groups["path"].Value, inWorkflowsFolder);
			return uri is null
				? match.Value
				: $"]({uri}{match.Groups["fragment"].Value})";
		});
	}

	/// <summary>Resolves a relative link path without extension to its knowledge URI.</summary>
	/// <param name="path">The link path, such as <c>../debugger</c> or <c>workflows/find-writer</c>.</param>
	/// <param name="inWorkflowsFolder">Whether the linking file lives in <c>references/workflows/</c>.</param>
	/// <returns>The URI, or <see langword="null" /> when the link leaves the knowledge folders.</returns>
	private static string? Resolve(string path, bool inWorkflowsFolder)
	{
		// A leading slash names a site-root or protocol-relative path, not a file relative to the knowledge folder.
		if (path.StartsWith('/'))
		{
			return null;
		}

		List<string> segments = inWorkflowsFolder ? ["workflows"] : [];
		foreach (string segment in path.Split('/'))
		{
			switch (segment)
			{
				case "" or ".":
					continue;
				case "..":
					if (segments.Count == 0)
					{
						return null;
					}

					segments.RemoveAt(segments.Count - 1);
					break;
				default:
					segments.Add(segment);
					break;
			}
		}

		string resolved = string.Join('/', segments);
		return segments.Count switch
		{
			1 => McpResourceUris.Doc(resolved),
			2 when resolved.StartsWith(WorkflowsFolder, StringComparison.Ordinal) => McpResourceUris.Workflow(
				segments[1]),
			_ => null
		};
	}

	[GeneratedRegex(@"\]\((?<path>(?![a-zA-Z][a-zA-Z0-9+.-]*:)[^)\s#]+?)\.md(?<fragment>#[^)\s]*)?\)",
		RegexOptions.CultureInvariant)]
	private static partial Regex RelativeMarkdownLink();
}
