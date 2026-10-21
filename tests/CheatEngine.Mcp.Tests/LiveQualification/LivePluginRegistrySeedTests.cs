namespace CheatEngine.Mcp.Tests.LiveQualification;

public sealed class LivePluginRegistrySeedTests
{
	[Theory]
	[InlineData(true, 1)]
	[InlineData(false, 0)]
	public void Create_ProducesOneExactOwnedPluginPair(bool currentEnabled, int expectedEnabledValue)
	{
		const string path = @"C:\private-run\plugins\A\CheatEngine.Mcp.dll";

		LivePluginRegistrySeed seed = LivePluginRegistrySeed.Create(path, currentEnabled);

		Assert.Equal("00000000 A", seed.PathValueName);
		Assert.Equal("00000000 B", seed.EnabledValueName);
		Assert.Equal(Path.GetFullPath(path), seed.PluginPath);
		Assert.Equal(expectedEnabledValue, seed.EnabledValue);
	}

	[Theory]
	[InlineData("relative\\CheatEngine.Mcp.dll")]
	[InlineData("C:\\private-run\\plugins\\A\\CheatEngine.Mcp.txt")]
	[InlineData("")]
	public void Create_RejectsPathsThatCannotNameAnOwnedPluginDll(string path)
	{
		Assert.Throws<InvalidDataException>(() => LivePluginRegistrySeed.Create(path, currentEnabled: true));
	}
}
