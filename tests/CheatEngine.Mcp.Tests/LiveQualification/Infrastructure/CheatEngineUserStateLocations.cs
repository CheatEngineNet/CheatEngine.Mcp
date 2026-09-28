using System.Runtime.Versioning;

namespace CheatEngine.Mcp.Tests.LiveQualification.Infrastructure;

/// <summary>
///     Where the Cheat Engine user state lives. Only two shapes are accepted, so no bug can point the guard's
///     <c>DeleteSubKeyTree</c> at another key: the real <c>HKCU\Software\Cheat Engine</c> with
///     <c>%APPDATA%\Cheat Engine</c>,
///     or a test-owned scratch key <c>HKCU\Software\CheatEngine.Client.Tests\&lt;guid&gt;</c> with a folder below the
///     temporary directory.
/// </summary>
/// <param name="RegistrySubKey">The key below <c>HKEY_CURRENT_USER</c>.</param>
/// <param name="AppDataDirectory">The Cheat Engine folder of the roaming application data.</param>
[SupportedOSPlatform("windows")]
internal sealed record CheatEngineUserStateLocations(string RegistrySubKey, string AppDataDirectory)
{
	/// <summary>The Cheat Engine user key.</summary>
	internal const string CheatEngineRegistrySubKey = @"Software\Cheat Engine";

	/// <summary>The parent of the test-owned scratch keys.</summary>
	internal const string ScratchRegistryParent = @"Software\CheatEngine.Client.Tests";

	/// <summary>The workstation's real Cheat Engine user state.</summary>
	internal static CheatEngineUserStateLocations Workstation => new(CheatEngineRegistrySubKey,
		Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Cheat Engine"));

	/// <summary>Throws unless <paramref name="subKey" /> is the Cheat Engine key or a scratch key.</summary>
	internal static void RequireGuardedSubKey(string subKey)
	{
		ArgumentNullException.ThrowIfNull(subKey);
		string[] segments = subKey.Split('\\');
		bool scratch = segments.Length == 3 &&
					   string.Equals(string.Join('\\', segments[..2]), ScratchRegistryParent,
						   StringComparison.OrdinalIgnoreCase) &&
					   Guid.TryParseExact(segments[2], "N", out _);
		if (!scratch && !string.Equals(subKey, CheatEngineRegistrySubKey, StringComparison.OrdinalIgnoreCase))
		{
			throw new ArgumentException(
				$"'HKCU\\{subKey}' is neither {CheatEngineRegistrySubKey} nor a scratch key {ScratchRegistryParent}\\<guid>.",
				nameof(subKey));
		}
	}

	/// <summary>Throws unless both locations have one of the accepted shapes, consistently.</summary>
	internal void Validate()
	{
		RequireGuardedSubKey(RegistrySubKey);
		string appData = Path.TrimEndingDirectorySeparator(Path.GetFullPath(AppDataDirectory));
		bool real = string.Equals(RegistrySubKey, CheatEngineRegistrySubKey, StringComparison.OrdinalIgnoreCase);
		bool accepted = real
			? string.Equals(appData, Path.TrimEndingDirectorySeparator(Workstation.AppDataDirectory),
				StringComparison.OrdinalIgnoreCase)
			: LiveQualificationOptIn.IsSameOrBelow(appData, Path.GetTempPath()) &&
			  !string.Equals(appData, Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath())),
				  StringComparison.OrdinalIgnoreCase);
		if (!accepted)
		{
			throw new ArgumentException(real
				? $"The Cheat Engine key goes with %APPDATA%\\Cheat Engine, not '{appData}'."
				: $"A scratch key goes with a folder below the temporary directory, not '{appData}'.");
		}
	}
}
