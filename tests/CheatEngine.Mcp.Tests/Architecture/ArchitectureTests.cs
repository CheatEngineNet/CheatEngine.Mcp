using System.Reflection;
using System.Runtime.CompilerServices;

using CheatEngine.Mcp.Gateway;
using CheatEngine.Mcp.Plugin;
using CheatEngine.Mcp.Prompts;
using CheatEngine.Mcp.Resources;
using CheatEngine.Mcp.Tests.Support;
using CheatEngine.Mcp.Tools;

namespace CheatEngine.Mcp.Tests.Architecture;

/// <summary>The project graph and composition rules that keep each layer replaceable.</summary>
public sealed class ArchitectureTests
{
	private static readonly Assembly Core = typeof(McpRuntimeInfo).Assembly;
	private static readonly Assembly Tools = typeof(CheatEngineToolsBuilderExtensions).Assembly;
	private static readonly Assembly Resources = typeof(CheatEngineResourcesBuilderExtensions).Assembly;
	private static readonly Assembly Prompts = typeof(CheatEnginePromptsBuilderExtensions).Assembly;
	private static readonly Assembly Hosting = typeof(McpBackendHost).Assembly;
	private static readonly Assembly Gateway = typeof(GatewayProgram).Assembly;
	private static readonly Assembly Plugin = typeof(CheatEngineMcpPlugin).Assembly;

	private static readonly Assembly[] Product = [Core, Tools, Resources, Prompts, Hosting, Gateway, Plugin];

	private static readonly string[] ProjectRoots = ["srcs", "libs", "tests"];

	/// <summary>
	///     Each product assembly's allowed layers; a package entry also admits its assemblies, such as
	///     CheatEngine.SDK.Lua.
	/// </summary>
	public static TheoryData<string, string[]> AllowedReferences => new()
	{
		{ "CheatEngine.Mcp.Core", ["CheatEngine.Client", "CheatEngine.SDK"] },
		{ "CheatEngine.Mcp.Tools", ["CheatEngine.Mcp.Core", "CheatEngine.Client", "CheatEngine.SDK"] },
		{ "CheatEngine.Mcp.Resources", ["CheatEngine.Mcp.Core", "CheatEngine.Mcp.Tools"] },
		// Prompts render the workflow bodies of the Resources knowledge base and link the documents it serves.
		{ "CheatEngine.Mcp.Prompts", ["CheatEngine.Mcp.Core", "CheatEngine.Mcp.Resources"] },
		// The transport never sees the Client: it borrows primitive instances through the Core manifest.
		{ "CheatEngine.Mcp.Hosting", ["CheatEngine.Mcp.Core"] },
		{
			"CheatEngine.Mcp.Gateway", [
				"CheatEngine.Mcp.Core", "CheatEngine.Mcp.Hosting", "CheatEngine.Mcp.Tools", "CheatEngine.Mcp.Resources",
				"CheatEngine.Mcp.Prompts"
			]
		},
		// The composition root; its log is the plugin's own rolling file writer, never a logging library (NLog).
		{
			"CheatEngine.Mcp.Plugin", [
				"CheatEngine.Mcp.Core", "CheatEngine.Mcp.Hosting", "CheatEngine.Mcp.Tools", "CheatEngine.Mcp.Resources",
				"CheatEngine.Mcp.Prompts", "CheatEngine.Client", "CheatEngine.SDK"
			]
		}
	};

	[Theory]
	[MemberData(nameof(AllowedReferences))]
	public void ProjectGraph_Assembly_ReferencesOnlyItsAllowedLayers(string assembly, string[] allowed)
	{
		Assembly subject = Product.Single(candidate => candidate.GetName().Name == assembly);
		string[] disallowed = subject.GetReferencedAssemblies().Select(static reference => reference.Name!)
			.Where(static name => name.StartsWith("CheatEngine.", StringComparison.Ordinal)
								  || name.StartsWith("NLog", StringComparison.Ordinal))
			.Where(name => !allowed.Any(layer => name == layer
												 || (!layer.StartsWith("CheatEngine.Mcp.", StringComparison.Ordinal)
													 && name.StartsWith(layer + ".", StringComparison.Ordinal))))
			.ToArray();
		Assert.Empty(disallowed);
	}

	[Fact]
	public void ProjectGraph_DomainLibraries_NeverReferenceAspNetCore()
	{
		foreach (Assembly subject in new[] { Core, Tools, Resources, Prompts })
		{
			Assert.DoesNotContain(subject.GetReferencedAssemblies(), reference =>
				reference.Name!.StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal));
		}
	}

	[Fact]
	public void InternalsVisibleTo_ProductAssemblies_OnlyExposeCoreLuaBridgeToThePlugin()
	{
		foreach (Assembly subject in Product)
		{
			string[] expected = subject == Core
				? ["CheatEngine.Mcp.Plugin", "CheatEngine.Mcp.Tests"]
				: subject == Gateway
					? []
					: ["CheatEngine.Mcp.Tests"];
			Assert.Equal(expected, subject.GetCustomAttributes<InternalsVisibleToAttribute>()
				.Select(static attribute => attribute.AssemblyName)
				.Order());
		}
	}

	[Fact]
	public void AotAnalysis_ProductAssemblies_AreBuiltAotCompatibleAndTrimmable()
	{
		// CEMCP005 keeps IsAotCompatible on every product project; the SDK records it, and IsTrimmable, as metadata.
		foreach (Assembly subject in Product)
		{
			AssemblyMetadataAttribute[] metadata = subject.GetCustomAttributes<AssemblyMetadataAttribute>().ToArray();
			Assert.Contains(metadata, static attribute => attribute is { Key: "IsAotCompatible", Value: "True" });
			Assert.Contains(metadata, static attribute => attribute is { Key: "IsTrimmable", Value: "True" });
		}
	}

	[Fact]
	public void State_ProductAssemblies_HoldNoMutableStaticFields()
	{
		// All state belongs to a DI lifetime; stateless helpers and compiler caches are fine.
		string[] mutable = Product.SelectMany(static assembly => assembly.GetTypes())
			.Where(static type => !type.IsDefined(typeof(CompilerGeneratedAttribute), false))
			.SelectMany(static type => type.GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic
													  | BindingFlags.DeclaredOnly))
			.Where(static field => field is { IsLiteral: false, IsInitOnly: false })
			.Select(static field => $"{field.DeclaringType!.FullName}.{field.Name}")
			.ToArray();
		Assert.Empty(mutable);
	}

	[Fact]
	public void PluginStatus_SdkUiException_IsCalledOnlyByThePluginIndicator()
	{
		string[] callers = SourceFiles("srcs", "libs")
			.Where(static file => File.ReadAllText(file).Contains("ShowPluginStatus(", StringComparison.Ordinal))
			.Select(Relative)
			.Order(StringComparer.Ordinal)
			.ToArray();
		Assert.Equal(
			[
				"srcs/CheatEngine.Mcp.Plugin/Lua/PluginLuaToolRuntime.cs",
				"srcs/CheatEngine.Mcp.Plugin/McpStatusIndicator.cs"
			],
			callers);
	}

	[Theory]
	[InlineData("WithTools<")]
	[InlineData("WithToolsFromAssembly")]
	[InlineData("WithResourcesFromAssembly")]
	[InlineData("WithPromptsFromAssembly")]
	[InlineData("ActivatorUtilities")]
	public void Composition_Sources_RegisterPrimitivesOnlyThroughTheBuilders(string forbidden)
	{
		Assert.Empty(SourceFiles("srcs", "libs")
			.Where(file => File.ReadAllText(file).Contains(forbidden, StringComparison.Ordinal))
			.Select(Relative));
	}

	[Theory]
	[InlineData("BundleEntryPoint")]
	[InlineData("BundleCache")]
	[InlineData("McpPayload")]
	[InlineData("MCP_BUNDLE_CACHE_DIRECTORY")]
	[InlineData("CheatEngine.Mcp.Bootstrap")]
	[InlineData("CheatEngine.Mcp.Runtime")]
	[InlineData("WebSingletons")]
	[InlineData("ConfigureWebServices")]
	public void Repository_RemovedWrapperAndTransition_LeaveNoTrace(string trace)
	{
		string self = Path.Combine(RepositoryPaths.Root, "tests", "CheatEngine.Mcp.Tests", "Architecture",
			"ArchitectureTests.cs");
		IEnumerable<string> files = SourceFiles("srcs", "libs", "tests", "eng", ".github")
			.Concat(Directory.EnumerateFiles(RepositoryPaths.Root, "*.*", SearchOption.TopDirectoryOnly))
			.Where(file => !string.Equals(file, self, StringComparison.OrdinalIgnoreCase)
						   && Path.GetExtension(file) is ".cs" or ".csproj" or ".props" or ".targets" or ".ps1"
							   or ".md" or ".json" or ".yml" or ".slnx" or ".pubxml");
		Assert.Empty(files.Where(file => File.ReadAllText(file).Contains(trace, StringComparison.Ordinal))
			.Select(Relative));
	}

	[Fact]
	public void Repository_ProjectFolders_EachHoldTheirSolutionProject()
	{
		// A restore can leave a lock file behind in the folder of a removed project; no folder may outlive its project.
		string solution = File.ReadAllText(Path.Combine(RepositoryPaths.Root, "CheatEngine.Mcp.slnx"));
		string[] orphans = ProjectRoots
			.SelectMany(static root => Directory.EnumerateDirectories(Path.Combine(RepositoryPaths.Root, root)))
			.Select(Relative)
			.Where(folder =>
				!solution.Contains($"\"{folder}/{Path.GetFileName(folder)}.csproj\"", StringComparison.Ordinal)
				|| !File.Exists(Path.Combine(RepositoryPaths.Root, folder, Path.GetFileName(folder) + ".csproj")))
			.ToArray();
		Assert.Empty(orphans);
	}

	private static IEnumerable<string> SourceFiles(params string[] roots)
	{
		return roots.Select(static root => Path.Combine(RepositoryPaths.Root, root))
			.Where(Directory.Exists)
			.SelectMany(static root => Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories))
			.Where(static file => !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}",
									  StringComparison.OrdinalIgnoreCase)
								  && !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}",
									  StringComparison.OrdinalIgnoreCase)
								  && !file.EndsWith("packages.lock.json", StringComparison.OrdinalIgnoreCase))
			.Where(static file => Path.GetExtension(file) is not ".dll" and not ".exe" and not ".pdb");
	}

	private static string Relative(string file)
	{
		return Path.GetRelativePath(RepositoryPaths.Root, file).Replace(Path.DirectorySeparatorChar, '/');
	}
}
