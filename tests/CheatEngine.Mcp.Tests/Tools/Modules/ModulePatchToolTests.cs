using System.Buffers.Binary;

using CheatEngine.Mcp.Core.Contract;
using CheatEngine.Mcp.Core.Files;
using CheatEngine.Mcp.Tests.Core;
using CheatEngine.Mcp.Tools.Modules;

namespace CheatEngine.Mcp.Tests.Tools.Modules;

/// <summary><c>module_find_patches</c>: the comparison itself and the host-file rules on the module's path.</summary>
public sealed class ModulePatchToolTests : IDisposable
{
	private readonly McpFilePathsTests.Scratch _scratch = new();
	private static CancellationToken Token => TestContext.Current.CancellationToken;

	public static TheoryData<string> RefusedModulePaths => new()
	{
		"\\\\attacker\\share\\sample.dll",
		"//attacker/share/sample.dll",
		"\\\\?\\C:\\Games\\sample.dll",
		"\\\\.\\C:\\Games\\sample.dll",
		"C:\\Games\\sample.dll:payload",
		"C:\\Games\\CON",
		"relative\\sample.dll",
		""
	};

	public void Dispose()
	{
		_scratch.Dispose();
	}

	[Fact]
	public void FindPatches_HookedCode_ReportsTheRangesAfterApplyingRelocations()
	{
		TestPe pe = SampleModule.Build();
		string file = WriteModule(pe);
		ModuleSymbolTarget target = ModuleToolTests.LoadedSample(pe, file);
		uint hook = SampleModule.TextRva + 0x40;
		byte[] jump = [0xE9, 0x11, 0x22, 0x33, 0x44];
		jump.CopyTo(target.Memory, (int) hook);
		target.Names[SampleModule.LoadedBase + hook] = "sample.Alpha+30";

		PatchScanResult result = Tool(target).FindPatches("sample.dll", cancellationToken: Token);

		ModulePatch patch = Assert.Single(result.Patches);
		Assert.Equal(new ModulePatch("7FFA00004040", ".text", 5, "91 92 93 94 95", "E9 11 22 33 44", "sample.Alpha+30"),
			patch);
		Assert.Equal((file, SampleModule.TextSize, 0L, 2, false),
			(result.FilePath, (int) result.ComparedBytes, result.UnreadableBytes, result.RelocationsApplied,
				result.Truncated));
	}

	[Fact]
	public void FindPatches_DataSections_AreComparedOnlyWhenAskedAndTheImportTableIsSkipped()
	{
		TestPe pe = SampleModule.Build();
		ModuleSymbolTarget target = ModuleToolTests.LoadedSample(pe, WriteModule(pe));
		// The loader writes the import address table and the program writes its data.
		BinaryPrimitives.WriteUInt64LittleEndian(target.Memory.AsSpan((int) SampleModule.IatRva), 0x7FFB12345678);
		target.Memory[SampleModule.DataRva + 8] = 0x01;

		PatchScanResult code = Tool(target).FindPatches("sample.dll", cancellationToken: Token);
		PatchScanResult all = Tool(target).FindPatches("sample.dll", true, cancellationToken: Token);

		Assert.Empty(code.Patches);
		ModulePatch data = Assert.Single(all.Patches);
		Assert.Equal(("7FFA00002008", ".data", "AB", "01"), (data.Address, data.Section, data.FileBytes,
			data.MemoryBytes));
	}

	[Fact]
	public void FindPatches_UnreadablePage_IsSkippedCountedAndBreaksTheRange()
	{
		TestPe pe = SampleModule.Build();
		ModuleSymbolTarget target = ModuleToolTests.LoadedSample(pe, WriteModule(pe));
		target.UnreadablePages.Add(SampleModule.LoadedBase + SampleModule.TextRva + 0x1000);
		target.Memory[SampleModule.TextRva + 0xFFF] = 0xCC;

		PatchScanResult result = Tool(target).FindPatches("sample.dll", cancellationToken: Token);

		Assert.Equal((0x1000L, 0x1000L), (result.ComparedBytes, result.UnreadableBytes));
		Assert.Equal("7FFA00004FFF", Assert.Single(result.Patches).Address);
	}

	[Fact]
	public void FindPatches_LargeSection_IsReadInChunksAndStopsAtTheLimit()
	{
		TestPe pe = SampleModule.Build(0x50000);
		ModuleSymbolTarget target = ModuleToolTests.LoadedSample(pe, WriteModule(pe));
		for (int index = 0; index < 5; index++)
		{
			target.Memory[SampleModule.TextRva + 0x200 + (index * 0x100)] ^= 0xFF;
		}

		PatchScanResult result = Tool(target).FindPatches("sample.dll", limit: 3, cancellationToken: Token);

		Assert.True(result.Truncated);
		Assert.Equal(3, result.Patches.Length);
		// Locate, headers, one chunk (the limit is reached in the first 256 KiB), then the names.
		Assert.Equal(4, target.Dispatcher.Calls);
	}

	[Fact]
	public void FindPatches_LargeUnreadableSection_StopsAtTheAttemptedByteLimit()
	{
		int textSize = checked((int) ModulePatchTools.MaximumComparedBytes + ModulePatchTools.ChunkBytes);
		TestPe pe = SampleModule.Build(textSize);
		ModuleSymbolTarget target = ModuleToolTests.LoadedSample(pe, WriteModule(pe));
		ulong firstUnreadable = SampleModule.LoadedBase + SampleModule.TextRva;
		for (int offset = 0; offset < textSize; offset += 4096)
		{
			target.UnreadablePages.Add(firstUnreadable + (uint) offset);
		}

		PatchScanResult result = Tool(target).FindPatches("sample.dll", cancellationToken: Token);

		Assert.True(result.Truncated);
		Assert.Equal(0L, result.ComparedBytes);
		Assert.Equal(ModulePatchTools.MaximumComparedBytes, result.UnreadableBytes);
	}

	[Fact]
	public void FindPatches_LimitReachedInsideChunk_CountsOnlyInspectedBytes()
	{
		TestPe pe = SampleModule.Build();
		ModuleSymbolTarget target = ModuleToolTests.LoadedSample(pe, WriteModule(pe));
		target.Memory[SampleModule.TextRva + 0x20] ^= 0xFF;
		target.Memory[SampleModule.TextRva + 0x80] ^= 0xFF;

		PatchScanResult result = Tool(target).FindPatches("sample.dll", limit: 1, cancellationToken: Token);

		Assert.True(result.Truncated);
		Assert.Equal((0x81L, 0L, 1), (result.ComparedBytes, result.UnreadableBytes, result.Patches.Length));
	}

	[Fact]
	public void FindPatches_WholeLargeSection_SpansSeveralChunks()
	{
		TestPe pe = SampleModule.Build(0x50000);
		ModuleSymbolTarget target = ModuleToolTests.LoadedSample(pe, WriteModule(pe));
		target.Memory[SampleModule.TextRva + 0x4FFF0] = 0x00;

		PatchScanResult result = Tool(target).FindPatches("sample.dll", cancellationToken: Token);

		Assert.Equal(0x50000L, result.ComparedBytes);
		Assert.Single(result.Patches);
		Assert.Equal(2, target.Calls("ReadBytesDetailed") - 1);
	}

	[Fact]
	public void FindPatches_RelocationSpanningChunks_IsNotReportedAsPatch()
	{
		uint relocation = SampleModule.TextRva + ModulePatchTools.ChunkBytes - 4;
		int textSize = ModulePatchTools.ChunkBytes + 0x100;
		TestPe pe = SampleModule.Build(textSize, additionalRelocationRva: relocation);
		ModuleSymbolTarget target = ModuleToolTests.LoadedSample(pe, WriteModule(pe));

		PatchScanResult result = Tool(target).FindPatches("sample.dll", cancellationToken: Token);

		Assert.Equal(((long) textSize, 0L, 3, false), (result.ComparedBytes, result.UnreadableBytes,
			result.RelocationsApplied, result.Truncated));
		Assert.Empty(result.Patches);
	}

	[Fact]
	public void FindPatches_TargetChangesDuringTheScan_IsTargetChanged()
	{
		TestPe pe = SampleModule.Build(0x50000);
		ModuleSymbolTarget target = ModuleToolTests.LoadedSample(pe, WriteModule(pe));
		target.BeforeRead = request =>
		{
			if (request.Length == ModulePatchTools.ChunkBytes)
			{
				target.SelectionEpoch++;
			}
		};

		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() =>
			Tool(target).FindPatches("sample.dll", cancellationToken: Token));

		Assert.Equal(ToolErrorKind.TargetChanged, exception.Error.Kind);
	}

	[Fact]
	public void FindPatches_OtherBuildOnDisk_IsInvalidStateBeforeComparing()
	{
		TestPe loaded = SampleModule.Build();
		TestPe updated = new()
		{
			TimeDateStamp = 0x60000000,
			ImageBase = SampleModule.PreferredBase,
			Characteristics = 0x2022
		};
		foreach (TestPe.TestSection section in loaded.Sections)
		{
			updated.AddSection(section.Name, section.VirtualAddress, section.Data, section.Characteristics);
		}

		ModuleSymbolTarget target = ModuleToolTests.LoadedSample(loaded, WriteModule(updated));

		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() =>
			Tool(target).FindPatches("sample.dll", cancellationToken: Token));

		Assert.Equal(ToolErrorKind.InvalidState, exception.Error.Kind);
		Assert.Contains("not the build that was loaded", exception.Error.Message, StringComparison.Ordinal);
		Assert.Equal(1, target.Calls("ReadBytesDetailed"));
	}

	[Theory]
	[MemberData(nameof(RefusedModulePaths))]
	public void FindPatches_RefusedModulePath_NeverReadsMemory(string path)
	{
		ModuleSymbolTarget target = ModuleToolTests.LoadedSample(path: path);

		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() =>
			Tool(target).FindPatches("sample.dll", cancellationToken: Token));

		Assert.Equal((ToolErrorKind.InvalidState, ToolHostEffect.NotStarted),
			(exception.Error.Kind, exception.Error.HostEffect));
		Assert.Equal(0, target.Calls("ReadBytesDetailed"));
		Assert.Equal(1, target.Dispatcher.Calls);
		if (path.Length > 0)
		{
			Assert.DoesNotContain(path, exception.Error.Message, StringComparison.Ordinal);
		}
	}

	[Fact]
	public void FindPatches_ModuleFileThroughAJunction_IsRefusedWithoutReadingMemory()
	{
		TestPe pe = SampleModule.Build();
		string real = _scratch.CreateFolder("real");
		File.WriteAllBytes(Path.Combine(real, "sample.dll"), pe.File());
		string link = _scratch.CreateJunction("link", real);
		ModuleSymbolTarget target = ModuleToolTests.LoadedSample(pe, Path.Combine(link, "sample.dll"));

		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() =>
			Tool(target).FindPatches("sample.dll", cancellationToken: Token));

		Assert.Contains("reparse point", exception.Error.Message, StringComparison.Ordinal);
		Assert.Equal(0, target.Calls("ReadBytesDetailed"));
	}

	[Fact]
	public void FindPatches_ModuleFileInTheMcpDataDirectory_IsRefusedWithoutReadingMemory()
	{
		TestPe pe = SampleModule.Build();
		string data = _scratch.CreateFolder("data");
		string file = Path.Combine(data, "sample.dll");
		File.WriteAllBytes(file, pe.File());
		ModuleSymbolTarget target = ModuleToolTests.LoadedSample(pe, file);

		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() =>
			Tool(target).FindPatches("sample.dll", cancellationToken: Token));

		Assert.Contains("protected MCP directory", exception.Error.Message, StringComparison.Ordinal);
		Assert.Equal(0, target.Calls("ReadBytesDetailed"));
	}

	[Fact]
	public void FindPatches_MissingModuleFile_IsNotFoundWithoutReadingMemory()
	{
		ModuleSymbolTarget target =
			ModuleToolTests.LoadedSample(path: Path.Combine(_scratch.CreateFolder("gone"), "sample.dll"));

		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() =>
			Tool(target).FindPatches("sample.dll", cancellationToken: Token));

		Assert.Equal(ToolErrorKind.NotFound, exception.Error.Kind);
		Assert.Equal(0, target.Calls("ReadBytesDetailed"));
	}

	[Theory]
	[InlineData(0, ToolErrorKind.InvalidArgument)]
	[InlineData(1025, ToolErrorKind.LimitExceeded)]
	public void FindPatches_RefusedLimit_NeverDispatches(int limit, ToolErrorKind kind)
	{
		ModuleSymbolTarget target = ModuleToolTests.LoadedSample();

		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() =>
			Tool(target).FindPatches("sample.dll", limit: limit, cancellationToken: Token));

		Assert.Equal(kind, exception.Error.Kind);
		Assert.Equal(0, target.Dispatcher.Calls);
	}

	private ModulePatchTools Tool(ModuleSymbolTarget target)
	{
		return new ModulePatchTools(target.Dispatch, Paths());
	}

	private McpFilePaths Paths()
	{
		return new McpFilePaths(new McpFileOptions(), Path.Combine(_scratch.Root, "registry"),
			Path.Combine(_scratch.Root, "data"));
	}

	private string WriteModule(TestPe pe)
	{
		string folder = _scratch.CreateFolder("game");
		string file = Path.Combine(folder, "sample.dll");
		File.WriteAllBytes(file, pe.File());
		return file;
	}
}
