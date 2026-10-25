using System.Diagnostics;
using System.Text.Json.Nodes;
using System.Xml.Linq;

using CheatEngine.Mcp.Tests.Support;

namespace CheatEngine.Mcp.Tests.Contract;

/// <summary>Exercises source dependency boundaries without restoring or compiling fixture projects.</summary>
public sealed class SourceBuildProfileTests
{
	[Theory]
	[InlineData("Debug")]
	[InlineData("Release")]
	public async Task SourceReferencesUseOnePlatformAndConfigurationForDirectAndTransitiveDependencies(string configuration)
	{
		string root = Directory.CreateTempSubdirectory("CheatEngine.Mcp.SourceConfiguration-").FullName;
		try
		{
			string project = Path.Combine(root, "Configuration.proj");
			XDocument fixture = new(new XElement("Project",
				new XElement("PropertyGroup", new XElement("Configuration", configuration)),
				new XElement("ItemGroup",
					new XElement("ProjectReference", new XAttribute("Include", "externals/CheatEngine.SDK/Engine.csproj"),
						new XElement("AdditionalProperties", "Existing=preserved")),
					new XElement("ProjectReference", new XAttribute("Include", "Unrelated.csproj"))),
				new XElement("Import", new XAttribute("Project", Path.Combine(RepositoryPaths.Root, "Directory.Build.targets"))),
				new XElement("Target", new XAttribute("Name", "IncludeTransitiveProjectReferences"),
					new XElement("ItemGroup",
						new XElement("ProjectReference", new XAttribute("Include", "externals/CheatEngine.Client/Core.csproj"),
							new XElement("AdditionalProperties", "Existing=preserved")))),
				new XElement("Target", new XAttribute("Name", "AssignProjectConfiguration"))));
			fixture.Save(project);
			ProcessStartInfo start = new("dotnet")
			{
				WorkingDirectory = RepositoryPaths.Root,
				UseShellExecute = false,
				CreateNoWindow = true,
				RedirectStandardOutput = true,
				RedirectStandardError = true
			};
			string[] arguments = ["msbuild", project, "-t:AssignProjectConfiguration", "-getItem:ProjectReference",
				"-nologo", "-verbosity:quiet", "-nodeReuse:false"];
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
				string output = await stdout;
				Assert.True(process.ExitCode == 0, output + await stderr);
				JsonArray references = JsonNode.Parse(output)!["Items"]!["ProjectReference"]!.AsArray();
				Assert.Equal(3, references.Count);
				foreach (int index in new[] { 0, 2 })
				{
					JsonNode reference = references[index]!;
					Assert.Equal("Platform=AnyCPU", reference["SetPlatform"]!.GetValue<string>());
					Assert.Equal($"Configuration={configuration}", reference["SetConfiguration"]!.GetValue<string>());
					Assert.Equal("Existing=preserved;ShouldUnsetParentConfigurationAndPlatform=false",
						reference["AdditionalProperties"]!.GetValue<string>());
				}

				Assert.Null(references[1]!["SetPlatform"]);
				Assert.Null(references[1]!["SetConfiguration"]);
				Assert.Null(references[1]!["AdditionalProperties"]);
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
