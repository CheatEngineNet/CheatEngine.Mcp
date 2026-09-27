using CheatEngine.Mcp.Core.Contract;
using CheatEngine.Mcp.Core.Values;
using CheatEngine.Mcp.Tools.Modules;
using CheatEngine.SDK.Engine.Inspection;
using CheatEngine.SDK.Engine.Values;

namespace CheatEngine.Mcp.Tests.Tools.Modules;

/// <summary><c>module_list</c>, <c>module_get</c> and <c>module_list_exports</c> over a simulated target.</summary>
public sealed class ModuleToolTests
{
	private static CancellationToken Token => TestContext.Current.CancellationToken;

	[Fact]
	public void List_AttachedProcess_PagesTheFilteredModulesInBothFormats()
	{
		ModuleSymbolTarget target = new();
		target.AddModule("game.exe", 0x140000000, 0x5000);
		target.AddModule("mono-2.0-bdwgc.dll", 0x7FFA00000000, 0x20000, "C:\\Games\\Game\\mono.dll");
		target.AddModule("MonoBleedingEdge.dll", 0x7FFB00000000, 0x1000);
		ModuleTools tools = new(target.Dispatch);

		ModuleList concise = tools.List(nameContains: "MONO", limit: 1, cancellationToken: Token);
		ModuleList detailed = tools.List(nameContains: "mono", offset: 1, format: ResultFormat.Detailed, cancellationToken: Token);

		Assert.Equal((ModuleSymbolTarget.ProcessId, 2, 1), (concise.ProcessId, concise.Total, concise.NextOffset));
		ModuleEntry first = Assert.Single(concise.Modules);
		Assert.Equal(new ModuleEntry("mono-2.0-bdwgc.dll", "7FFA00000000", 0x20000, null, null), first);
		Assert.Null(detailed.NextOffset);
		Assert.Equal(new ModuleEntry("MonoBleedingEdge.dll", "7FFB00000000", 0x1000, true, "C:\\Games\\Game\\game.exe"),
			Assert.Single(detailed.Modules));
		Assert.Equal(2, target.Dispatcher.Calls);
	}

	[Fact]
	public void List_OtherProcess_UsesItsIdWithoutReadingTheAttachedProcess()
	{
		ModuleSymbolTarget target = new()
		{
			Attached = false
		};
		target.AddModule("other.exe", 0x400000, 0x1000);

		ModuleList list = new ModuleTools(target.Dispatch).List(processId: 77, cancellationToken: Token);

		Assert.Equal(77, list.ProcessId);
		Assert.Equal(0, target.Calls("GetCurrentProcess"));
	}

	[Fact]
	public void List_MoreModulesThanTheFirstCapacity_RetriesWithTheLargerOne()
	{
		ModuleSymbolTarget target = new();
		for (int index = 0; index < 5000; index++)
		{
			target.AddModule($"m{index}.dll", 0x10000000UL + ((ulong) index * 0x10000), 0x1000);
		}

		ModuleList list = new ModuleTools(target.Dispatch).List(limit: 1000, cancellationToken: Token);

		Assert.Equal(5000, list.Total);
		Assert.Equal(1000, list.NextOffset);
		Assert.Equal(2, target.Calls("TryGetModules"));
	}

	[Fact]
	public void List_NothingAttached_IsNotAttached()
	{
		ModuleSymbolTarget target = new()
		{
			Attached = false
		};

		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() =>
			new ModuleTools(target.Dispatch).List(cancellationToken: Token));

		Assert.Equal(ToolErrorKind.NotAttached, exception.Error.Kind);
	}

	public static TheoryData<int?, string?, int, int, ToolErrorKind> RefusedListArguments => new()
	{
		{ 0, null, 0, 200, ToolErrorKind.InvalidArgument },
		{ null, new string('a', 257), 0, 200, ToolErrorKind.InvalidArgument },
		{ null, null, -1, 200, ToolErrorKind.InvalidArgument },
		{ null, null, 0, 0, ToolErrorKind.InvalidArgument },
		{ null, null, 0, 1001, ToolErrorKind.LimitExceeded }
	};

	[Theory]
	[MemberData(nameof(RefusedListArguments))]
	public void List_RefusedArgument_NeverDispatches(int? processId, string? nameContains, int offset, int limit,
		ToolErrorKind kind)
	{
		ModuleSymbolTarget target = new();

		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() =>
			new ModuleTools(target.Dispatch).List(processId, nameContains, offset, limit, cancellationToken: Token));

		Assert.Equal((kind, ToolHostEffect.NotStarted), (exception.Error.Kind, exception.Error.HostEffect));
		Assert.Equal(0, target.Dispatcher.Calls);
		Assert.Equal(0, target.TotalClientCalls);
	}

	[Fact]
	public void Get_Module_ReportsSectionsWithTheirFlagsThePeFieldsAndThePdb()
	{
		ModuleSymbolTarget target = LoadedSample();

		ModuleDetails details = new ModuleTools(target.Dispatch).Get("SAMPLE.dll", cancellationToken: Token);

		Assert.Equal(("sample.dll", "7FFA00000000", 0x6000L, true), (details.Name, details.Base, details.Size,
			details.Is64Bit));
		Assert.Equal(new ModuleSection(".text", "7FFA00004000", 0x2000, "A00", true, false), details.Sections[1]);
		Assert.Equal(new ModuleSection(".data", "7FFA00002000", 0x200, "600", false, true), details.Sections[0]);
		PeImageInfo pe = Assert.IsType<PeImageInfo>(details.Pe);
		Assert.Equal(PeMachine.X64, pe.Machine);
		Assert.Equal("5F3A1B2C", pe.TimeDateStamp);
		Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(0x5F3A1B2C), pe.TimeDateStampUtc);
		Assert.Equal("7FFA00004010", pe.EntryPoint);
		Assert.Equal("7FFA00000000", pe.HeaderImageBase);
		Assert.Equal(PeSubsystem.WindowsCui, pe.Subsystem);
		Assert.True(pe.IsDll);
		Assert.False(pe.Managed);
		Assert.Equal(new PdbReference("C:\\build\\sample.pdb", "3F2504E0-4F89-11D3-9A0C-0305E82C3301", 3), pe.Pdb);
		Assert.Equal(1, target.Dispatcher.Calls);
	}

	[Fact]
	public void Get_InnerAddress_FindsTheContainingModule()
	{
		ModuleSymbolTarget target = LoadedSample();
		target.Addresses["sample.dll+4010"] = SampleModule.LoadedBase + 0x4010;

		Assert.Equal("sample.dll", new ModuleTools(target.Dispatch).Get("sample.dll+4010", cancellationToken: Token).Name);
	}

	[Fact]
	public void Get_UnknownModule_IsNotFound()
	{
		ModuleSymbolTarget target = LoadedSample();

		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() =>
			new ModuleTools(target.Dispatch).Get("missing.dll", cancellationToken: Token));

		Assert.Equal(ToolErrorKind.NotFound, exception.Error.Kind);
		Assert.Contains("module_list", exception.Error.Hint, StringComparison.Ordinal);
		Assert.Equal(0, target.Calls("ReadBytesDetailed"));
	}

	[Fact]
	public void Get_UnreadableHeader_OmitsThePeFields()
	{
		ModuleSymbolTarget target = LoadedSample();
		target.UnreadablePages.Add(SampleModule.LoadedBase);

		ModuleDetails details = new ModuleTools(target.Dispatch).Get("sample.dll", cancellationToken: Token);

		Assert.Null(details.Pe);
		Assert.Equal(2, details.Sections.Length);
		Assert.Null(details.Sections[0].Executable);
	}

	[Theory]
	[InlineData("")]
	[InlineData("   ")]
	[InlineData("bad\nname")]
	public void Get_RefusedModuleArgument_NeverDispatches(string module)
	{
		ModuleSymbolTarget target = new();

		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() =>
			new ModuleTools(target.Dispatch).Get(module, cancellationToken: Token));

		Assert.Equal(ToolErrorKind.InvalidArgument, exception.Error.Kind);
		Assert.Equal(0, target.Dispatcher.Calls);
	}

	[Fact]
	public void ListExports_Module_PagesExportsByOrdinalWithForwarders()
	{
		ModuleSymbolTarget target = LoadedSample();
		ModuleExportTools tools = new(target.Dispatch);

		ExportList all = tools.ListExports("sample.dll", cancellationToken: Token);
		ExportList filtered = tools.ListExports("sample.dll", "a", 1, 1, cancellationToken: Token);

		Assert.Equal(("sample.dll", 4, (int?) null), (all.Module, all.Total, all.NextOffset));
		ModuleExport[] expected =
		[
			new(1, "Alpha", "7FFA00004010"),
			new(2, null, "7FFA00004020"),
			new(3, "Beta", "7FFA00004030"),
			new(4, "Forwarded", null, "NTDLL.RtlAllocateHeap")
		];
		Assert.Equal(expected, all.Exports);
		// Alpha, Beta and Forwarded contain "a"; the page after the first holds Beta.
		Assert.Equal((3, 2), (filtered.Total, filtered.NextOffset));
		Assert.Equal("Beta", Assert.Single(filtered.Exports).Name);
	}

	[Fact]
	public void ListExports_MalformedDirectory_IsInvalidStateAfterTheReads()
	{
		ModuleSymbolTarget target = LoadedSample();
		BitConverter.TryWriteBytes(target.Memory.AsSpan((int) SampleModule.ExportRva + 20), 70000);

		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() =>
			new ModuleExportTools(target.Dispatch).ListExports("sample.dll", cancellationToken: Token));

		Assert.Equal((ToolErrorKind.InvalidState, ToolHostEffect.Completed),
			(exception.Error.Kind, exception.Error.HostEffect));
		Assert.Contains("malformed", exception.Error.Message, StringComparison.Ordinal);
	}

	[Fact]
	public void ListExports_UnreadableExportData_IsMemoryReadFailed()
	{
		ModuleSymbolTarget target = LoadedSample();
		target.UnreadablePages.Add(SampleModule.LoadedBase + SampleModule.ExportRva);

		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() =>
			new ModuleExportTools(target.Dispatch).ListExports("sample.dll", cancellationToken: Token));

		Assert.Equal(ToolErrorKind.MemoryReadFailed, exception.Error.Kind);
	}

	[Theory]
	[InlineData(-1, 10, ToolErrorKind.InvalidArgument)]
	[InlineData(0, 1001, ToolErrorKind.LimitExceeded)]
	public void ListExports_RefusedPaging_NeverDispatches(int offset, int limit, ToolErrorKind kind)
	{
		ModuleSymbolTarget target = LoadedSample();

		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() =>
			new ModuleExportTools(target.Dispatch).ListExports("sample.dll", null, offset, limit, cancellationToken: Token));

		Assert.Equal(kind, exception.Error.Kind);
		Assert.Equal(0, target.Dispatcher.Calls);
	}

	/// <summary>The sample DLL mapped at <see cref="SampleModule.LoadedBase" /> in an attached target.</summary>
	internal static ModuleSymbolTarget LoadedSample(TestPe? pe = null, string path = "C:\\Games\\Game\\sample.dll")
	{
		pe ??= SampleModule.Build();
		ModuleSymbolTarget target = new()
		{
			MemoryBase = SampleModule.LoadedBase,
			Memory = pe.Map(SampleModule.LoadedBase)
		};
		ModuleInfo module = target.AddModule("sample.dll", SampleModule.LoadedBase, pe.SizeOfImage, path);
		target.Sections[module.Name] =
		[
			new ModuleSectionInfo(".data", new MemorySize(0x200), new Address(SampleModule.LoadedBase + SampleModule.DataRva),
				new ModuleFileOffset(0x600)),
			new ModuleSectionInfo(".text", new MemorySize(0x2000),
				new Address(SampleModule.LoadedBase + SampleModule.TextRva), new ModuleFileOffset(0xA00))
		];
		return target;
	}
}
