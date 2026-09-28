using System.Buffers.Binary;

using CheatEngine.Client.Results;
using CheatEngine.Mcp.Core.Contract;
using CheatEngine.Mcp.Core.Values;
using CheatEngine.Mcp.Tools.Modules;

namespace CheatEngine.Mcp.Tests.Tools.Modules;

/// <summary><c>module_list_imports</c> over a simulated target: slots, values, targets, filters and refusals.</summary>
public sealed class ModuleImportToolTests
{
	private const ulong Stub = SampleModule.LoadedBase + SampleModule.TextRva + 0x40;

	private static CancellationToken Token => TestContext.Current.CancellationToken;

	public static TheoryData<string, string?, string?, int, int, ToolErrorKind> RefusedArguments => new()
	{
		{ " ", null, null, 0, 200, ToolErrorKind.InvalidArgument },
		{ "sample.dll", new string('k', 257), null, 0, 200, ToolErrorKind.InvalidArgument },
		{ "sample.dll", null, new string('n', 257), 0, 200, ToolErrorKind.InvalidArgument },
		{ "sample.dll", null, null, -1, 200, ToolErrorKind.InvalidArgument },
		{ "sample.dll", null, null, 0, 0, ToolErrorKind.InvalidArgument },
		{ "sample.dll", null, null, 0, 1001, ToolErrorKind.LimitExceeded }
	};

	[Fact]
	public void ListImports_Module_ListsEverySlotWithItsValueAndTarget()
	{
		(ModuleSymbolTarget target, TestImportTable imports, TestImportTable delayed) = LoadedWithImports();
		ulong slots = SampleModule.LoadedBase + imports.SlotRvas[0];
		ulong bound = SampleModule.LoadedBase + imports.SlotRvas[1];

		ImportList list = new ModuleImportTools(target.Dispatch).ListImports("sample.dll", cancellationToken: Token);

		Assert.Equal(("sample.dll", 6, (int?) null), (list.Module, list.Total, list.NextOffset));
		ModuleImport[] expected =
		[
			new("KERNEL32.dll", Hex(slots), false, "7FFB00001000", "Sleep", Hint: 0x5A1,
				TargetModule: "KERNELBASE.dll", TargetSymbol: "KERNELBASE.Sleep"),
			new("KERNEL32.dll", Hex(slots + 8), false, "7FFB00002000", "GetTickCount", Hint: 0x2B3,
				TargetModule: "KERNELBASE.dll"),
			new("KERNEL32.dll", Hex(slots + 16), false, "7FFB00003000", Ordinal: 17, TargetModule: "KERNELBASE.dll"),
			new("bound.dll", Hex(bound), false, "7FFC00001000"),
			new("bound.dll", Hex(bound + 8), false, "7FFC00001010"),
			new("USER32.dll", Hex(SampleModule.LoadedBase + delayed.SlotRvas[0]), true, Hex(Stub), "MessageBoxW",
				Hint: 0x28A, TargetModule: "sample.dll", TargetSymbol: "sample.dll+4040")
		];
		Assert.Equal(expected, list.Imports);
		Assert.Equal(1, target.Dispatcher.Calls);
	}

	[Fact]
	public void ListImports_Filters_MatchDllAndNameCaseInsensitivelyAndSkipUnnamedSlots()
	{
		(ModuleSymbolTarget target, _, _) = LoadedWithImports();
		ModuleImportTools tools = new(target.Dispatch);

		ImportList byDll = tools.ListImports("sample.dll", "kernel32", cancellationToken: Token);
		ImportList byName = tools.ListImports("sample.dll", nameContains: "TICK", cancellationToken: Token);
		ImportList both = tools.ListImports("sample.dll", "user", "box", cancellationToken: Token);
		ImportList none = tools.ListImports("sample.dll", "bound", "a", cancellationToken: Token);

		Assert.Equal(["Sleep", "GetTickCount", null], byDll.Imports.Select(static import => import.Name));
		Assert.Equal("GetTickCount", Assert.Single(byName.Imports).Name);
		Assert.Equal("MessageBoxW", Assert.Single(both.Imports).Name);
		Assert.Equal(0, none.Total);
		Assert.Empty(none.Imports);
	}

	[Fact]
	public void ListImports_Paging_NamesOnlyTheReturnedPage()
	{
		(ModuleSymbolTarget target, _, _) = LoadedWithImports();

		ImportList page = new ModuleImportTools(target.Dispatch).ListImports("sample.dll", offset: 1, limit: 2,
			cancellationToken: Token);

		Assert.Equal((6, (int?) 3), (page.Total, page.NextOffset));
		Assert.Equal(["GetTickCount", null], page.Imports.Select(static import => import.Name));
		Assert.Equal(2, target.Calls("TryResolveName"));
	}

	[Fact]
	public void ListImports_WithoutDelayLoaded_ListsOnlyTheImportDirectory()
	{
		(ModuleSymbolTarget target, _, _) = LoadedWithImports();

		ImportList list = new ModuleImportTools(target.Dispatch).ListImports("sample.dll", includeDelayLoaded: false,
			cancellationToken: Token);

		Assert.Equal(5, list.Total);
		Assert.DoesNotContain(list.Imports, static import => import.DelayLoaded);
	}

	[Fact]
	public void ListImports_Pe32Module_ReadsFourByteSlots()
	{
		(TestPe pe, TestImportTable imports, _) = SampleModule.BuildWithImports(false, false);
		ModuleSymbolTarget target = ModuleToolTests.LoadedSample(pe);

		ImportList list = new ModuleImportTools(target.Dispatch).ListImports("sample.dll", cancellationToken: Token);

		ModuleImport tick = list.Imports[1];
		Assert.Equal((Hex(SampleModule.LoadedBase + imports.SlotRvas[0] + 4), "76A02000", "GetTickCount"),
			(tick.SlotAddress, tick.Value, tick.Name));
		Assert.Equal(Hex(SampleModule.LoadedBase + imports.SlotRvas[1] + 4), list.Imports[4].SlotAddress);
	}

	[Fact]
	public void ListImports_OldDelayLoadDescriptorOfAPe32Exe_NamesItsImportsInsteadOfFailing()
	{
		// A 32-bit game linked before Visual C++ 7: its delay-load descriptor and lookup table hold addresses.
		const ulong loadedBase = 0x00400000;
		(TestPe pe, TestImportTable delayed) = SampleModule.BuildWithDelayLoads(false, TestDelayForm.Addresses);
		ModuleSymbolTarget target = new()
		{
			MemoryBase = loadedBase,
			Memory = pe.Map(loadedBase)
		};
		target.AddModule("game.exe", loadedBase, pe.SizeOfImage);

		ImportList list = new ModuleImportTools(target.Dispatch).ListImports("game.exe", cancellationToken: Token);

		ulong slot = loadedBase + delayed.SlotRvas[0];
		ulong stub = loadedBase + SampleModule.DelayStubRva;
		ModuleImport[] expected =
		[
			new("USER32.dll", Hex(slot), true, Hex(stub), "MessageBoxW", Hint: 0x28A, TargetModule: "game.exe"),
			new("USER32.dll", Hex(slot + 4), true, Hex(stub + 0x10), Ordinal: 42, TargetModule: "game.exe")
		];
		Assert.Equal(("game.exe", 2), (list.Module, list.Total));
		Assert.Equal(expected, list.Imports);
	}

	[Fact]
	public void ListImports_ModuleWithoutImports_IsAnEmptyPage()
	{
		ModuleSymbolTarget target = ModuleToolTests.LoadedSample();

		ImportList list = new ModuleImportTools(target.Dispatch).ListImports("sample.dll", cancellationToken: Token);

		Assert.Equal(("sample.dll", 0, (int?) null), (list.Module, list.Total, list.NextOffset));
		Assert.Empty(list.Imports);
	}

	[Fact]
	public void ListImports_MalformedDirectory_IsInvalidStateAfterTheReads()
	{
		(ModuleSymbolTarget target, _, _) = LoadedWithImports();
		BinaryPrimitives.WriteUInt32LittleEndian(target.Memory.AsSpan((int) SampleModule.ImportRva + 12), 0x7FFFFFF0);

		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() =>
			new ModuleImportTools(target.Dispatch).ListImports("sample.dll", cancellationToken: Token));

		Assert.Equal((ToolErrorKind.InvalidState, ToolHostEffect.Completed),
			(exception.Error.Kind, exception.Error.HostEffect));
		Assert.Contains("malformed", exception.Error.Message, StringComparison.Ordinal);
	}

	[Fact]
	public void ListImports_UnreadableImportData_IsMemoryReadFailed()
	{
		(ModuleSymbolTarget target, _, _) = LoadedWithImports();
		target.UnreadablePages.Add(SampleModule.LoadedBase + SampleModule.ImportRva);

		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() =>
			new ModuleImportTools(target.Dispatch).ListImports("sample.dll", cancellationToken: Token));

		Assert.Equal(ToolErrorKind.MemoryReadFailed, exception.Error.Kind);
	}

	[Fact]
	public void ListImports_NamesSpreadPastTheReadBudget_IsLimitExceeded()
	{
		const uint bigRva = 0x10000;
		const int pages = 140;
		TestImport[] functions = [.. Enumerable.Range(0, pages).Select(static index => new TestImport("F", 1))];
		TestImportTable imports = TestPe.ImportData(SampleModule.ImportRva, true,
			[new TestImportDll("spread.dll", functions)]);
		TestPe pe = SampleModule.Build().AddSection(".idata", SampleModule.ImportRva, imports.Data,
				TestPe.WritableData)
			.AddSection(".big", bigRva, new byte[16], TestPe.WritableData, (uint) pages * 0x10000)
			.SetDirectory(1, SampleModule.ImportRva, imports.DescriptorsSize);
		ModuleSymbolTarget target = ModuleToolTests.LoadedSample(pe);
		for (int index = 0; index < pages; index++)
		{
			// Each hint/name entry sits on its own 64 KiB page, so reading the names needs more than 8 MiB.
			uint hintName = bigRva + ((uint) index * 0x10000);
			target.Memory[hintName + 2] = (byte) 'F';
			BinaryPrimitives.WriteUInt64LittleEndian(
				target.Memory.AsSpan((int) imports.LookupRvas[0] + (index * 8)), hintName);
		}

		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() =>
			new ModuleImportTools(target.Dispatch).ListImports("sample.dll", cancellationToken: Token));

		Assert.Equal(ToolErrorKind.LimitExceeded, exception.Error.Kind);
		Assert.Equal(0, target.Calls("TryResolveName"));
	}

	[Theory]
	[InlineData(CheatEngineFailureKind.NotFound, null)]
	[InlineData(CheatEngineFailureKind.TargetChanged, ToolErrorKind.TargetChanged)]
	[InlineData(CheatEngineFailureKind.TargetNotAttached, ToolErrorKind.NotAttached)]
	public void ListImports_NamingFailure_IsMissingDataOrTheWholeRequestsFailure(CheatEngineFailureKind failure,
		ToolErrorKind? expected)
	{
		(ModuleSymbolTarget target, _, _) = LoadedWithImports();
		target.NameFailures[SampleModule.KernelBase64 + 0x1000] = failure;
		ModuleImportTools tools = new(target.Dispatch);

		if (expected is { } kind)
		{
			CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() =>
				tools.ListImports("sample.dll", limit: 1, cancellationToken: Token));
			Assert.Equal(kind, exception.Error.Kind);
		}
		else
		{
			ModuleImport sleep = Assert.Single(tools.ListImports("sample.dll", limit: 1, cancellationToken: Token)
				.Imports);
			Assert.Equal(("Sleep", "KERNELBASE.dll", null), (sleep.Name, sleep.TargetModule, sleep.TargetSymbol));
		}
	}

	[Theory]
	[MemberData(nameof(RefusedArguments))]
	public void ListImports_RefusedArguments_NeverDispatch(string module, string? dllContains, string? nameContains,
		int offset, int limit, ToolErrorKind kind)
	{
		(ModuleSymbolTarget target, _, _) = LoadedWithImports();

		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() =>
			new ModuleImportTools(target.Dispatch).ListImports(module, dllContains, nameContains, true, offset, limit,
				Token));

		Assert.Equal(kind, exception.Error.Kind);
		Assert.Equal(0, target.Dispatcher.Calls);
	}

	[Fact]
	public void ListImports_Detached_IsNotAttachedBeforeAnyRead()
	{
		(ModuleSymbolTarget target, _, _) = LoadedWithImports();
		target.Attached = false;

		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() =>
			new ModuleImportTools(target.Dispatch).ListImports("sample.dll", cancellationToken: Token));

		Assert.Equal(ToolErrorKind.NotAttached, exception.Error.Kind);
		Assert.Equal(0, target.Calls("ReadBytesDetailed"));
	}

	/// <summary>
	///     The sample DLL with its import and delay-load sections, mapped in an attached target that also holds
	///     KERNELBASE.dll, with a Cheat Engine name for Sleep, an echoed address for GetTickCount and a module-relative
	///     name for the delay-load stub.
	/// </summary>
	private static (ModuleSymbolTarget Target, TestImportTable Imports, TestImportTable Delayed) LoadedWithImports()
	{
		(TestPe pe, TestImportTable imports, TestImportTable? delayed) = SampleModule.BuildWithImports();
		ModuleSymbolTarget target = ModuleToolTests.LoadedSample(pe);
		target.AddModule("KERNELBASE.dll", SampleModule.KernelBase64, 0x10000, "C:\\Windows\\System32\\KernelBase.dll");
		target.Names[SampleModule.KernelBase64 + 0x1000] = "KERNELBASE.Sleep";
		target.Names[SampleModule.KernelBase64 + 0x2000] = "7FFB00002000";
		target.Names[Stub] = "sample.dll+4040";
		return (target, imports, delayed!);
	}

	private static string Hex(ulong value)
	{
		return HexFormat.Address(value);
	}
}
