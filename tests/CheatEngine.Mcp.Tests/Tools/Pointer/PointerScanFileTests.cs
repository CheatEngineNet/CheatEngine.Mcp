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
	public void Load_NonSeekableOver32MiBStream_RejectsBeforeParsingAndLeavesInputOpen()
	{
		byte[] content = new byte[PointerScanFile.MaximumBytes + 1];
		Array.Fill(content, (byte) ' ');
		Encoding.UTF8.GetBytes("""
			{"version":2,"mapName":"m","target":0,"width":8,"paths":[],"incomplete":false}
			""").CopyTo(content, 0);
		using ChunkedReadStream input = new(content, 81920);

		InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
			PointerScanFile.Load(input, CancellationToken.None));

		Assert.Equal("Pointer scan JSON exceeds the 33554432-byte limit.", exception.Message);
		Assert.Equal(PointerScanFile.MaximumBytes + 1, input.BytesRead);
		Assert.False(input.WasDisposed);
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
	public void SaveLoad_Version2_ExplicitCompleteCoverage_RoundTripsWithoutChangingIncompleteState()
	{
		PointerScanSnapshot original = new("complete", 0x401000, 8,
		[
			new PointerPath(0x400000, null, 0, [-8, 16])
		], true, CoverageIncomplete: false);
		using MemoryStream json = new();

		PointerScanFile.Save(json, original, CancellationToken.None);
		json.Position = 0;
		PointerScanSnapshot loaded = PointerScanFile.Load(json, CancellationToken.None);

		Assert.True(loaded.Incomplete);
		Assert.False(loaded.CoverageIncomplete);
		Assert.Equal([-8L, 16], Assert.Single(loaded.Paths).Offsets);
	}

	[Fact]
	public void Save_InvalidMapName_FailsBeforeWritingAnyJson()
	{
		PointerScanSnapshot invalid = new("   ", 0, 8,
		[
			new PointerPath(0x400000, null, 0, [0])
		], false);
		using MemoryStream output = new();

		ArgumentException exception = Assert.Throws<ArgumentException>(() =>
			PointerScanFile.Save(output, invalid, CancellationToken.None));

		Assert.Equal("snapshot", exception.ParamName);
		Assert.Empty(output.ToArray());
	}

	[Theory]
	[MemberData(nameof(InvalidSnapshotCaseIds))]
	public void Validate_InvalidSnapshot_RejectsTheSpecificInvariant(string caseId)
	{
		(PointerScanSnapshot snapshot, string expectedMessage) = CreateInvalidSnapshot(caseId);
		ArgumentException exception = Assert.Throws<ArgumentException>(() => PointerScanFile.Validate(snapshot));

		Assert.Equal("snapshot", exception.ParamName);
		Assert.Contains(expectedMessage, exception.Message, StringComparison.Ordinal);
	}

	[Fact]
	public void Load_NonSeekablePartialReads_ConsumesAllChunksAndPreservesCallerStreamOwnership()
	{
		byte[] content = Encoding.UTF8.GetBytes(
			"{\"version\":2,\"mapName\":\"chunked\",\"target\":4096,\"width\":8,\"paths\":[],\"incomplete\":false}");
		using ChunkedReadStream input = new(content, chunkSize: 3);

		PointerScanSnapshot loaded = PointerScanFile.Load(input, CancellationToken.None);

		Assert.Equal(("chunked", 4096UL, 8), (loaded.MapName, loaded.Target, loaded.Width));
		Assert.False(input.WasDisposed);
		Assert.Equal(content.Length, input.BytesRead);
	}

	[Fact]
	public void Load_PartialReadFailure_LeavesCallerOwnedInputOpen()
	{
		byte[] content = Encoding.UTF8.GetBytes("{\"version\":2");
		using ChunkedReadStream input = new(content, chunkSize: 4, failAfterFirstRead: true);

		Assert.Throws<IOException>(() => PointerScanFile.Load(input, CancellationToken.None));

		Assert.False(input.WasDisposed);
		Assert.Equal(4, input.BytesRead);
	}

	[Theory]
	[MemberData(nameof(MalformedSchemaCaseIds))]
	public void Load_MalformedOrPartialSchema_Rejects(string caseId)
	{
		using MemoryStream json = Json(CreateMalformedSchemaContent(caseId));

		Assert.Throws<InvalidDataException>(() => PointerScanFile.Load(json, CancellationToken.None));
	}

	[Fact]
	public void Load_WriteOnlyStream_RejectsBeforeAttemptingToRead()
	{
		using WriteOnlyStream input = new();

		ArgumentException exception = Assert.Throws<ArgumentException>(() =>
			PointerScanFile.Load(input, CancellationToken.None));

		Assert.Equal("input", exception.ParamName);
		Assert.Equal(0, input.ReadAttempts);
	}

	[Fact]
	public void Save_ReadOnlyStream_RejectsBeforeAttemptingToWrite()
	{
		PointerScanSnapshot snapshot = new("map", 0, 8, [new PointerPath(0, null, 0, [0])], false);
		using MemoryStream output = new(Array.Empty<byte>(), writable: false);

		ArgumentException exception = Assert.Throws<ArgumentException>(() =>
			PointerScanFile.Save(output, snapshot, CancellationToken.None));

		Assert.Equal("output", exception.ParamName);
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

	public static IEnumerable<object[]> InvalidSnapshotCaseIds =>
	[
		["invalid_width"],
		["contradictory_coverage"],
		["narrow_target_overflow"],
		["absolute_path_offset"]
	];

	private static (PointerScanSnapshot Snapshot, string ExpectedMessage) CreateInvalidSnapshot(string caseId) => caseId switch
	{
		"invalid_width" =>
			(new PointerScanSnapshot("map", 0, 3, [new PointerPath(0, null, 0, [0])], false), "width must be 4 or 8"),
		"contradictory_coverage" =>
			(new PointerScanSnapshot("map", 0, 8, [new PointerPath(0, null, 0, [0])], false, CoverageIncomplete: true),
				"incomplete source"),
		"narrow_target_overflow" =>
			(new PointerScanSnapshot("map", uint.MaxValue + 1UL, 4, [new PointerPath(0, null, 0, [0])], false), "target does not fit"),
		"absolute_path_offset" =>
			(new PointerScanSnapshot("map", 0, 8, [new PointerPath(0, null, 1, [0])], false), "Absolute pointer paths"),
		_ => throw new ArgumentOutOfRangeException(nameof(caseId), caseId, "Unknown invalid snapshot case.")
	};

	public static IEnumerable<object[]> MalformedSchemaCaseIds =>
	[
		["empty_file"],
		["missing_width"],
		["paths_not_array"],
		["offsets_not_array"]
	];

	private static string CreateMalformedSchemaContent(string caseId) => caseId switch
	{
		"empty_file" => "",
		"missing_width" => "{\"version\":2,\"mapName\":\"m\",\"target\":0,\"paths\":[],\"incomplete\":false}",
		"paths_not_array" => "{\"version\":2,\"mapName\":\"m\",\"target\":0,\"width\":8,\"paths\":{},\"incomplete\":false}",
		"offsets_not_array" => "{\"version\":2,\"mapName\":\"m\",\"target\":0,\"width\":8,\"paths\":[{\"baseAddress\":0,\"module\":null,\"moduleOffset\":0,\"offsets\":0}],\"incomplete\":false}",
		_ => throw new ArgumentOutOfRangeException(nameof(caseId), caseId, "Unknown malformed schema case.")
	};

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

	private sealed class ChunkedReadStream(byte[] content, int chunkSize, bool failAfterFirstRead = false) : Stream
	{
		private readonly byte[] _content = content;
		private readonly int _chunkSize = chunkSize;
		private readonly bool _failAfterFirstRead = failAfterFirstRead;
		private int _position;
		private int _readCount;

		internal bool WasDisposed
		{
			get; private set;
		}
		internal int BytesRead => _position;
		public override bool CanRead => !WasDisposed;
		public override bool CanSeek => false;
		public override bool CanWrite => false;
		public override long Length => throw new NotSupportedException();
		public override long Position
		{
			get => _position;
			set => throw new NotSupportedException();
		}

		public override void Flush() => throw new NotSupportedException();
		public override int Read(byte[] buffer, int offset, int count)
		{
			if (_failAfterFirstRead && _readCount > 0)
			{
				throw new IOException("Synthetic partial-read failure.");
			}

			int remaining = _content.Length - _position;
			if (remaining == 0)
			{
				return 0;
			}

			int read = Math.Min(Math.Min(remaining, count), _chunkSize);
			Array.Copy(_content, _position, buffer, offset, read);
			_position += read;
			_readCount++;
			return read;
		}

		public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
		public override void SetLength(long value) => throw new NotSupportedException();
		public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
		protected override void Dispose(bool disposing)
		{
			WasDisposed = true;
			base.Dispose(disposing);
		}
	}

	private sealed class WriteOnlyStream : Stream
	{
		internal int ReadAttempts
		{
			get; private set;
		}
		public override bool CanRead => false;
		public override bool CanSeek => false;
		public override bool CanWrite => true;
		public override long Length => 0;
		public override long Position
		{
			get; set;
		}
		public override void Flush()
		{
		}
		public override int Read(byte[] buffer, int offset, int count)
		{
			ReadAttempts++;
			throw new NotSupportedException();
		}
		public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
		public override void SetLength(long value) => throw new NotSupportedException();
		public override void Write(byte[] buffer, int offset, int count)
		{
		}
	}
}
