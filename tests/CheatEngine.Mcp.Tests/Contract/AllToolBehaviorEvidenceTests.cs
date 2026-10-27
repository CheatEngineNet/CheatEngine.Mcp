using System.Reflection;
using System.Text.Json;

using CheatEngine.Mcp.Core.Contract;
using CheatEngine.Mcp.Tests.Support;

namespace CheatEngine.Mcp.Tests.Contract;

/// <summary>New or renamed tools must retain reviewed, executable positive adapter evidence.</summary>
public sealed class AllToolBehaviorEvidenceTests
{
	private static readonly Dictionary<string, JsonElement> Evidence = ReadEvidence();
	private static readonly string[] Suites = ["portable", "nativeLua"];

	public static IEnumerable<object[]> BackendTools =>
		McpPrimitiveCatalog.Create(TestComposition.BackendManifest).Tools
			.Select(static tool => new object[] { tool.Name });

	[Fact]
	public void BehaviorEvidence_NamesMatchTheProductionBackendCatalogExactly()
	{
		string[] expected = McpPrimitiveCatalog.Create(TestComposition.BackendManifest).Tools
			.Select(static tool => tool.Name).Order(StringComparer.Ordinal).ToArray();

		Assert.NotEmpty(expected);
		Assert.Equal(expected, Evidence.Keys.Order(StringComparer.Ordinal));
	}

	[Theory]
	[MemberData(nameof(BackendTools))]
	public void BackendTool_HasReviewedAdapterEvidenceReferencingExistingTests(string toolName)
	{
		Assert.True(Evidence.TryGetValue(toolName, out JsonElement tool),
			$"{toolName} needs a reviewed positive adapter case in behavior-evidence.json.");
		JsonElement[] cases = tool.GetProperty("cases").EnumerateArray().ToArray();
		Assert.Equal(cases.Length, cases.Select(static entry =>
			(entry.GetProperty("suite").GetString(), entry.GetProperty("scope").GetString(),
				entry.GetProperty("testMethod").GetString())).Distinct().Count());
		JsonElement[] adapters = cases
			.Where(static entry => entry.GetProperty("scope").GetString() == "adapter").ToArray();
		Assert.NotEmpty(adapters);
		foreach (JsonElement entry in adapters)
		{
			string methodName = entry.GetProperty("testMethod").GetString()!;
			int separator = methodName.LastIndexOf('.');
			Assert.True(separator > 0, $"{toolName} has an invalid test method: {methodName}");
			Type? type = typeof(AllToolBehaviorEvidenceTests).Assembly.GetType(methodName[..separator]);
			Assert.NotNull(type);
			MethodInfo? method = type.GetMethod(methodName[(separator + 1)..], BindingFlags.Public | BindingFlags.Instance);
			Assert.NotNull(method);
			Assert.Contains(method.GetCustomAttributes(), static attribute => attribute is FactAttribute);
			Assert.False(string.IsNullOrWhiteSpace(entry.GetProperty("oracle").GetString()));
			Assert.Contains(entry.GetProperty("suite").GetString(), Suites);

			string source = Path.GetFullPath(Path.Combine(RepositoryPaths.Root, entry.GetProperty("sourceFile").GetString()!));
			Assert.StartsWith(Path.TrimEndingDirectorySeparator(RepositoryPaths.Root) + Path.DirectorySeparatorChar,
				source, StringComparison.OrdinalIgnoreCase);
			Assert.True(File.Exists(source), $"{toolName} references a missing test source: {source}");

			string[] required = entry.GetProperty("requiredCases").EnumerateArray()
				.Select(static item => item.GetString()!).ToArray();
			Assert.NotEmpty(required);
			Assert.Equal(required.Length, required.Distinct(StringComparer.Ordinal).Count());
			Assert.All(required, name => Assert.True(name == methodName ||
				name.StartsWith(methodName + "(", StringComparison.Ordinal),
				$"{toolName} has a required case outside its mapped test: {name}"));
		}
	}

	private static Dictionary<string, JsonElement> ReadEvidence()
	{
		using JsonDocument document = JsonDocument.Parse(File.ReadAllText(Path.Combine(RepositoryPaths.Root,
			"docs", "qualification", "all-tools", "behavior-evidence.json")));
		return document.RootElement.GetProperty("tools").EnumerateArray()
			.ToDictionary(static tool => tool.GetProperty("name").GetString()!, static tool => tool.Clone(), StringComparer.Ordinal);
	}
}
