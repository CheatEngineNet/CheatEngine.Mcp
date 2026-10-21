using System.Runtime.Versioning;
using System.Text.Json.Nodes;

namespace CheatEngine.Mcp.Tests.LiveQualification;

[SupportedOSPlatform("windows")]
public sealed class LiveMonoTargetTests
{
	private const string Nonce = "0123456789abcdef0123456789abcdef";
	private const int ProcessId = 4242;
	private const string AssemblyHash = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
	private const string SourceHash = "CCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCC";

	[Fact]
	public void RequireShortManifestPath_AcceptsTemporaryPathBelowLegacyLimit()
	{
		LiveMonoTarget.RequireShortManifestPath(@"C:\" + new string('x', 219));
	}

	[Theory]
	[InlineData(220)]
	[InlineData(221)]
	public void RequireShortManifestPath_RejectsTemporaryPathAtOrAboveLegacyLimit(int leafLength)
	{
		InvalidDataException error = Assert.Throws<InvalidDataException>(() =>
			LiveMonoTarget.RequireShortManifestPath(@"C:\" + new string('x', leafLength)));
		Assert.Contains("shorter live run root", error.Message, StringComparison.Ordinal);
	}

	[Theory]
	[InlineData("first build", 'A')]
	[InlineData("rebuilt legacy executable", 'B')]
	public void RequireFixtureIdentity_AcceptsEachBinaryMatchingItsBuildSidecar(string caseName, char hashDigit)
	{
		Assert.NotEmpty(caseName);
		Fixture fixture = CreateFixture();
		string source = Path.Combine(fixture.Root, "Program.cs");
		string binaryHash = new(hashDigit, 64);
		JsonObject identity = BuildIdentity(fixture.Assembly, source, binaryHash);

		LiveMonoTarget.RequireFixtureIdentity(identity, fixture.Assembly, source, binaryHash, SourceHash, SourceHash);
	}

	[Theory]
	[InlineData("targetSha256", "changed binary")]
	[InlineData("sourceSha256", "stale build source")]
	[InlineData("targetPath", "foreign executable")]
	[InlineData("sourcePath", "foreign source")]
	[InlineData("architecture", "x86")]
	public void RequireFixtureIdentity_RejectsChangedBuildIdentity(string property, string caseName)
	{
		Assert.NotEmpty(caseName);
		Fixture fixture = CreateFixture();
		string source = Path.Combine(fixture.Root, "Program.cs");
		JsonObject identity = BuildIdentity(fixture.Assembly, source, AssemblyHash);
		identity[property] = property.EndsWith("Path", StringComparison.Ordinal)
			? Path.Combine(fixture.Root, "foreign", "input") : new string('D', 64);

		Assert.Throws<InvalidDataException>(() => LiveMonoTarget.RequireFixtureIdentity(identity, fixture.Assembly,
			source, AssemblyHash, SourceHash, SourceHash));
	}

	[Fact]
	public void RequireFixtureIdentity_RejectsChangedReviewedSourceEvenWithMatchingSidecar()
	{
		Fixture fixture = CreateFixture();
		string source = Path.Combine(fixture.Root, "Program.cs");
		JsonObject identity = BuildIdentity(fixture.Assembly, source, AssemblyHash);
		string changedHash = new('D', 64);
		identity["sourceSha256"] = changedHash;

		Assert.Throws<InvalidDataException>(() => LiveMonoTarget.RequireFixtureIdentity(identity, fixture.Assembly,
			source, AssemblyHash, changedHash, SourceHash));
	}

	private static JsonObject BuildIdentity(string assembly, string source, string binaryHash) => new()
	{
		["architecture"] = "x64",
		["targetPath"] = assembly,
		["targetSha256"] = binaryHash,
		["sourcePath"] = source,
		["sourceSha256"] = SourceHash
	};

	[Fact]
	public void ManifestParse_AcceptsFixedOwnedMonoProbe()
	{
		Fixture fixture = CreateFixture();

		LiveMonoTarget.Manifest manifest = LiveMonoTarget.Manifest.Parse(JsonNode.Parse(fixture.Node.ToJsonString())!, Nonce, ProcessId, fixture.Root,
			fixture.Assembly, AssemblyHash);

		Assert.Equal(Nonce, manifest.Nonce);
		Assert.Equal(ProcessId, manifest.ProcessId);
		Assert.Equal(Path.GetFullPath(fixture.Assembly), manifest.AssemblyPath);
		Assert.Equal(AssemblyHash, manifest.AssemblyHash);
		Assert.Equal("CheatEngine.Mcp.MonoFixture", manifest.Namespace);
		Assert.Equal("MonoPrimitiveProbe", manifest.ClassName);
		Assert.Equal("7FF612340000", manifest.ObjectPointer);
		Assert.Equal(Path.Combine(fixture.Root, "mono-owned-" + Nonce + ".stop"), manifest.StopMarker);
		Assert.Equal(314159, manifest.Magic);
		Assert.Equal(7000L, manifest.Counter);
		Assert.Equal(1.25f, manifest.Speed);
		Assert.Equal(424242, manifest.StaticMagic);
		Assert.Equal(7, manifest.ExpectedBumpArgument);
		Assert.Equal(7007L, manifest.ExpectedCounterAfterInvoke);
	}

	public static IEnumerable<object?[]> InvalidManifestMutations()
	{
		foreach (string mutation in new[]
		{
			"nonce", "pid", "architecture", "runtime marker", "owned root", "sibling stop-marker escape", "assembly path",
			"assembly hash", "namespace", "class", "magic", "counter", "speed", "static magic", "missing fields",
			"missing handle token", "zero handle token", "zero object pointer", "malformed object pointer", "lifetime",
			"warmup", "bump argument", "bump result"
		})
		{
			yield return [mutation];
		}
	}

	[Theory]
	[MemberData(nameof(InvalidManifestMutations))]
	public void ManifestParse_RejectsEachOwnedIdentityCorruption(string mutation)
	{
		Fixture fixture = CreateFixture();
		ApplyMutation(fixture.Node, mutation);

		Assert.Throws<InvalidDataException>(() => LiveMonoTarget.Manifest.Parse(JsonNode.Parse(fixture.Node.ToJsonString())!, Nonce, ProcessId,
			fixture.Root, fixture.Assembly, AssemblyHash));
	}

	private static void ApplyMutation(JsonObject node, string mutation)
	{
		switch (mutation)
		{
			case "nonce":
				node["nonce"] = "fedcba9876543210fedcba9876543210";
				break;
			case "pid":
				node["pid"] = ProcessId + 1;
				break;
			case "architecture":
				node["architecture"] = "x86";
				break;
			case "runtime marker":
				node["monoRuntimeMarker"] = "Other.Runtime";
				break;
			case "owned root":
				node["ownedRoot"] = Path.Combine(Path.GetTempPath(), "cemcp-mono-foreign-root");
				break;
			case "sibling stop-marker escape":
				node["stopMarker"] = Path.Combine(Path.GetTempPath(), "cemcp-mono-owned-sibling", "mono-owned-" + Nonce + ".stop");
				break;
			case "assembly path":
				node["assemblyFullPath"] = Path.Combine(Path.GetTempPath(), "cemcp-mono-other.exe");
				break;
			case "assembly hash":
				node["assemblySha256"] = new string('B', 64);
				break;
			case "namespace":
				node["namespace"] = "Other.Namespace";
				break;
			case "class":
				node["class"] = "OtherClass";
				break;
			case "magic":
				Fields(node)["Magic"] = 1;
				break;
			case "counter":
				Fields(node)["Counter"] = 7001L;
				break;
			case "speed":
				Fields(node)["Speed"] = 1.5d;
				break;
			case "static magic":
				node["staticMagic"] = 1;
				break;
			case "missing fields":
				node.Remove("fields");
				break;
			case "missing handle token":
				node.Remove("gcHandleToken");
				break;
			case "zero handle token":
				node["gcHandleToken"] = 0;
				break;
			case "zero object pointer":
				node["objectPointer"] = "0";
				break;
			case "malformed object pointer":
				node["objectPointer"] = "not-hex";
				break;
			case "lifetime":
				node["maximumLifetimeMilliseconds"] = 119999;
				break;
			case "warmup":
				node["staticAdd42WarmupResult"] = 41;
				break;
			case "bump argument":
				node["expectedInstanceBumpArgument"] = 6;
				break;
			case "bump result":
				node["expectedCounterAfterInvoke"] = 7006L;
				break;
			default:
				throw new ArgumentOutOfRangeException(nameof(mutation), mutation, "Unknown synthetic manifest mutation.");
		}
	}

	private static JsonObject Fields(JsonObject node) => node["fields"]?.AsObject()
		?? throw new InvalidOperationException("The synthetic manifest is missing fields.");

	private static Fixture CreateFixture()
	{
		string root = Path.Combine(Path.GetTempPath(), "cemcp-mono-owned-root");
		string assembly = Path.Combine(root, "CheatEngine.Mcp.LiveMonoTarget.exe");
		return new(root, assembly, new JsonObject
		{
			["nonce"] = Nonce,
			["pid"] = ProcessId,
			["architecture"] = "x64",
			["monoRuntimeMarker"] = "Mono.Runtime",
			["ownedRoot"] = root,
			["stopMarker"] = Path.Combine(root, "mono-owned-" + Nonce + ".stop"),
			["maximumLifetimeMilliseconds"] = 120000,
			["assemblyFullPath"] = assembly,
			["assemblySha256"] = AssemblyHash,
			["namespace"] = "CheatEngine.Mcp.MonoFixture",
			["class"] = "MonoPrimitiveProbe",
			["objectPointer"] = "7FF612340000",
			["gcHandleToken"] = 1234u,
			["fields"] = new JsonObject
			{
				["Magic"] = 314159,
				["Counter"] = 7000L,
				["Speed"] = 1.25d
			},
			["staticMagic"] = 424242,
			["staticAdd42WarmupResult"] = 42,
			["expectedInstanceBumpArgument"] = 7,
			["expectedCounterAfterInvoke"] = 7007L
		});
	}

	private sealed record Fixture(string Root, string Assembly, JsonObject Node);
}
