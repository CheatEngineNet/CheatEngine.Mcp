using System.Text.RegularExpressions;

using CheatEngine.Mcp.Core.Contract;
using CheatEngine.Mcp.Tests.Support;

namespace CheatEngine.Mcp.Tests.Resources;

/// <summary>
///     The served tool map (<c>cheatengine://docs/tool-map</c>): one table row per frozen v2 tool name, so the map can
///     never drift from the catalog the gateway and backends expose.
/// </summary>
public sealed partial class ToolMapDocumentTests
{
	[Fact]
	public void ToolMap_Rows_ListEveryFrozenNameOnce()
	{
		string[] rows = Rows(ToolMap());

		Assert.Equal(rows.Length, rows.Distinct(StringComparer.Ordinal).Count());
		Assert.Equal(CheatEngineToolNames.All.Order(StringComparer.Ordinal), rows.Order(StringComparer.Ordinal));
	}

	[Fact]
	public void ToolMap_Rows_ListEveryExposedTool()
	{
		string[] rows = Rows(ToolMap());

		Assert.Empty(TestComposition.GatewayTools.Select(static tool => tool.Name).Except(rows));
	}

	// Read from disk, so the rows are checked as written, without a rebuild.
	private static string ToolMap()
	{
		return File.ReadAllText(Path.Combine(KnowledgeResourceTests.KnowledgeFolder, McpKnowledgeText.DocumentsFolder,
			"tool-map.md")).ReplaceLineEndings("\n");
	}

	// Only a table row's first cell names a tool; prose may cite tools freely.
	private static string[] Rows(string markdown)
	{
		return markdown.Split('\n').Select(static line => ToolRow().Match(line))
			.Where(static match => match.Success)
			.Select(static match => match.Groups[1].Value).ToArray();
	}

	[GeneratedRegex(@"^\| `([^`]+)` \|", RegexOptions.CultureInvariant)]
	private static partial Regex ToolRow();
}
