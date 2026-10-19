using System.Runtime.Versioning;

namespace CheatEngine.Mcp.Tests.LiveQualification;

[SupportedOSPlatform("windows")]
public sealed class LiveProcessCreateQualificationTests
{
	[Fact]
	public void OwnedExecutablePath_NormalDirectory_UsesShortCanonicalLeafUnderRoot()
	{
		string directory = Path.Combine(Path.GetTempPath(), "CheatEngine.Mcp", "process-create", Guid.NewGuid().ToString("N"));

		string path = LiveProcessCreateQualification.OwnedExecutablePath(directory);

		Assert.Equal(Path.GetFullPath(Path.Combine(directory, "target.exe")), path);
		Assert.True(LiveQualificationOptIn.IsSameOrBelow(path, Path.GetFullPath(directory)));
		Assert.True(path.Length < 240);
	}

	[Fact]
	public void OwnedExecutablePath_JustBelowConservativeLegacyClrLimit_Accepts()
	{
		string directory = DirectoryForExecutableLength(239);
		Assert.Equal(239, LiveProcessCreateQualification.OwnedExecutablePath(directory).Length);
	}

	[Theory]
	[InlineData(240)]
	[InlineData(241)]
	public void OwnedExecutablePath_AtOrAboveConservativeLegacyClrLimit_RejectsBeforeCopyOrLaunch(int length)
	{
		string directory = DirectoryForExecutableLength(length);

		InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
			LiveProcessCreateQualification.OwnedExecutablePath(directory));

		Assert.Contains("shorter than 240", exception.Message, StringComparison.Ordinal);
	}

	private static string DirectoryForExecutableLength(int length)
	{
		string temporary = Path.GetFullPath(Path.GetTempPath());
		int paddingLength = length + 1 - Path.Combine(temporary, "x", "target.exe").Length;
		Assert.True(paddingLength > 0);
		string directory = Path.Combine(temporary, new string('x', paddingLength));
		Assert.Equal(length, Path.GetFullPath(Path.Combine(directory, "target.exe")).Length);
		return directory;
	}
}
