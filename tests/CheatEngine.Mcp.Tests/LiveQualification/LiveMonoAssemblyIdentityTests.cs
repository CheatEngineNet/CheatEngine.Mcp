using System.Runtime.Versioning;
using System.Text.Json.Nodes;

namespace CheatEngine.Mcp.Tests.LiveQualification;

[SupportedOSPlatform("windows")]
public sealed class LiveMonoAssemblyIdentityTests
{
	[Fact]
	public void RequireOwnedAssembly_UsesMetadataEvenWhenFileIsRenamed()
	{
		string root = Path.Combine(Path.GetTempPath(), "mcp-mono-assembly-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(root);
		try
		{
			string renamed = Path.Combine(root, "renamed-owned-target.exe");
			File.Copy(typeof(LiveMonoAssemblyIdentityTests).Assembly.Location, renamed);
			JsonObject expected = new()
			{
				["name"] = "CheatEngine.Mcp.Tests",
				["imageHandle"] = "42"
			};
			JsonObject response = new()
			{
				["assemblies"] = new JsonArray(new JsonObject { ["name"] = Path.GetFileName(renamed) }, expected)
			};

			Assert.Same(expected, LiveMonoQualification.RequireOwnedAssembly(response, renamed));
		}
		finally { Directory.Delete(root, true); }
	}

	[Theory]
	[InlineData("CheatEngine.Mcp.Tests.dll")]
	[InlineData("CheatEngine.Mcp.Tests.foreign")]
	public void RequireOwnedAssembly_RejectsFilenameAndPartialNameMatches(string wrongName)
	{
		JsonObject response = new()
		{
			["assemblies"] = new JsonArray(new JsonObject { ["name"] = wrongName })
		};

		Assert.Throws<InvalidOperationException>(() => LiveMonoQualification.RequireOwnedAssembly(response,
			typeof(LiveMonoAssemblyIdentityTests).Assembly.Location));
	}

	[Fact]
	public void RequireOwnedAssembly_RejectsDuplicateMetadataNames()
	{
		JsonObject response = new()
		{
			["assemblies"] = new JsonArray(new JsonObject { ["name"] = "CheatEngine.Mcp.Tests" },
				new JsonObject { ["name"] = "CheatEngine.Mcp.Tests" })
		};

		Assert.Throws<InvalidOperationException>(() => LiveMonoQualification.RequireOwnedAssembly(response,
			typeof(LiveMonoAssemblyIdentityTests).Assembly.Location));
	}
}
