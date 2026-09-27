using System.Text.RegularExpressions;

using CheatEngine.Mcp.Core.Contract;
using CheatEngine.Mcp.Tests.Support;

namespace CheatEngine.Mcp.Tests.Contract;

/// <summary>
///     The operator skill's tool catalog: the frozen v2 names and a separate historic-name migration table. Historic
///     names are documentation, not registered tools.
/// </summary>
public sealed partial class SkillCatalogTests
{
	private const string HistoricMigrationHeading = "## Historic name migration";

	[Fact]
	public void SkillCatalog_V2Section_ListsEveryFrozenNameOnce()
	{
		string[] v2 = ReadCatalog();

		Assert.Equal(v2.Length, v2.Distinct(StringComparer.Ordinal).Count());
		Assert.Equal(CheatEngineToolNames.All.Order(StringComparer.Ordinal), v2.Order(StringComparer.Ordinal));
	}

	[Fact]
	public void SkillCatalog_ListsEveryExposedTool()
	{
		string[] v2 = ReadCatalog();

		Assert.Empty(TestComposition.GatewayTools.Select(static tool => tool.Name).Except(v2));
	}

	private static string[] ReadCatalog()
	{
		string[] lines = File.ReadAllLines(Path.Combine(RepositoryPaths.Root, "skills", "cheatengine-mcp", "references",
			"tool-catalog.md"));
		int historicHeading = Array.IndexOf(lines, HistoricMigrationHeading);
		int split = historicHeading < 0 ? lines.Length : historicHeading;
		// The migration mapping is intentionally not parsed as a tool list: no historic tool is registered.
		return Rows(lines.Take(split));
	}

	// Only a table row's first cell names a tool; prose may cite tools freely.
	private static string[] Rows(IEnumerable<string> lines)
	{
		return lines.Select(static line => ToolRow().Match(line)).Where(static match => match.Success)
			.Select(static match => match.Groups[1].Value).ToArray();
	}

	[GeneratedRegex(@"^\| `([^`]+)` \|", RegexOptions.CultureInvariant)]
	private static partial Regex ToolRow();
}
