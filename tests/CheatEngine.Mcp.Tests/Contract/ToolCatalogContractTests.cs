using System.Collections.Frozen;
using System.Reflection;
using System.Text.Json.Nodes;

using CheatEngine.Mcp.Core.Contract;
using CheatEngine.Mcp.Core.Features;
using CheatEngine.Mcp.Tests.Support;

using ModelContextProtocol.Protocol;

namespace CheatEngine.Mcp.Tests.Contract;

/// <summary>
///     The reviewed v2 catalog: the names, their fit with the naming rules, the map from every pre-v2 name, the reviewed
///     open-world list and the per-tool summary that the reference documentation is generated from.
/// </summary>
public sealed class ToolCatalogContractTests
{
	private const string ClientToolPrefix = "mcp__cheatengine__";
	private const int ClientToolNameLimit = 64;

	/// <summary>Plan section 2: the tool count of every domain.</summary>
	public static TheoryData<string, int> DomainCounts => new()
	{
		{ "instance", 1 },
		{ "runtime", 6 },
		{ "process", 9 },
		{ "memory", 19 },
		{ "scan", 8 },
		{ "aob", 3 },
		{ "pointer", 16 },
		{ "module", 5 },
		{ "symbol", 10 },
		{ "speedhack", 2 },
		{ "util", 2 },
		{ "code", 14 },
		{ "asm", 8 },
		{ "record", 14 },
		{ "table", 3 },
		{ "structure", 15 },
		{ "debugger", 18 },
		{ "exec", 7 },
		{ "dotnet", 10 },
		{ "mono", 15 },
		{ "kernel", 7 },
		{ "lua", 2 }
	};

	[Fact]
	public void ToolNames_Constants_Are194UniqueNamesSplitBetweenBackendAndGateway()
	{
		string[] constants = typeof(CheatEngineToolNames).GetFields(BindingFlags.Public | BindingFlags.Static)
			.Where(static field => field.IsLiteral)
			.Select(static field => (string) field.GetRawConstantValue()!)
			.ToArray();

		Assert.Equal(194, constants.Length);
		Assert.Equal(constants.Order(StringComparer.Ordinal), CheatEngineToolNames.All.Order(StringComparer.Ordinal));
		Assert.Equal(193, CheatEngineToolNames.Backend.Count);
		Assert.Equal([CheatEngineToolNames.InstanceList],
			CheatEngineToolNames.All.Except(CheatEngineToolNames.Backend));
	}

	[Fact]
	public void ToolNames_Domains_AreExactlyThePlanDomains()
	{
		Assert.Equal(DomainCounts.Select(static row => row.Data.Item1).Order(StringComparer.Ordinal),
			CheatEngineToolNames.All.Select(Domain).Distinct().Order(StringComparer.Ordinal));
	}

	[Theory]
	[MemberData(nameof(DomainCounts))]
	public void ToolNames_Domain_HasThePlanCount(string domain, int count)
	{
		Assert.Equal(count, CheatEngineToolNames.All.Count(name => Domain(name) == domain));
	}

	[Fact]
	public void ToolNames_EveryName_FollowsTheNamingRules()
	{
		Assert.All(CheatEngineToolNames.All, static name =>
		{
			Assert.True(McpContractRules.IsSnakeName(name), name);
			Assert.InRange(name.Length, 1, McpContractRules.MaxToolNameLength);
			FrozenSet<string> domains = name == CheatEngineToolNames.InstanceList
				? McpContractRules.GatewayDomains
				: McpContractRules.Domains;
			Assert.True(domains.Contains(Domain(name)), name);
			Assert.True(McpContractRules.Verbs.Contains(Verb(name)), name);
			Assert.InRange((ClientToolPrefix + name).Length, 1, ClientToolNameLimit);
		});
	}

	[Fact]
	public void ToolNames_ClosedVerbList_HasNoUnusedVerb()
	{
		Assert.Empty(McpContractRules.Verbs.Except(CheatEngineToolNames.All.Select(Verb))
			.Order(StringComparer.Ordinal));
	}

	[Fact]
	public void HistoricToolNames_Fixture_ListsEveryMigrationSource()
	{
		string[] legacy = ReadLines("legacy-tool-names.txt");

		Assert.Equal(191, legacy.Length);
		Assert.Equal(legacy.Order(StringComparer.Ordinal).Distinct(StringComparer.Ordinal), legacy);
	}

	[Fact]
	public void LegacyToolMap_OldNames_AreTheFixtureOnceEachInOrder()
	{
		Assert.Equal(ReadLines("legacy-tool-names.txt"), ReadMap().Select(static entry => entry.OldName));
	}

	[Fact]
	public void LegacyToolMap_Targets_AreFrozenNamesAndMatchTheirKind()
	{
		MapEntry[] map = ReadMap();
		Dictionary<string, int> sources = map.SelectMany(static entry => entry.Targets)
			.GroupBy(static target => target, StringComparer.Ordinal)
			.ToDictionary(static group => group.Key, static group => group.Count(), StringComparer.Ordinal);

		Assert.All(map, entry =>
		{
			Assert.All(entry.Targets, static target => Assert.True(CheatEngineToolNames.All.Contains(target), target));
			Assert.Equal(entry.Targets.Length, entry.Targets.Distinct(StringComparer.Ordinal).Count());
			Assert.False(string.IsNullOrWhiteSpace(entry.Note), $"{entry.OldName} has no note.");
			string expected = entry.Targets switch
			{
				[] => "removed",
				_ when entry.Targets.Contains(entry.OldName) => "changed_in_place",
				[_, _, ..] => "split",
				[{ } target] when sources[target] > 1 => "merged",
				_ => "renamed"
			};
			Assert.True(expected == entry.Kind,
				$"{entry.OldName} is {entry.Kind}, but its targets make it {expected}.");
			// A kept name must be the same tool: an old name may reappear in v2 only as its own replacement.
			Assert.True(!CheatEngineToolNames.All.Contains(entry.OldName) || entry.Kind == "changed_in_place",
				$"{entry.OldName} is a v2 name, so it must be changed in place.");
		});
	}

	[Fact]
	public void LegacyToolMap_UntargetedV2Names_AreTheReviewedNetNewTools()
	{
		HashSet<string> targeted =
			ReadMap().SelectMany(static entry => entry.Targets).ToHashSet(StringComparer.Ordinal);
		string[] netNew = ReadLines("net-new-tools.txt");

		Assert.Equal(netNew.Order(StringComparer.Ordinal).Distinct(StringComparer.Ordinal), netNew);
		Assert.Equal(netNew, CheatEngineToolNames.All.Where(name => !targeted.Contains(name))
			.Order(StringComparer.Ordinal));
	}

	[Fact]
	public void OpenWorldTools_Fixture_ListsFrozenBackendNamesOnceInOrder()
	{
		string[] openWorld = ReadLines("open-world-tools.txt");

		Assert.NotEmpty(openWorld);
		Assert.Equal(openWorld.Order(StringComparer.Ordinal).Distinct(StringComparer.Ordinal), openWorld);
		Assert.All(openWorld, static name => Assert.True(CheatEngineToolNames.Backend.Contains(name), name));
		Assert.DoesNotContain(CheatEngineToolNames.SpeedhackSetSpeed, openWorld);
	}

	[Fact]
	public void RegisteredV2Tools_OpenWorldAnnotation_MatchesTheReviewedList()
	{
		HashSet<string> openWorld = ReadLines("open-world-tools.txt").ToHashSet(StringComparer.Ordinal);

		Assert.All(BackendCatalog(), tool =>
			Assert.True(tool.Annotations?.OpenWorldHint == openWorld.Contains(tool.Name),
				$"{tool.Name} sets OpenWorld={tool.Annotations?.OpenWorldHint}, but open-world-tools.txt says " +
				$"{openWorld.Contains(tool.Name)}."));
	}

	[Fact]
	public void ToolSummary_BackendAndGatewayListing_MatchesGoldenSnapshot()
	{
		Tool instanceList =
			TestComposition.GatewayTools.Single(static tool => tool.Name == CheatEngineToolNames.InstanceList);
		IEnumerable<string> lines = BackendCatalog().Select(static tool => Summarize(tool, false))
			.Append(Summarize(instanceList, true))
			.Order(StringComparer.Ordinal);

		string[] summary =
		[
			"# name | title | readOnly,destructive,idempotent,openWorld | dispatchClass | requires",
			"# Generated from the backend tools/list and the gateway's instance_list.",
			.. lines
		];
		GoldenFile.AssertMatches("tool-summary.txt", string.Join('\n', summary));
	}

	private static string Summarize(Tool tool, bool gatewayLocal)
	{
		JsonArray? requires = tool.Meta?[McpFeatureGate.RequiresMetaKey] as JsonArray;
		string required = requires is { Count: > 0 }
			? string.Join('+', requires.Select(static feature => feature!.GetValue<string>()))
			: "-";
		ToolAnnotations? annotations = tool.Annotations;
		string hints = string.Join(',', Hint(annotations?.ReadOnlyHint), Hint(annotations?.DestructiveHint),
			Hint(annotations?.IdempotentHint), Hint(annotations?.OpenWorldHint));
		string dispatchClass = gatewayLocal
			? "gateway"
			: tool.Meta?[McpDispatchClass.MetaKey]?.GetValue<string>() ?? "unset";
		return $"{tool.Name} | {tool.Title} | {hints} | {dispatchClass} | {required}";
	}

	private static string Hint(bool? value)
	{
		return value switch
		{
			true => "true",
			false => "false",
			null => "unset"
		};
	}

	private static IReadOnlyList<Tool> BackendCatalog()
	{
		return McpPrimitiveCatalog.Create(TestComposition.BackendManifest).Tools;
	}

	private static string Domain(string name)
	{
		return name.Split('_')[0];
	}

	private static string Verb(string name)
	{
		return name.Split('_')[1];
	}

	private static string[] ReadLines(string golden)
	{
		return File.ReadAllLines(Path.Combine(RepositoryPaths.Root, "tests", "CheatEngine.Mcp.Tests", "Contract",
				"Golden", golden))
			.Where(static line => line.Length > 0 && !line.StartsWith('#'))
			.ToArray();
	}

	private static MapEntry[] ReadMap()
	{
		return ReadLines("legacy-tool-map.txt").Select(static line =>
		{
			string[] columns = line.Split(" | ");
			Assert.True(columns.Length == 4, $"'{line}' must have four columns.");
			string[] targets = columns[2] == "-" ? [] : columns[2].Split(", ");
			return new MapEntry(columns[0], columns[1], targets, columns[3]);
		}).ToArray();
	}

	private sealed record MapEntry(string OldName, string Kind, string[] Targets, string Note);
}
