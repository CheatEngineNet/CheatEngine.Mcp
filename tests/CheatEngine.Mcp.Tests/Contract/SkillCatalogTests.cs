using System.Text.RegularExpressions;

using CheatEngine.Mcp.Core.Contract;
using CheatEngine.Mcp.Tests.Support;

namespace CheatEngine.Mcp.Tests.Contract;

/// <summary>
///     The operator skill's tool catalog: the frozen v2 names, then the legacy tools still served, so each migration batch
///     that removes a legacy tool also removes its row.
/// </summary>
public sealed partial class SkillCatalogTests
{
	private const string LegacyHeading = "## Legacy tools (removed by 2.0.0)";

	[Fact]
	public void SkillCatalog_V2Section_ListsEveryFrozenNameOnce()
	{
		string[] v2 = ReadCatalog().V2;

		Assert.Equal(v2.Length, v2.Distinct(StringComparer.Ordinal).Count());
		Assert.Equal(CheatEngineToolNames.All.Order(StringComparer.Ordinal), v2.Order(StringComparer.Ordinal));
	}

	[Fact]
	public void SkillCatalog_LegacySection_ListsExactlyTheLegacyToolsStillServed()
	{
		string[] legacy = ReadCatalog().Legacy;
		IEnumerable<string> served = McpPrimitiveCatalog.Create(TestComposition.BackendManifest).Tools
			.Where(McpContractRules.IsLegacy).Select(static tool => tool.Name);

		Assert.Equal(legacy.Length, legacy.Distinct(StringComparer.Ordinal).Count());
		Assert.Equal(served.Order(StringComparer.Ordinal), legacy.Order(StringComparer.Ordinal));
	}

	[Fact]
	public void SkillCatalog_ListsEveryExposedTool()
	{
		(string[] v2, string[] legacy) = ReadCatalog();

		Assert.Empty(TestComposition.GatewayTools.Select(static tool => tool.Name).Except(v2.Concat(legacy)));
	}

	private static (string[] V2, string[] Legacy) ReadCatalog()
	{
		string[] lines = File.ReadAllLines(Path.Combine(RepositoryPaths.Root, "skills", "cheatengine-mcp", "references",
			"tool-catalog.md"));
		int legacyHeading = Array.IndexOf(lines, LegacyHeading);
		int split = legacyHeading < 0 ? lines.Length : legacyHeading;
		return (Rows(lines.Take(split)), Rows(lines.Skip(split)));
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
