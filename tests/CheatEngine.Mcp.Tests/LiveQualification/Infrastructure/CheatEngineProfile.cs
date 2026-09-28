using System.Runtime.Versioning;

namespace CheatEngine.Mcp.Tests.LiveQualification.Infrastructure;

/// <summary>The exact Cheat Engine installation a live run qualifies against.</summary>
/// <param name="Profile">The profile id a qualification result names.</param>
/// <param name="HostExecutable">The file name of the host executable.</param>
/// <param name="HostSha256">The upper-case SHA-256 of the host executable.</param>
/// <param name="HostFileVersion">The file version resource of the host executable.</param>
/// <param name="HostMachine">The COFF machine of the host executable.</param>
/// <param name="TargetSha256">The disposable targets, by file name, with their upper-case SHA-256.</param>
[SupportedOSPlatform("windows")]
internal sealed record CheatEngineProfile(
	string Profile,
	string HostExecutable,
	string HostSha256,
	string HostFileVersion,
	string HostMachine,
	IReadOnlyDictionary<string, string> TargetSha256)
{
	/// <summary>The x64 gtutorial target.</summary>
	internal const string Target64 = "gtutorial-x86_64.exe";

	/// <summary>The x86 gtutorial target.</summary>
	internal const string Target32 = "gtutorial-i386.exe";

	/// <summary>
	///     Cheat Engine 7.7.0.10621 x64 with its two gtutorial targets, the profile the harness gate also pins
	///     (<c>QualificationAuthorization.ExactCheatEngineSha256</c>).
	/// </summary>
	internal static CheatEngineProfile CheatEngine77
	{
		get;
	} = new("ce-7.7.0.10621-x64-managed-hostfxr", "cheatengine-x86_64.exe",
		"9727076DA50924E4A097B49A02155E4B34759269C3017FF31375364B8826EB4D", "7.7.0.10621", "Amd64",
		new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
		{
			[Target64] = "2DABEFFD5DD45A3DA79697D6B8A3B7942A9EFF0EA78519A313ECB29AD5A865E3",
			[Target32] = "9131B1CA916D6AC1FB67224A67CF0F578105D43AEEEBB03137E94D73CBE11BCA"
		});
}
