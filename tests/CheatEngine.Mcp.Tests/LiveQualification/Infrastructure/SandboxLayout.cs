// Adapted from CheatEngine.Client; see NOTICE.md and licenses/CheatEngine.Client.LICENSE.
using System.Globalization;
using System.Runtime.Versioning;
using System.Security.Cryptography;

namespace CheatEngine.Mcp.Tests.LiveQualification;

/// <summary>
///     The directory of one live run, below the run root and outside the repository:
///     <c>&lt;runRoot&gt;/&lt;yyyyMMddTHHmmssZ&gt;-&lt;4 hex&gt;/</c> holds <c>ce/</c> (the sandboxed Cheat Engine copy, with
///     the generated autorun driver), <c>plugins/&lt;bundle&gt;/</c>, <c>sessions/&lt;id&gt;/</c> (transcript, debug output,
///     authorization manifest), <c>receipts.jsonl</c>, <c>summary.json</c> and the user state backups
///     (<c>hkcu-backup.json</c>, <c>appdata-backup.json</c> and <c>appdata-backup/</c>). The crash marker
///     <c>registry-restore-pending.json</c> sits in the run root, where the next run finds it. Nothing in a run directory is
///     ever committed as is: evidence is redacted first (the run path becomes <c>&lt;run&gt;</c>).
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class SandboxLayout
{
	/// <summary>The generated driver, loaded by the sandbox's autorun folder; the <c>zz_</c> prefix makes it run last.</summary>
	internal const string DriverScriptName = "zz_cheatengine_client_qualification.lua";

	/// <summary>The crash marker, in the run root, that names the backups of a run whose user state is not yet restored.</summary>
	internal const string RestoreMarkerName = "registry-restore-pending.json";

	private SandboxLayout(string runRoot, string runId)
	{
		RunRoot = runRoot;
		RunId = runId;
		RunDirectory = Path.Combine(runRoot, runId);
	}

	/// <summary>The run root that holds every run directory.</summary>
	internal string RunRoot
	{
		get;
	}

	/// <summary>The id of this run, <c>yyyyMMddTHHmmssZ-xxxx</c>.</summary>
	internal string RunId
	{
		get;
	}

	/// <summary>The directory of this run.</summary>
	internal string RunDirectory
	{
		get;
	}

	/// <summary>The sandboxed copy of Cheat Engine.</summary>
	internal string CheatEngineDirectory => Path.Combine(RunDirectory, "ce");

	/// <summary>The generated autorun driver inside the sandbox.</summary>
	internal string DriverScriptPath => Path.Combine(CheatEngineDirectory, "autorun", DriverScriptName);

	/// <summary>The plugin bundles.</summary>
	internal string PluginsDirectory => Path.Combine(RunDirectory, "plugins");

	/// <summary>The receipt ledger.</summary>
	internal string ReceiptsPath => Path.Combine(RunDirectory, "receipts.jsonl");

	/// <summary>The run summary.</summary>
	internal string SummaryPath => Path.Combine(RunDirectory, "summary.json");

	/// <summary>The recursive snapshot of the Cheat Engine user registry key.</summary>
	internal string RegistryBackupPath => Path.Combine(RunDirectory, "hkcu-backup.json");

	/// <summary>The listing (path, length, SHA-256) of the Cheat Engine <c>%APPDATA%</c> folder.</summary>
	internal string AppDataManifestPath => Path.Combine(RunDirectory, "appdata-backup.json");

	/// <summary>The copy of the Cheat Engine <c>%APPDATA%</c> folder.</summary>
	internal string AppDataBackupDirectory => Path.Combine(RunDirectory, "appdata-backup");

	/// <summary>The crash marker of the run root.</summary>
	internal string RestoreMarkerPath => Path.Combine(RunRoot, RestoreMarkerName);

	/// <summary>Creates a new, empty run directory below <paramref name="runRoot" />.</summary>
	internal static SandboxLayout Create(string runRoot, DateTimeOffset now)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(runRoot);
		string root = Path.GetFullPath(runRoot);
		for (int attempt = 0; attempt < 16; attempt++)
		{
			string id = string.Create(CultureInfo.InvariantCulture,
				$"{now.UtcDateTime:yyyyMMdd'T'HHmmss'Z'}-{RandomNumberGenerator.GetHexString(4, true)}");
			SandboxLayout layout = new(root, id);
			if (Directory.Exists(layout.RunDirectory))
			{
				continue;
			}

			Directory.CreateDirectory(layout.RunDirectory);
			Directory.CreateDirectory(layout.PluginsDirectory);
			return layout;
		}

		throw new IOException($"No free run directory name below '{root}' for {now:o}.");
	}

	/// <summary>Creates (if needed) and returns the directory of one session.</summary>
	internal string SessionDirectory(string session)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(session);
		string directory = Path.Combine(RunDirectory, "sessions", session);
		Directory.CreateDirectory(directory);
		return directory;
	}
}
