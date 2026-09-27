using CheatEngine.Mcp.Tests.Support;

namespace CheatEngine.Mcp.Tests.Contract;

public sealed class SkillCatalogTests
{
	[Fact]
	public void SkillCatalogListsEveryExposedTool()
	{
		string root = FindRepositoryRoot();
		string catalog =
			File.ReadAllText(Path.Combine(root, "skills", "cheatengine-mcp", "references", "tool-catalog.md"));

		foreach (string toolName in TestComposition.GatewayTools.Select(tool => tool.Name))
		{
			Assert.Contains($"`{toolName}`", catalog);
		}
	}

	private static string FindRepositoryRoot()
	{
		DirectoryInfo? directory = new(AppContext.BaseDirectory);

		while (directory is not null)
		{
			if (File.Exists(Path.Combine(directory.FullName, "CheatEngine.Mcp.slnx")))
			{
				return directory.FullName;
			}

			directory = directory.Parent;
		}

		throw new DirectoryNotFoundException("Could not find repository root from test output directory.");
	}
}
