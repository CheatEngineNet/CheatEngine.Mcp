namespace CheatEngine.Mcp.Tests;

public sealed class SkillBundleTests
{
	[Fact]
	public void ProjectCopiesSkillBesideDllWithoutLocalMachineState()
	{
		string projectFile = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "src", "CheatEngine.Mcp", "CheatEngine.Mcp.csproj"));

		Assert.Contains("<Content Include=\"../../skills/cheatengine-mcp/**/*.*\"", projectFile);
		Assert.Contains("<CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>", projectFile);
		Assert.Contains("<Link>skills/cheatengine-mcp/%(RecursiveDir)%(Filename)%(Extension)</Link>", projectFile);
		Assert.Contains("skills/cheatengine-mcp/references/local-cheat-engine.md", projectFile);
		Assert.True(File.Exists(Path.Combine(AppContext.BaseDirectory, "skills", "cheatengine-mcp", "SKILL.md")));
		Assert.False(File.Exists(Path.Combine(AppContext.BaseDirectory, "skills", "cheatengine-mcp", "references", "local-cheat-engine.md")));
		Assert.False(
			projectFile.Contains("<EmbeddedResource Include=\"skills", StringComparison.OrdinalIgnoreCase),
			"The distributable skill must be copied beside the DLL, not embedded into it.");
	}

	[Fact]
	public void SkillCatalogListsEveryExposedTool()
	{
		string root = FindRepositoryRoot();
		string catalog = File.ReadAllText(Path.Combine(root, "skills", "cheatengine-mcp", "references", "tool-catalog.md"));

		foreach (string toolName in Gateway.GatewayToolCatalog.GetTools().Select(tool => tool.Name))
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
