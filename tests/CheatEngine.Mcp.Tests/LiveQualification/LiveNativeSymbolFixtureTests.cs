using System.Runtime.Versioning;

namespace CheatEngine.Mcp.Tests.LiveQualification;

[SupportedOSPlatform("windows")]
public sealed class LiveNativeSymbolFixtureTests
{
	[Fact]
	public void ReadPdbIdentity_RejectsMalformedHeader()
	{
		using Scratch scratch = new();
		string path = Path.Combine(scratch.Root, "bad.pdb");
		File.WriteAllBytes(path, new byte[56]);
		Assert.Throws<InvalidDataException>(() => NativeSymbolFixture.ReadPdbIdentity(path, 56));
	}

	[Fact]
	public void VerifyBeforeHostStart_RejectsOversizePdbBeforeHashing()
	{
		using Scratch scratch = new();
		string source = Path.Combine(scratch.Root, "fixture.cpp");
		string dll = Path.Combine(scratch.Root, "fixture.dll");
		string pdb = Path.Combine(scratch.Root, "fixture.pdb");
		File.WriteAllText(source, "x");
		File.WriteAllBytes(dll, [1]);
		using (FileStream stream = File.Create(pdb))
		{
			stream.SetLength((16 * 1024 * 1024) + 1L);
		}

		NativeSymbolFixture fixture = new(source, dll, pdb, "00", "00", "00", 1, new FileInfo(pdb).Length, new NativeSymbolCodeView(Guid.Empty, 0), new Dictionary<string, uint> { ["CemcpFixtureLayoutSize"] = 1, ["CemcpFixtureTick"] = 2 });
		InvalidDataException exception = Assert.Throws<InvalidDataException>(fixture.VerifyBeforeHostStart);
		Assert.Contains("exceeded a reviewed bound", exception.Message, StringComparison.Ordinal);
	}

	[Fact]
	public void ExactExportOracle_RejectsExtraOrdinalOnlyFunctionBeforeTableParsing()
	{
		InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
			NativeSymbolFixture.ValidateExactExportCounts(functions: 3, names: 2, requiredCount: 2));
		Assert.Contains("exact set", exception.Message, StringComparison.Ordinal);
	}

	[Theory]
	[InlineData(0)]
	[InlineData((16 * 1024 * 1024) + 1L)]
	public void ImageSizeOracle_RejectsUnmappablePeSizes(long size)
	{
		InvalidDataException exception = Assert.Throws<InvalidDataException>(() => NativeSymbolFixture.ValidateImageSize(size));
		Assert.Contains("target-allocation bound", exception.Message, StringComparison.Ordinal);
	}

	[Fact]
	public void ImageSizeOracle_AcceptsReviewedMappedImageSize()
	{
		Assert.Equal(4096L, NativeSymbolFixture.ValidateImageSize(4096));
	}

	private sealed class Scratch : IDisposable
	{
		internal string Root { get; } = Path.Combine(Path.GetTempPath(), "cemcp-native-symbol-tests-" + Guid.NewGuid().ToString("N"));
		internal Scratch() => Directory.CreateDirectory(Root);
		public void Dispose()
		{
			if (Directory.Exists(Root))
			{
				Directory.Delete(Root, true);
			}
		}
	}
}
