using System.Diagnostics;

namespace CheatEngine.Mcp.Tests.Support;

/// <summary>
///     A folder on a local fixed drive whose path has no short-name segment (a <c>RUNNER~1</c> temp folder would be
///     refused as an 8.3 alias), holding the protected directories and the files a test creates.
/// </summary>
internal sealed class McpFileScratch : IDisposable
{
	internal McpFileScratch()
	{
		string temp = Path.GetFullPath(Path.GetTempPath());
		string parent = temp.Contains('~', StringComparison.Ordinal)
			? Path.Combine(RepositoryPaths.Root, "artifacts", "test-scratch")
			: temp;
		Root = Path.Combine(parent, $"CheatEngine.Mcp.FilePaths-{Guid.NewGuid():N}");
		Directory.CreateDirectory(Root);
	}

	internal string Root
	{
		get;
	}

	private List<string> Junctions
	{
		get;
	} = [];

	public void Dispose()
	{
		// A recursive deletion refuses junctions; removing one alone never touches its target.
		foreach (string link in Junctions)
		{
			Directory.Delete(link, false);
		}

		if (Directory.Exists(Root))
		{
			Directory.Delete(Root, true);
		}
	}

	internal string CreateFolder(string relative)
	{
		return Directory.CreateDirectory(Path.Combine(Root, relative)).FullName;
	}

	/// <summary>Creates a directory junction, which needs no privilege, unlike a symbolic link.</summary>
	internal string CreateJunction(string relative, string target)
	{
		string link = Path.Combine(Root, relative);
		ProcessStartInfo start = new("cmd.exe")
		{
			UseShellExecute = false,
			CreateNoWindow = true,
			RedirectStandardOutput = true,
			RedirectStandardError = true
		};
		foreach (string argument in (string[]) ["/d", "/c", "mklink", "/J", link, target])
		{
			start.ArgumentList.Add(argument);
		}

		using Process process = Process.Start(start)!;
		string output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
		process.WaitForExit();
		Assert.True(process.ExitCode == 0 && Directory.Exists(link), output);
		Assert.True((File.GetAttributes(link) & FileAttributes.ReparsePoint) != 0);
		Junctions.Add(link);
		return link;
	}
}
