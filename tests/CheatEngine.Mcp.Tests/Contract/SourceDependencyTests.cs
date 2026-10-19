using System.Diagnostics;
using System.Reflection;
using System.Text.Json;

using CheatEngine.Mcp.Tests.Support;
using CheatEngine.SDK.Engine.Runtime;

namespace CheatEngine.Mcp.Tests.Contract;

/// <summary>Verifies the built dependency graph and the source identity used by runtime capability checks.</summary>
public sealed class SourceDependencyTests
{
	[Theory]
	[InlineData("CheatEngine.Mcp.Plugin", "CheatEngine.Mcp")]
	[InlineData("CheatEngine.Mcp.Gateway", "CheatEngine.Mcp.Gateway")]
	public void BuiltCheatEngineDependenciesComeFromSourceProjects(string project, string assembly)
	{
		string configuration = Path.GetFileName(Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory));
		string path = Path.Combine(RepositoryPaths.Root, "artifacts", "bin", project, configuration, assembly + ".deps.json");
		using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
		JsonProperty[] dependencies = document.RootElement.GetProperty("libraries").EnumerateObject()
			.Where(static item => item.Name.StartsWith("CheatEngine.Client", StringComparison.Ordinal)
				|| item.Name.StartsWith("CheatEngine.SDK", StringComparison.Ordinal))
			.ToArray();

		Assert.Contains(dependencies, static item => item.Name.StartsWith("CheatEngine.Client.Core/", StringComparison.Ordinal));
		Assert.Contains(dependencies, static item => item.Name.StartsWith("CheatEngine.SDK.Engine/", StringComparison.Ordinal));
		Assert.Contains(dependencies, static item => item.Name.StartsWith("CheatEngine.SDK.Hosting/", StringComparison.Ordinal));
		Assert.All(dependencies, static item => Assert.Equal("project", item.Value.GetProperty("type").GetString()));
	}

	[Fact]
	public void ClientEmbedsTheConsumedSdkSourceIdentityWithoutAPackageHash()
	{
		Assembly client = Assembly.Load("CheatEngine.Client.Core");
		Dictionary<string, string?> metadata = client.GetCustomAttributes<AssemblyMetadataAttribute>()
			.ToDictionary(static item => item.Key, static item => item.Value, StringComparer.Ordinal);
		const string prefix = "CheatEngine.Client.ConsumedSdk.";
		Assert.Equal("source", metadata[prefix + "Kind"]);
		Assert.False(metadata.ContainsKey(prefix + "ContentHashSha512"));
		string? sdkIdentity = typeof(RuntimeInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
		Assert.NotNull(sdkIdentity);
		Assert.Equal(metadata[prefix + "Version"] + "+" + metadata[prefix + "SourceCommit"], sdkIdentity);
		Assert.Matches("^[0-9a-f]{40}$", metadata[prefix + "SourceCommit"]!);
		Assert.Equal(sdkIdentity.Split('.')[0], metadata[prefix + "SupportedMajor"]);
	}

	[Theory]
	[InlineData("CheatEngine.Client.Core")]
	[InlineData("CheatEngine.SDK.Engine")]
	public void SourceProjectsKeepTheSelectedBuildConfiguration(string assemblyName)
	{
		bool release = string.Equals(Path.GetFileName(Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory)),
			"release", StringComparison.OrdinalIgnoreCase);
		Assembly assembly = Assembly.Load(assemblyName);
		DebuggableAttribute? debugging = assembly.GetCustomAttribute<DebuggableAttribute>();
		Assert.NotNull(debugging);
		Assert.Equal(!release, debugging.IsJITOptimizerDisabled);
	}
}
