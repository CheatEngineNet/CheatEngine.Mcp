using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

using CheatEngine.Mcp.Tests.Support;

namespace CheatEngine.Mcp.Tests.Contract;

/// <summary>Pins the reproduced upstream notices to the serviced runtime used by the Native AOT gateway.</summary>
public sealed class RuntimeNoticeTests
{
	[Theory]
	[InlineData("Microsoft.NETCore.App", "B7D4569659507A00EFEEA90F15A0A95BB2F8B6CE33737D86090B8E22C74A8C98")]
	[InlineData("Microsoft.AspNetCore.App", "9C0268C3085D693279C6E75E2509E4C67D976170DBD4F886AACCBB163E345B9E")]
	public void RuntimeNotices_MatchServicedRuntimePacks(string framework, string expectedSha256)
	{
		string root = RepositoryPaths.Root;
		using JsonDocument aotLock = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "srcs",
			"CheatEngine.Mcp.Gateway", "packages.aot.lock.json")));
		string version = aotLock.RootElement.GetProperty("dependencies").GetProperty("net10.0")
			.GetProperty("Microsoft.DotNet.ILCompiler").GetProperty("resolved").GetString()!;
		string notices = File.ReadAllText(Path.Combine(root, "THIRD-PARTY-NOTICES.md"))
			.Replace("\r\n", "\n", StringComparison.Ordinal);
		Assert.Contains($"| {framework} | {version} |", notices, StringComparison.Ordinal);
		int heading = notices.IndexOf($"### {framework} {version} component notices\n", StringComparison.Ordinal);
		Assert.True(heading >= 0, "The runtime notice section must match the AOT compiler's serviced runtime.");
		string fence = new('`', 8);
		string opening = fence + "text\n";
		int start = notices.IndexOf(opening, heading, StringComparison.Ordinal);
		Assert.True(start >= 0, "The upstream notice text must be reproduced in its fenced section.");
		start += opening.Length;
		int end = notices.IndexOf("\n" + fence, start, StringComparison.Ordinal);
		Assert.True(end > start, "The upstream component notices must not be empty.");
		string digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(notices[start..end])));

		Assert.Equal(expectedSha256, digest);
	}
}
