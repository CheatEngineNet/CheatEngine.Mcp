namespace CheatEngine.Mcp.Tests.Support;

internal static class RepositoryPaths
{
	internal static string Root
	{
		get;
	} = FindRoot();

	private static string FindRoot()
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
