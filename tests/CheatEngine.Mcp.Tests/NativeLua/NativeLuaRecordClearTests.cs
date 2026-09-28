using CheatEngine.Mcp.Core.Contract;
using CheatEngine.Mcp.Core.Features;
using CheatEngine.Mcp.Tests.Support;
using CheatEngine.Mcp.Tools.Record;
using CheatEngine.Mcp.Tools.Symbol;

namespace CheatEngine.Mcp.Tests.NativeLua;

/// <summary>Record clearing, record offsets and system symbols against real Lua with only the CE globals stubbed.</summary>
public sealed partial class NativeLuaToolRuntimeTests
{
	/// <summary>
	///     One address-list record whose offset storage behaves like Cheat Engine's: 0-based indexes, a count that
	///     truncates or grows the list, and <c>Type</c> as a plain field.
	/// </summary>
	private const string RecordOffsetStubs = """
	                                         vtAutoAssembler = 11
	                                         record = {ID = 7, Type = 2, count = 3, stored = {[0] = 1, [1] = 2, [2] = 3}}
	                                         record.setOffsetCount = function(count)
	                                         	for index = count, record.count - 1 do record.stored[index] = nil end
	                                         	for index = record.count, count - 1 do record.stored[index] = 0 end
	                                         	record.count = count
	                                         end
	                                         record.getOffsetCount = function() return record.count end
	                                         record.setOffset = function(index, value)
	                                         	if index < record.count then record.stored[index] = value end
	                                         end
	                                         record.getOffset = function(index) return record.stored[index] end
	                                         addressList = {}
	                                         addressList.getMemoryRecordByID = function(id) if id == 7 then return record end return nil end
	                                         getAddressList = function() return addressList end
	                                         """;

	[Fact]
	public void RecordSetOffsets_DereferenceOrder_StoresTheFirstOffsetAtTheHighestIndex()
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs(RecordOffsetStubs);

		RecordLuaChanged changed = SetRecordOffsets(7L, [0x10, 0x18, 0, -8]);

		Assert.Equal(new RecordLuaChanged(7), changed);
		Assert.Equal(4L, ModuleSymbolRead("record.count"));
		// [[[[base]+10]+18]+0]-8: Cheat Engine applies Offset[3] first and Offset[0] last.
		object?[] stored =
			[.. Enumerable.Range(0, 4).Select(static index => ModuleSymbolRead($"record.stored[{index}]"))];
		Assert.Equal([-8L, 0L, 0x18L, 0x10L], stored);
		LuaFixedScriptAssert.NeverLoadsCode(RecordLuaScripts.SetOffsets);
	}

	[Fact]
	public void RecordSetOffsets_EmptyList_RemovesThePointerChain()
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs(RecordOffsetStubs);

		SetRecordOffsets(7L, []);

		Assert.Equal((0L, null), (ModuleSymbolRead("record.count"), ModuleSymbolRead("record.stored[0]")));
	}

	[Fact]
	public void RecordSetOffsets_AutoAssemblerRecord_RefusesWithoutChangingOffsets()
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs(RecordOffsetStubs + "\nrecord.Type = vtAutoAssembler");

		CheatEngineToolException exception =
			Assert.Throws<CheatEngineToolException>(() => SetRecordOffsets(7L, [0x10]));

		Assert.Equal((ToolErrorKind.InvalidState, ToolHostEffect.NotStarted),
			(exception.Error.Kind, exception.Error.HostEffect));
		Assert.Equal((3L, 1L), (ModuleSymbolRead("record.count"), ModuleSymbolRead("record.stored[0]")));
	}

	[Fact]
	public void RecordSetOffsets_MissingRecord_ReportsNotFound()
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs(RecordOffsetStubs);

		CheatEngineToolException exception =
			Assert.Throws<CheatEngineToolException>(() => SetRecordOffsets(8L, [0x10]));

		Assert.Equal((ToolErrorKind.NotFound, ToolHostEffect.NotStarted),
			(exception.Error.Kind, exception.Error.HostEffect));
	}

	[Fact]
	public void RecordSetOffsets_HostKeepsADifferentOffset_ReportsTheStartedRefusal()
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs(RecordOffsetStubs + """

		                                 record.setOffset = function(index, value)
		                                 	if index > 0 then record.stored[index] = value end
		                                 end
		                                 """);

		CheatEngineToolException exception =
			Assert.Throws<CheatEngineToolException>(() => SetRecordOffsets(7L, [0x10, 0x20]));

		Assert.Equal((ToolErrorKind.HostRefused, ToolHostEffect.Started),
			(exception.Error.Kind, exception.Error.HostEffect));
	}

	private static RecordLuaChanged SetRecordOffsets(long id, long[] offsets)
	{
		return CreateNativeDispatch(new McpFeatureOptions()).RunLua(CheatEngineToolNames.RecordUpdate,
			RecordLuaScripts.SetOffsets, RecordJsonContext.Default.RecordLuaChanged, Token, id, offsets);
	}

	[Fact]
	public void RecordClear_NestedStubbedAddressList_CountsEveryChildBeforeClearing()
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs("""
		             vtAutoAssembler = 17
		             clearCalls = 0
		             childA = {ID = 3, Type = 0, Active = false, Count = 0, Child = {}}
		             childB = {ID = 4, Type = 0, Active = false, Count = 0, Child = {}}
		             childC = {ID = 5, Type = 0, Active = false, Count = 0, Child = {}}
		             topA = {ID = 1, Type = 0, Active = false, Count = 2, Child = {[0] = childA, [1] = childB}}
		             topB = {ID = 2, Type = 0, Active = false, Count = 1, Child = {[0] = childC}}
		             addressList = {Count = 2}
		             addressList.getMemoryRecord = function(index) return index == 0 and topA or topB end
		             addressList.clear = function() clearCalls = clearCalls + 1; addressList.Count = 0 end
		             getAddressList = function() return addressList end
		             """);

		RecordClearResult result = new RecordClearTools(CreateNativeDispatch(new McpFeatureOptions())).Clear(Token);

		Assert.Equal(new RecordClearResult(5), result);
		Assert.Equal(1L, ReadGlobal("clearCalls"));
		Assert.Equal(0L, ModuleSymbolRead("addressList.Count"));
	}

	[Fact]
	public void RecordClear_TreeIndexedAddressList_CountsEachRecordOnce()
	{
		using RuntimeScope scope = CreateScope();
		// Like Cheat Engine: Count and getMemoryRecord index the whole tree, nested records included.
		InstallStubs("""
		             vtAutoAssembler = 11
		             clearCalls = 0
		             childA = {ID = 3, Type = 0, Active = false, Count = 0, Child = {}}
		             childB = {ID = 4, Type = 0, Active = false, Count = 0, Child = {}}
		             childC = {ID = 5, Type = vtAutoAssembler, Active = true, Count = 0, Child = {}}
		             topA = {ID = 1, Type = 0, Active = false, Count = 2, Child = {[0] = childA, [1] = childB}}
		             topB = {ID = 2, Type = 0, Active = false, Count = 1, Child = {[0] = childC}}
		             tree = {[0] = topA, [1] = childA, [2] = childB, [3] = topB, [4] = childC}
		             addressList = {Count = 5}
		             addressList.getMemoryRecord = function(index) return tree[index] end
		             addressList.clear = function() clearCalls = clearCalls + 1; addressList.Count = 0 end
		             getAddressList = function() return addressList end
		             """);

		// The nested active script is found before anything changes, and the list is then cleared once.
		CheatEngineToolException gated = Assert.Throws<CheatEngineToolException>(() =>
			new RecordClearTools(CreateNativeDispatch(new McpFeatureOptions { EnableAutoAssembler = false }))
				.Clear(Token));
		RecordClearResult result = new RecordClearTools(CreateNativeDispatch(new McpFeatureOptions())).Clear(Token);

		Assert.Equal(new RecordClearResult(5), result);
		Assert.Equal(ToolErrorKind.CapabilityDisabled, gated.Error.Kind);
		Assert.Equal(1L, ReadGlobal("clearCalls"));
		LuaFixedScriptAssert.NeverLoadsCode(RecordClearTools.ClearScript);
	}

	[Fact]
	public void RecordClear_ActiveAutoAssemblerWithoutGate_LeavesTheAddressListUntouched()
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs("""
		             vtAutoAssembler = 17
		             clearCalls = 0
		             active = {ID = 1, Type = vtAutoAssembler, Active = true, Count = 0, Child = {}}
		             addressList = {Count = 1}
		             addressList.getMemoryRecord = function(_) return active end
		             addressList.clear = function() clearCalls = clearCalls + 1; addressList.Count = 0 end
		             getAddressList = function() return addressList end
		             """);

		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() =>
			new RecordClearTools(CreateNativeDispatch(new McpFeatureOptions { EnableAutoAssembler = false }))
				.Clear(Token));

		Assert.Equal((ToolErrorKind.CapabilityDisabled, ToolHostEffect.NotStarted),
			(exception.Error.Kind, exception.Error.HostEffect));
		Assert.Equal((1L, 0L), (ModuleSymbolRead("addressList.Count"), ReadGlobal("clearCalls")));
	}

	[Fact]
	public void SymbolEnableSources_WindowsStub_ReportsExternalAccess()
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs("""
		             windowsCalls = 0
		             kernelCalls = 0
		             enableWindowsSymbols = function() windowsCalls = windowsCalls + 1 end
		             enableKernelSymbols = function() kernelCalls = kernelCalls + 1 end
		             """);

		SymbolSourcesEnabled result = ModuleSymbolTools(CreateNativeDispatch(new McpFeatureOptions()))
			.EnableSources(true, false, Token);

		Assert.Equal(new SymbolSourcesEnabled(true, false, true), result);
		Assert.Equal((1L, 0L), (ReadGlobal("windowsCalls"), ReadGlobal("kernelCalls")));
	}

	[Fact]
	public void SymbolEnableSources_KernelFailureAfterWindows_ReportsPartialEffect()
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs("""
		             windowsCalls = 0
		             kernelCalls = 0
		             enableWindowsSymbols = function() windowsCalls = windowsCalls + 1 end
		             enableKernelSymbols = function() kernelCalls = kernelCalls + 1; error('kernel driver refused') end
		             """);

		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() =>
			ModuleSymbolTools(CreateNativeDispatch(new McpFeatureOptions { EnableKernelAccess = true }))
				.EnableSources(true, true, Token));

		Assert.Equal((ToolErrorKind.PartialEffect, ToolHostEffect.Started),
			(exception.Error.Kind, exception.Error.HostEffect));
		Assert.Equal((1L, 1L), (ReadGlobal("windowsCalls"), ReadGlobal("kernelCalls")));
	}
}
