using System.Globalization;
using System.Runtime.Versioning;
using System.Security.Cryptography;

namespace CheatEngine.Mcp.Tests.LiveQualification;

/// <summary>Prepares one verified native fixture PDB for isolated symbol-source qualification without starting a host.</summary>
[SupportedOSPlatform("windows")]
internal static class LiveSymbolSourceFixture
{
	// CEFuncProc.pas appends this English resource name to _NT_SYMBOL_PATH before it adds the srv* search prefix.
	internal const string CacheDirectoryName = "Cheat Engine Symbols";
	internal const string BackupDirectoryName = "symbol-source-backup";

	internal static LiveSymbolSourceFixtureStage Stage(NativeSymbolFixture fixture, string ownedRunRoot)
	{
		LiveSymbolSourceFixtureManifest manifest = Verify(fixture, ownedRunRoot);
		return LiveSymbolSourceFixtureStage.Create(manifest);
	}

	internal static LiveSymbolSourceFixtureManifest Verify(NativeSymbolFixture fixture, string ownedRunRoot)
	{
		ArgumentNullException.ThrowIfNull(fixture);
		ArgumentException.ThrowIfNullOrWhiteSpace(ownedRunRoot);
		string root = Path.TrimEndingDirectorySeparator(EnvironmentSymbolPathPrefix(ownedRunRoot));
		RequireInsideRun(fixture.DllPath, root);
		RequireInsideRun(fixture.PdbPath, root);
		fixture.VerifyBeforeHostStart();

		string pdbName = Path.GetFileName(fixture.PdbPath);
		if (string.IsNullOrWhiteSpace(pdbName) || !string.Equals(Path.GetExtension(pdbName), ".pdb", StringComparison.OrdinalIgnoreCase))
		{
			throw new InvalidDataException("The reviewed fixture PDB has an invalid file name.");
		}

		NativeSymbolCodeView dllIdentity = NativeSymbolFixture.ReadCodeView(fixture.DllPath);
		NativeSymbolCodeView pdbIdentity = NativeSymbolFixture.ReadPdbIdentity(fixture.PdbPath, fixture.PdbLength);
		if (dllIdentity != fixture.CodeView || pdbIdentity != fixture.CodeView || dllIdentity.Guid == Guid.Empty || dllIdentity.Age < 0)
		{
			throw new InvalidDataException("The frozen fixture DLL and PDB do not have the same reviewed CodeView identity.");
		}

		string dllSha256 = Hash(fixture.DllPath, fixture.DllLength);
		string pdbSha256 = Hash(fixture.PdbPath, fixture.PdbLength);
		if (!string.Equals(dllSha256, fixture.DllSha256, StringComparison.OrdinalIgnoreCase) ||
			!string.Equals(pdbSha256, fixture.PdbSha256, StringComparison.OrdinalIgnoreCase))
		{
			throw new InvalidDataException("The frozen fixture DLL or PDB hash differs from its build manifest.");
		}

		string key = SymbolStoreKey(fixture.CodeView);
		string cacheRoot = Path.Combine(root, CacheDirectoryName);
		string backupRoot = Path.Combine(root, BackupDirectoryName);
		string cachePdbPath = Path.Combine(cacheRoot, pdbName, key, pdbName);
		string backupPdbPath = Path.Combine(backupRoot, pdbName);
		RequireInsideRun(cachePdbPath, root);
		RequireInsideRun(backupPdbPath, root);
		return new LiveSymbolSourceFixtureManifest(root, fixture.DllPath, fixture.PdbPath, backupPdbPath, cachePdbPath,
			pdbName, key, dllSha256, pdbSha256, fixture.CodeView, fixture.DllLength, fixture.PdbLength);
	}

	/// <summary>The plain, pre-enable _NT_SYMBOL_PATH prefix CE extends with its private cache directory.</summary>
	internal static string EnvironmentSymbolPathPrefix(string ownedRunRoot)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(ownedRunRoot);
		if (!Path.IsPathFullyQualified(ownedRunRoot) || !Directory.Exists(ownedRunRoot))
		{
			throw new InvalidDataException("The symbol-source fixture run root must be an existing absolute directory.");
		}

		string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(ownedRunRoot));
		LiveCompilerCandidate.RequireNoReparseAncestors(root);
		return root + Path.DirectorySeparatorChar;
	}

	internal static string SymbolStoreKey(NativeSymbolCodeView identity)
	{
		if (identity.Guid == Guid.Empty || identity.Age < 0)
		{
			throw new InvalidDataException("The CodeView identity cannot form a symbol-store key.");
		}

		return identity.Guid.ToString("N").ToUpperInvariant() + unchecked((uint) identity.Age).ToString("X", CultureInfo.InvariantCulture);
	}

	internal static void RequireInsideRun(string path, string root)
	{
		if (!Path.IsPathFullyQualified(path) || !LiveQualificationOptIn.IsSameOrBelow(path, root) ||
			string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(path)), root, StringComparison.OrdinalIgnoreCase))
		{
			throw new InvalidDataException("A symbol-source fixture path escaped the exact owned run root.");
		}
	}

	internal static string Hash(string path, long expectedLength)
	{
		using FileStream input = new(path, FileMode.Open, FileAccess.Read, FileShare.Read);
		if (input.Length != expectedLength || input.Length <= 0)
		{
			throw new InvalidDataException("A frozen symbol-source fixture file changed while it was being checked.");
		}

		return Convert.ToHexString(SHA256.HashData(input));
	}
}

/// <summary>Verified manifest facts and the independently derived symbol-server layout for one private fixture cache.</summary>
[SupportedOSPlatform("windows")]
internal sealed record LiveSymbolSourceFixtureManifest(string OwnedRunRoot, string DllPath, string OriginalPdbPath,
	string BackupPdbPath, string ExpectedCachePdbPath, string PdbName, string SymbolStoreKey, string DllSha256,
	string PdbSha256, NativeSymbolCodeView Identity, long DllLength, long PdbLength)
{
	/// <summary>The exact plain _NT_SYMBOL_PATH value set before CE enables its symbol-server search path.</summary>
	internal string EnvironmentSymbolPathPrefix => LiveSymbolSourceFixture.EnvironmentSymbolPathPrefix(OwnedRunRoot);

	/// <summary>The CE-derived local cache root after its fixed English resource directory is appended.</summary>
	internal string DerivedCheatEngineCacheRoot => Path.Combine(OwnedRunRoot, LiveSymbolSourceFixture.CacheDirectoryName);
}

/// <summary>Owns a reversible PDB move and exposes the exact private symbol-server cache path used by the probe.</summary>
[SupportedOSPlatform("windows")]
internal sealed class LiveSymbolSourceFixtureStage : IDisposable
{
	private bool _rolledBack;

	private LiveSymbolSourceFixtureStage(LiveSymbolSourceFixtureManifest manifest)
	{
		Manifest = manifest;
	}

	internal LiveSymbolSourceFixtureManifest Manifest
	{
		get;
	}

	internal string CachePdbPath => Manifest.ExpectedCachePdbPath;
	internal string BackupPdbPath => Manifest.BackupPdbPath;
	internal string OriginalPdbPath => Manifest.OriginalPdbPath;
	internal string PdbSha256 => Manifest.PdbSha256;
	internal NativeSymbolCodeView Identity => Manifest.Identity;

	internal static LiveSymbolSourceFixtureStage Create(LiveSymbolSourceFixtureManifest manifest)
	{
		ArgumentNullException.ThrowIfNull(manifest);
		string cacheRoot = Path.Combine(manifest.OwnedRunRoot, LiveSymbolSourceFixture.CacheDirectoryName);
		string backupRoot = Path.Combine(manifest.OwnedRunRoot, LiveSymbolSourceFixture.BackupDirectoryName);
		if (Directory.Exists(cacheRoot) || Directory.Exists(backupRoot))
		{
			throw new InvalidDataException("The private symbol-source cache or backup directory already exists.");
		}

		LiveSymbolSourceFixtureStage stage = new(manifest);
		try
		{
			Directory.CreateDirectory(Path.GetDirectoryName(manifest.ExpectedCachePdbPath)!);
			Directory.CreateDirectory(Path.GetDirectoryName(manifest.BackupPdbPath)!);
			File.Copy(manifest.OriginalPdbPath, manifest.ExpectedCachePdbPath, overwrite: false);
			stage.VerifyCacheCopy();
			File.Move(manifest.OriginalPdbPath, manifest.BackupPdbPath);
			stage.VerifyBackup();
			return stage;
		}
		catch (Exception failure)
		{
			try
			{
				stage.Rollback();
			}
			catch (Exception rollbackFailure)
			{
				throw new AggregateException(failure, rollbackFailure);
			}
			throw;
		}
	}

	internal void Rollback()
	{
		if (_rolledBack)
		{
			return;
		}

		if (File.Exists(BackupPdbPath))
		{
			if (File.Exists(OriginalPdbPath))
			{
				throw new InvalidDataException("The original PDB reappeared before the owned backup could be restored.");
			}
			File.Move(BackupPdbPath, OriginalPdbPath);
			if (!string.Equals(LiveSymbolSourceFixture.Hash(OriginalPdbPath, Manifest.PdbLength), PdbSha256,
				StringComparison.OrdinalIgnoreCase))
			{
				throw new InvalidDataException("The restored fixture PDB hash differs from its build manifest.");
			}
		}

		if (File.Exists(CachePdbPath))
		{
			File.Delete(CachePdbPath);
		}
		DeleteIfEmpty(Path.GetDirectoryName(CachePdbPath)!);
		DeleteIfEmpty(Path.GetDirectoryName(Path.GetDirectoryName(CachePdbPath)!)!);
		DeleteIfEmpty(Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(CachePdbPath)!)!)!);
		DeleteIfEmpty(Path.GetDirectoryName(BackupPdbPath)!);
		_rolledBack = true;
	}

	public void Dispose()
	{
		Rollback();
	}

	private void VerifyCacheCopy()
	{
		if (!File.Exists(CachePdbPath) || LiveSymbolSourceFixture.Hash(CachePdbPath, Manifest.PdbLength) != PdbSha256 ||
			NativeSymbolFixture.ReadPdbIdentity(CachePdbPath, Manifest.PdbLength) != Identity)
		{
			throw new InvalidDataException("The private symbol-server cache copy did not preserve the reviewed PDB bytes and identity.");
		}
	}

	private void VerifyBackup()
	{
		if (File.Exists(OriginalPdbPath) || !File.Exists(BackupPdbPath) ||
			LiveSymbolSourceFixture.Hash(BackupPdbPath, Manifest.PdbLength) != PdbSha256 ||
			NativeSymbolFixture.ReadPdbIdentity(BackupPdbPath, Manifest.PdbLength) != Identity)
		{
			throw new InvalidDataException("The original fixture PDB was not moved into its owned backup intact.");
		}
	}

	private static void DeleteIfEmpty(string path)
	{
		if (Directory.Exists(path) && !Directory.EnumerateFileSystemEntries(path).Any())
		{
			Directory.Delete(path);
		}
	}
}
