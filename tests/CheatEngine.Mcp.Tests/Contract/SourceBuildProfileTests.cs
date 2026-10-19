using System.Diagnostics;
using System.Xml.Linq;

using CheatEngine.Mcp.Tests.Support;

namespace CheatEngine.Mcp.Tests.Contract;

/// <summary>Exercises source dependency boundaries without restoring or compiling fixture projects.</summary>
public sealed class SourceBuildProfileTests
{
	[Theory]
	[InlineData(true, false, "CEMCP003")]
	[InlineData(false, true, null)]
	[InlineData(false, false, null)]
	public async Task LibraryProfileRejectsDeclaredSdkReferencesButAllowsInheritedReferences(bool direct,
		bool transitive, string? diagnostic)
	{
		string root = Directory.CreateTempSubdirectory("CheatEngine.Mcp.SourceProfile-").FullName;
		try
		{
			string project = Path.Combine(root, "Profile.proj");
			const string sdkProject = "CheatEngine.SDK.Engine.csproj";
			XDocument fixture = new(new XElement("Project",
				new XElement("ItemGroup", direct
					? new XElement("ProjectReference", new XAttribute("Include", sdkProject)) : null),
				new XElement("Import", new XAttribute("Project", Path.Combine(RepositoryPaths.Root, "Directory.Build.targets"))),
				new XElement("Target", new XAttribute("Name", "AddInheritedReferences"),
					new XAttribute("BeforeTargets", "CeMcpVerifyPluginProfile"),
					new XElement("ItemGroup", transitive
						? new XElement("ProjectReference", new XAttribute("Include", sdkProject)) : null))));
			fixture.Save(project);
			ProcessStartInfo start = new("dotnet")
			{
				WorkingDirectory = RepositoryPaths.Root,
				UseShellExecute = false,
				CreateNoWindow = true,
				RedirectStandardOutput = true,
				RedirectStandardError = true
			};
			string[] arguments = ["msbuild", project, "-t:CeMcpVerifyPluginProfile", "-nologo", "-verbosity:minimal"];
			foreach (string argument in arguments)
			{
				start.ArgumentList.Add(argument);
			}
			using CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
			deadline.CancelAfter(TimeSpan.FromSeconds(30));
			using Process process = Process.Start(start)!;
			Task<string> stdout = process.StandardOutput.ReadToEndAsync(deadline.Token);
			Task<string> stderr = process.StandardError.ReadToEndAsync(deadline.Token);
			try
			{
				await process.WaitForExitAsync(deadline.Token);
				string output = await stdout + await stderr;
				if (diagnostic is null)
				{
					Assert.True(process.ExitCode == 0, output);
				}
				else
				{
					Assert.NotEqual(0, process.ExitCode);
					Assert.Contains(diagnostic, output, StringComparison.Ordinal);
				}
			}
			finally
			{
				if (!process.HasExited)
				{
					process.Kill(true);
					await process.WaitForExitAsync(CancellationToken.None);
				}
			}
		}
		finally
		{
			await TestDirectory.DeleteAsync(root);
		}
	}
}
