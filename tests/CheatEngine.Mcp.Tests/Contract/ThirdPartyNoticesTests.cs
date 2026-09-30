using System.Text.Json;

using CheatEngine.Mcp.Tests.Support;

namespace CheatEngine.Mcp.Tests.Contract;

/// <summary>Keeps THIRD-PARTY-NOTICES.md in step with the packages that the plugin folder and the gateway ship.</summary>
public sealed class ThirdPartyNoticesTests
{
	[Theory]
	[InlineData("CheatEngine.Mcp.Plugin")]
	[InlineData("CheatEngine.Mcp.Gateway")]
	public void ThirdPartyNotices_ShippedPackages_AreEachListed(string project)
	{
		string notices = ReadNotices();
		string[] unlisted = ShippedPackages(project)
			.Where(package => !notices.Contains($"| {package.Id} | {package.Version} |", StringComparison.Ordinal))
			.Select(static package => $"{package.Id} {package.Version}")
			.ToArray();
		Assert.True(unlisted.Length == 0,
			$"THIRD-PARTY-NOTICES.md has no '| <id> | <version> |' row for: {string.Join(", ", unlisted)}");
	}

	[Theory]
	[InlineData("NLog")]
	public void ThirdPartyNotices_RemovedPackage_IsNotListed(string package)
	{
		Assert.DoesNotContain(package, ReadNotices(), StringComparison.Ordinal);
	}

	private static string ReadNotices()
	{
		return File.ReadAllText(Path.Combine(RepositoryPaths.Root, "THIRD-PARTY-NOTICES.md"));
	}

	private static List<(string Id, string Version)> ShippedPackages(string project)
	{
		// Tests run after the solution build, so the project output of the test's own configuration must exist.
		string configuration = Path.GetFileName(Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory));
		string assemblyName = project == "CheatEngine.Mcp.Plugin" ? "CheatEngine.Mcp" : project;
		string deps = Path.Combine(RepositoryPaths.Root, "artifacts", "bin", project, configuration,
			assemblyName + ".deps.json");
		Assert.True(File.Exists(deps), $"The build output is missing: {deps}");
		using JsonDocument document = JsonDocument.Parse(File.ReadAllText(deps));
		List<(string Id, string Version)> packages = [];
		foreach (JsonProperty library in document.RootElement.GetProperty("libraries").EnumerateObject())
		{
			if (library.Value.GetProperty("type").GetString() == "package")
			{
				int separator = library.Name.LastIndexOf('/');
				packages.Add((library.Name[..separator], library.Name[(separator + 1)..]));
			}
		}

		Assert.NotEmpty(packages);
		return packages;
	}
}
