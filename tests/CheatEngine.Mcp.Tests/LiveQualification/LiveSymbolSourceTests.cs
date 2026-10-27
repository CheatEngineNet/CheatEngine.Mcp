using System.Buffers.Binary;
using System.Runtime.Versioning;
using System.Security.Cryptography;

using CheatEngine.Mcp.Tests.Tools.Modules;

namespace CheatEngine.Mcp.Tests.LiveQualification;

[SupportedOSPlatform("windows")]
public sealed class LiveSymbolSourceTests
{
	[Fact]
	public void SymbolStoreKey_ValidIdentity_UsesUppercaseGuidAndHexAge()
	{
		NativeSymbolCodeView identity = new(new Guid("3f2504e0-4f89-11d3-9a0c-0305e82c3301"), 26);

		string key = LiveSymbolSourceFixture.SymbolStoreKey(identity);

		Assert.Equal("3F2504E04F8911D39A0C0305E82C33011A", key);
	}

	[Fact]
	public void SymbolStoreKey_EmptyGuid_RejectsInvalidIdentity()
	{
		Assert.Throws<InvalidDataException>(() => LiveSymbolSourceFixture.SymbolStoreKey(new NativeSymbolCodeView(Guid.Empty, 1)));
	}

	[Fact]
	public void EnvironmentSymbolPathPrefix_OwnedRunRoot_ReturnsPlainTrailingSeparatorWithoutSrvPrefix()
	{
		using Scratch scratch = new();

		string prefix = LiveSymbolSourceFixture.EnvironmentSymbolPathPrefix(scratch.Root);

		Assert.Equal(Path.TrimEndingDirectorySeparator(scratch.Root) + Path.DirectorySeparatorChar, prefix);
		Assert.DoesNotContain("srv*", prefix, StringComparison.OrdinalIgnoreCase);
	}

	[Fact]
	public void Verify_FixtureOutsideOwnedRunRoot_RejectsPath()
	{
		using Scratch scratch = new();
		NativeSymbolFixture fixture = scratch.CreateFixture();
		string differentRoot = Path.Combine(Path.GetTempPath(), "cemcp-symbol-source-other-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(differentRoot);
		try
		{
			InvalidDataException exception = Assert.Throws<InvalidDataException>(() => LiveSymbolSourceFixture.Verify(fixture, differentRoot));
			Assert.Contains("exact owned run root", exception.Message, StringComparison.Ordinal);
		}
		finally
		{
			Directory.Delete(differentRoot, true);
		}
	}

	[Fact]
	public void Verify_PdbHashDiffersFromManifest_RejectsFrozenArtifact()
	{
		using Scratch scratch = new();
		NativeSymbolFixture fixture = scratch.CreateFixture() with
		{
			PdbSha256 = new string('0', 64)
		};

		InvalidDataException exception = Assert.Throws<InvalidDataException>(() => LiveSymbolSourceFixture.Verify(fixture, scratch.Root));

		Assert.Contains("bytes changed", exception.Message, StringComparison.Ordinal);
	}

	[Fact]
	public void Verify_PdbIdentityDiffersFromDll_RejectsFrozenArtifact()
	{
		using Scratch scratch = new();
		NativeSymbolFixture fixture = scratch.CreateFixture(pdbIdentity: new NativeSymbolCodeView(Guid.NewGuid(), 1));

		InvalidDataException exception = Assert.Throws<InvalidDataException>(() => LiveSymbolSourceFixture.Verify(fixture, scratch.Root));

		Assert.Contains("CodeView record", exception.Message, StringComparison.Ordinal);
	}

	[Fact]
	public void Stage_ValidFixture_CopiesToExactCacheAndRollsBackOriginal()
	{
		using Scratch scratch = new();
		NativeSymbolFixture fixture = scratch.CreateFixture();
		string originalSha256 = fixture.PdbSha256;
		string expected = Path.Combine(scratch.Root, "Cheat Engine Symbols", Path.GetFileName(fixture.PdbPath),
			LiveSymbolSourceFixture.SymbolStoreKey(fixture.CodeView), Path.GetFileName(fixture.PdbPath));

		using (LiveSymbolSourceFixtureStage stage = LiveSymbolSourceFixture.Stage(fixture, scratch.Root))
		{
			Assert.Equal(expected, stage.CachePdbPath, ignoreCase: true);
			Assert.Equal(expected, stage.Manifest.ExpectedCachePdbPath, ignoreCase: true);
			Assert.Equal(Path.Combine(scratch.Root, "Cheat Engine Symbols"), stage.Manifest.DerivedCheatEngineCacheRoot,
				ignoreCase: true);
			Assert.Equal(Path.TrimEndingDirectorySeparator(scratch.Root) + Path.DirectorySeparatorChar,
				stage.Manifest.EnvironmentSymbolPathPrefix);
			Assert.Equal(fixture.PdbPath, stage.OriginalPdbPath, ignoreCase: true);
			Assert.Equal(fixture.CodeView, stage.Identity);
			Assert.Equal(originalSha256, stage.PdbSha256);
			Assert.False(File.Exists(fixture.PdbPath));
			Assert.True(File.Exists(stage.BackupPdbPath));
			Assert.Equal(originalSha256, Hash(stage.CachePdbPath));
			Assert.Equal(fixture.CodeView, NativeSymbolFixture.ReadPdbIdentity(stage.CachePdbPath, fixture.PdbLength));
		}

		Assert.True(File.Exists(fixture.PdbPath));
		Assert.Equal(originalSha256, Hash(fixture.PdbPath));
		Assert.False(File.Exists(expected));
	}

	private static string Hash(string path)
	{
		using FileStream input = File.OpenRead(path);
		return Convert.ToHexString(SHA256.HashData(input));
	}

	private sealed class Scratch : IDisposable
	{
		internal string Root { get; } = Path.Combine(Path.GetTempPath(), "cemcp-symbol-source-tests-" + Guid.NewGuid().ToString("N"));

		internal Scratch()
		{
			Directory.CreateDirectory(Root);
		}

		internal NativeSymbolFixture CreateFixture(NativeSymbolCodeView? pdbIdentity = null)
		{
			NativeSymbolCodeView identity = new(new Guid("3f2504e0-4f89-11d3-9a0c-0305e82c3301"), 26);
			NativeSymbolCodeView actualPdbIdentity = pdbIdentity ?? identity;
			string source = Path.Combine(Root, "fixture.cpp");
			string dll = Path.Combine(Root, "fixture.dll");
			string pdb = Path.Combine(Root, "fixture.pdb");
			File.WriteAllText(source, "fixture source");
			File.WriteAllBytes(pdb, CreatePdb(actualPdbIdentity));
			File.WriteAllBytes(dll, CreateDll(identity, Path.GetFileName(pdb)));
			IReadOnlyDictionary<string, uint> exports = NativeSymbolFixture.ReadRequiredExportRvas(dll,
				["CemcpFixtureLayoutSize", "CemcpFixtureTick"]);
			return new NativeSymbolFixture(source, dll, pdb, Hash(source), Hash(dll), Hash(pdb), new FileInfo(dll).Length,
				new FileInfo(pdb).Length, NativeSymbolFixture.ReadCodeView(dll), exports);
		}

		public void Dispose()
		{
			if (Directory.Exists(Root))
			{
				Directory.Delete(Root, true);
			}
		}

		private static byte[] CreateDll(NativeSymbolCodeView identity, string pdbName)
		{
			const uint rdataRva = 0x1000;
			const uint debugRva = rdataRva + 0x400;
			const uint textRva = 0x2000;
			byte[] rdata = new byte[0x1000];
			byte[] exports = TestPe.ExportData(rdataRva, "fixture.dll", 1,
			[
				("CemcpFixtureLayoutSize", textRva + 0x10, null),
				("CemcpFixtureTick", textRva + 0x20, null)
			]);
			exports.CopyTo(rdata, 0);
			byte[] debug = TestPe.DebugData(debugRva, identity.Guid, unchecked((uint) identity.Age), pdbName);
			debug.CopyTo(rdata, (int) (debugRva - rdataRva));
			TestPe pe = new()
			{
				Is64 = true,
				Machine = 0x8664,
				Characteristics = 0x2022,
				EntryPoint = textRva + 0x10
			};
			pe.AddSection(".rdata", rdataRva, rdata, TestPe.ReadOnlyData)
				.AddSection(".text", textRva, new byte[0x200], TestPe.Code)
				.SetDirectory(0, rdataRva, (uint) exports.Length)
				.SetDirectory(6, debugRva, 28);
			byte[] file = pe.File();
			const int rdataRaw = 0x400;
			BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(rdataRaw + (int) (debugRva - rdataRva) + 24, 4),
				(uint) (rdataRaw + (int) (debugRva - rdataRva) + 28));
			return file;
		}

		private static byte[] CreatePdb(NativeSymbolCodeView identity)
		{
			const int blockSize = 512;
			byte[] pdb = new byte[blockSize * 4];
			"Microsoft C/C++ MSF 7.00\r\n\u001ADS\0\0\0"u8.CopyTo(pdb);
			BinaryPrimitives.WriteInt32LittleEndian(pdb.AsSpan(32, 4), blockSize);
			BinaryPrimitives.WriteInt32LittleEndian(pdb.AsSpan(40, 4), 4);
			BinaryPrimitives.WriteInt32LittleEndian(pdb.AsSpan(44, 4), 16);
			BinaryPrimitives.WriteInt32LittleEndian(pdb.AsSpan(52, 4), 1);
			BinaryPrimitives.WriteInt32LittleEndian(pdb.AsSpan(blockSize, 4), 2);
			Span<byte> directory = pdb.AsSpan(blockSize * 2, 16);
			BinaryPrimitives.WriteInt32LittleEndian(directory, 2);
			BinaryPrimitives.WriteInt32LittleEndian(directory[4..], 0);
			BinaryPrimitives.WriteInt32LittleEndian(directory[8..], 28);
			BinaryPrimitives.WriteInt32LittleEndian(pdb.AsSpan((blockSize * 2) + 12, 4), 3);
			Span<byte> info = pdb.AsSpan(blockSize * 3, 28);
			BinaryPrimitives.WriteInt32LittleEndian(info, 20000404);
			BinaryPrimitives.WriteInt32LittleEndian(info[8..], identity.Age);
			identity.Guid.TryWriteBytes(info[12..]);
			return pdb;
		}
	}
}
