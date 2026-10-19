using System.Runtime.Versioning;

namespace CheatEngine.Mcp.Tests.LiveQualification;

[SupportedOSPlatform("windows")]
public sealed class LiveModuleExportQualificationPathTests
{
	private const string SystemDirectory = @"C:\Windows\System32";
	private const string Wow64Directory = @"C:\Windows\SysWOW64";

	[Fact]
	public void ResolveTargetModulePath_X86System32ModuleOn64BitWindows_UsesSysWow64File()
	{
		string path = LiveModuleExportQualification.ResolveTargetModulePath(@"C:\WINDOWS\System32\KERNEL32.DLL",
			false, true, SystemDirectory, Wow64Directory);

		Assert.Equal(@"C:\Windows\SysWOW64\KERNEL32.DLL", path, ignoreCase: true);
	}

	[Theory]
	[InlineData(true, true)]
	[InlineData(false, false)]
	public void ResolveTargetModulePath_WidthDoesNotRequireWow64_RemainsSystem32(bool targetIs64Bit,
		bool is64BitWindows)
	{
		const string original = @"C:\Windows\System32\KERNEL32.DLL";

		string path = LiveModuleExportQualification.ResolveTargetModulePath(original, targetIs64Bit, is64BitWindows,
			SystemDirectory, Wow64Directory);

		Assert.Equal(original, path);
	}

	[Theory]
	[InlineData(@"C:\Windows\System32-backup\KERNEL32.DLL")]
	[InlineData(@"C:\Windows\System32\drivers\KERNEL32.DLL")]
	[InlineData(@"D:\Games\KERNEL32.DLL")]
	public void ResolveTargetModulePath_X86UnrelatedOrPrefixCollisionPath_RemainsUnchanged(string original)
	{
		string path = LiveModuleExportQualification.ResolveTargetModulePath(original, false, true,
			SystemDirectory, Wow64Directory);

		Assert.Equal(original, path);
	}
}
