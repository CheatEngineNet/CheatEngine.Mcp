using System.Text;
using System.Text.Json;

using CheatEngine.Mcp.Core.Contract;
using CheatEngine.Mcp.Tools.Pointer;

namespace CheatEngine.Mcp.Tests.Tools.Pointer;

/// <summary>Bounded, schema-strict JSON persistence for pointer-scan paths.</summary>
public sealed class PointerScanFileTests
{
	[Fact]
	public void SaveLoad_Version2_RoundTripsMetadataSignedOffsetsAndUnresolvedPaths()
	{
		PointerScanSnapshot original = new("capture-2026", 0xFEDCBA9876543210, 8,
		[
			new PointerPath(0x1234, "game.exe", 0x234, [-0x100000, 0, 0x100000])
			{
				Verification = PointerVerification.LiveMatch
			}
		], true);
		using MemoryStream json = new();

		PointerScanFile.Save(json, original, CancellationToken.None);
		json.Position = 0;
		PointerScanSnapshot loaded = PointerScanFile.Load(json, CancellationToken.None);

		Assert.Equal(("capture-2026", 0xFEDCBA9876543210UL, 8, true),
			(loaded.MapName, loaded.Target, loaded.Width, loaded.Incomplete));
		PointerPath path = Assert.Single(loaded.Paths);
		Assert.Equal((0x1234UL, "game.exe", 0x234UL), (path.BaseAddress, path.Module, path.ModuleOffset));
		Assert.Equal([-0x100000L, 0, 0x100000], path.Offsets);
		Assert.Equal(PointerVerification.Unresolved, path.Verification);
		Assert.Contains("\"version\":2", Encoding.UTF8.GetString(json.ToArray()), StringComparison.Ordinal);
	}

	[Fact]
	public void Load_Version1_PreservesLegacyContentAndUsesImportMetadata()
	{
		using MemoryStream json = Json("""
			{"version":1,"width":4,"paths":[{"baseAddress":4096,"module":null,"moduleOffset":0,"offsets":[0,1048576]}],"incomplete":false}
			""");

		PointerScanSnapshot loaded = PointerScanFile.Load(json, CancellationToken.None);

		Assert.Equal(("imported", 0UL, 4, false), (loaded.MapName, loaded.Target, loaded.Width, loaded.Incomplete));
		PointerPath path = Assert.Single(loaded.Paths);
		Assert.Equal([0L, 1048576], path.Offsets);
		Assert.Equal(PointerVerification.Unresolved, path.Verification);
	}

	[Theory]
	[InlineData("""{"version":1,"width":8,"paths":[{"baseAddress":4096,"module":null,"moduleOffset":0,"offsets":[-1]}],"incomplete":false}""")]
	[InlineData("""{"version":2,"mapName":"m","target":0,"width":8,"paths":[{"baseAddress":4096,"module":null,"moduleOffset":0,"offsets":[-1048577]}],"incomplete":false}""")]
	[InlineData("""{"version":2,"mapName":"m","target":0,"width":8,"paths":[{"baseAddress":4096,"module":null,"moduleOffset":0,"offsets":[1048577]}],"incomplete":false}""")]
	public void Load_OffsetsOutsideVersionBounds_Rejects(string content)
	{
		using MemoryStream json = Json(content);

		Assert.Throws<InvalidDataException>(() => PointerScanFile.Load(json, CancellationToken.None));
	}

	[Theory]
	[InlineData("""{"version":2,"mapName":"m","mapName":"again","target":0,"width":8,"paths":[],"incomplete":false}""")]
	[InlineData("""{"version":2,"mapName":"m","target":0,"width":8,"paths":[],"incomplete":false,"extra":true}""")]
	[InlineData("""{"version":2,"mapName":"m","target":"0","width":8,"paths":[],"incomplete":false}""")]
	[InlineData("""{"version":2,"mapName":"m","target":0,"width":8,"paths":[[]],"incomplete":false}""")]
	public void Load_UnknownDuplicateOrWronglyTypedFields_Rejects(string content)
	{
		using MemoryStream json = Json(content);

		Assert.Throws<InvalidDataException>(() => PointerScanFile.Load(json, CancellationToken.None));
	}

	[Fact]
	public void Load_Over32MiBStream_RejectsBeforeParsing()
	{
		using Stream oversized = new DeclaredLengthStream(PointerScanFile.MaximumBytes + 1);

		Assert.Throws<InvalidDataException>(() => PointerScanFile.Load(oversized, CancellationToken.None));
	}

	[Fact]
	public void Load_JsonExceedingMaximumDepth_Rejects()
	{
		using MemoryStream json = Json("""
			{"version":2,"mapName":"m","target":0,"width":8,"paths":[[[[[[[[[]]]]]]]],"incomplete":false}
			""");

		Assert.ThrowsAny<JsonException>(() => PointerScanFile.Load(json, CancellationToken.None));
	}

	[Fact]
	public void SnapshotForSave_DeepCopiesPathsAndRefusesConcurrentRescan()
	{
		PointerScanSlot slot = new("scan", "capture", 0x5000, 8, 1);
		slot.LoadImportedSnapshot(new PointerScanSnapshot("capture", 0x5000, 8,
			[new PointerPath(0x1000, null, 0, [-4])], false));

		PointerScanSnapshot saved = slot.SnapshotForSave();
		saved.Paths[0].Offsets[0] = 99;
		Assert.Equal(-4, Assert.Single(slot.GetUsablePaths(out _)).Offsets[0]);
		Assert.Equal(PointerVerification.Unresolved, Assert.Single(slot.GetUsablePaths(out _)).Verification);
		Assert.True(slot.Describe().Incomplete, "An imported unresolved path must keep the scan incomplete.");
		Assert.True(slot.TryBeginRescan());
		CheatEngineToolException busy = Assert.Throws<CheatEngineToolException>(() => slot.SnapshotForSave());
		Assert.Equal(ToolErrorKind.Busy, busy.Error.Kind);
		CheatEngineToolException importBusy = Assert.Throws<CheatEngineToolException>(() =>
			slot.LoadImportedSnapshot(new PointerScanSnapshot("capture", 0x5000, 8,
				[new PointerPath(0x2000, null, 0, [8])], false)));
		Assert.Equal(ToolErrorKind.Busy, importBusy.Error.Kind);
		Assert.Equal(0x1000UL, Assert.Single(slot.GetUsablePaths(out _)).BaseAddress);
		slot.EndRescan();
		PointerScanSnapshot pendingSave = slot.SnapshotForSave();
		Assert.True(pendingSave.Incomplete);
		Assert.False(pendingSave.CoverageIncomplete);
		using MemoryStream persisted = new();
		PointerScanFile.Save(persisted, pendingSave, CancellationToken.None);
		persisted.Position = 0;
		Assert.False(PointerScanFile.Load(persisted, CancellationToken.None).CoverageIncomplete);
		Assert.True(slot.TryBeginRescan(out PointerPath[] paths, out bool coverageIncomplete));
		Assert.False(coverageIncomplete);
		slot.Replace([paths[0] with { Verification = PointerVerification.LiveMatch }], coverageIncomplete);
		slot.EndRescan();
		Assert.False(slot.Describe().Incomplete, "A verified imported path should no longer make a complete scan incomplete.");
	}

	private static MemoryStream Json(string content) => new(Encoding.UTF8.GetBytes(content));

	private sealed class DeclaredLengthStream(long length) : Stream
	{
		public override bool CanRead => true;
		public override bool CanSeek => true;
		public override bool CanWrite => false;
		public override long Length => length;

		public override long Position
		{
			get;
			set;
		}

		public override void Flush()
		{
		}

		public override int Read(byte[] buffer, int offset, int count) => 0;
		public override long Seek(long offset, SeekOrigin origin) => Position;
		public override void SetLength(long value) => throw new NotSupportedException();
		public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
	}
}
