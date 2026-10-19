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

	private const string RecordClearStubs = """
	                                        vtAutoAssembler = 11
	                                        destroyCalls = 0
	                                        childA = {ID = 3, Type = 0, Active = false, Count = 0, Child = {}}
	                                        childB = {ID = 4, Type = 0, Active = false, Count = 0, Child = {}}
	                                        childC = {ID = 5, Type = 0, Active = false, Count = 0, Child = {}}
	                                        topA = {ID = 1, Type = 0, Active = false, Count = 2, Child = {[0] = childA, [1] = childB}}
	                                        topB = {ID = 2, Type = 0, Active = false, Count = 1, Child = {[0] = childC}}
	                                        records = {topA, childA, childB, topB, childC}
	                                        byId = {[1] = topA, [2] = topB, [3] = childA, [4] = childB, [5] = childC}
	                                        addressList = {}
	                                        refreshIndex = function()
	                                            indexed = {}
	                                            for _, record in ipairs(records) do
	                                                if byId[record.ID] ~= nil and (not rootsOnly or record == topA or record == topB) then
	                                                    indexed[#indexed + 1] = record
	                                                end
	                                            end
	                                            addressList.Count = #indexed
	                                        end
	                                        local function eraseTree(record)
	                                            for child = 0, record.Count - 1 do eraseTree(record.Child[child]) end
	                                            byId[record.ID] = nil
	                                        end
	                                        for _, record in ipairs(records) do
	                                            record.destroy = function()
	                                                if byId[record.ID] == nil then error('record already destroyed') end
	                                                destroyCalls = destroyCalls + 1
	                                                eraseTree(record)
	                                                refreshIndex()
	                                            end
	                                        end
	                                        addressList.getMemoryRecord = function(index) return indexed[index + 1] end
	                                        addressList.getMemoryRecordByID = function(id) return byId[id] end
	                                        getAddressList = function() return addressList end
	                                        refreshIndex()
	                                        """;

	[Fact]
	public void RecordClear_NestedStubbedAddressList_CountsEveryChildBeforeClearing()
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs(RecordClearStubs + "\nrootsOnly = true; refreshIndex()");

		RecordClearResult result = new RecordClearTools(CreateNativeDispatch(new McpFeatureOptions())).Clear(Token);

		Assert.Equal(new RecordClearResult(5), result);
		Assert.Equal(5L, ReadGlobal("destroyCalls"));
		Assert.Equal(0L, ModuleSymbolRead("addressList.Count"));
	}

	[Fact]
	public void RecordClear_TreeIndexedAddressList_CountsEachRecordOnce()
	{
		using RuntimeScope scope = CreateScope();
		// Like Cheat Engine: Count and getMemoryRecord index the whole tree, nested records included.
		InstallStubs(RecordClearStubs + "\nchildC.Type = vtAutoAssembler; childC.Active = true");

		// The nested active script is found before anything changes; enabling the gate permits deletion.
		CheatEngineToolException gated = Assert.Throws<CheatEngineToolException>(() =>
			new RecordClearTools(CreateNativeDispatch(new McpFeatureOptions { EnableAutoAssembler = false }))
				.Clear(Token));
		Assert.Equal((5L, 0L), (ModuleSymbolRead("addressList.Count"), ReadGlobal("destroyCalls")));
		RecordClearResult result = new RecordClearTools(CreateNativeDispatch(new McpFeatureOptions())).Clear(Token);

		Assert.Equal(new RecordClearResult(5), result);
		Assert.Equal(ToolErrorKind.CapabilityDisabled, gated.Error.Kind);
		Assert.Equal(5L, ReadGlobal("destroyCalls"));
		Assert.Equal(0L, ModuleSymbolRead("addressList.Count"));
		LuaFixedScriptAssert.NeverLoadsCode(RecordClearTools.ClearScript);
	}

	[Fact]
	public void RecordClear_DestroyCallbackRemovesOtherRecords_DoesNotUseDestroyedObjects()
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs(RecordClearStubs + """

		                                  local destroyChild = childC.destroy
		                                  childC.destroy = function() destroyChild(); topA.destroy() end
		                                  """);

		RecordClearResult result = new RecordClearTools(CreateNativeDispatch(new McpFeatureOptions())).Clear(Token);

		Assert.Equal(new RecordClearResult(5), result);
		Assert.Equal((0L, 3L), (ModuleSymbolRead("addressList.Count"), ReadGlobal("destroyCalls")));
	}

	[Fact]
	public void RecordClear_EmptyAddressListWithoutClearMethod_ReturnsZero()
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs("getAddressList = function() return {Count = 0} end");

		RecordClearResult result = new RecordClearTools(CreateNativeDispatch(new McpFeatureOptions())).Clear(Token);

		Assert.Equal(new RecordClearResult(0), result);
	}

	[Fact]
	public void RecordClear_ActiveAutoAssemblerWithoutGate_LeavesTheAddressListUntouched()
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs("""
		             vtAutoAssembler = 17
		             destroyCalls = 0
		             active = {ID = 1, Type = vtAutoAssembler, Active = true, Count = 0, Child = {}}
		             addressList = {Count = 1}
		             addressList.getMemoryRecord = function(_) return active end
		             addressList.getMemoryRecordByID = function(id) return active end
		             active.destroy = function() destroyCalls = destroyCalls + 1; addressList.Count = 0 end
		             getAddressList = function() return addressList end
		             """);

		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() =>
			new RecordClearTools(CreateNativeDispatch(new McpFeatureOptions { EnableAutoAssembler = false }))
				.Clear(Token));

		Assert.Equal((ToolErrorKind.CapabilityDisabled, ToolHostEffect.NotStarted),
			(exception.Error.Kind, exception.Error.HostEffect));
		Assert.Equal("Set Mcp:EnableAutoAssembler to true in appsettings.json, then disable and re-enable the plugin.",
			exception.Error.Hint);
		Assert.Equal((1L, 0L), (ModuleSymbolRead("addressList.Count"), ReadGlobal("destroyCalls")));
	}

	[Theory]
	[InlineData("error('address list busy', 0)", "address list busy")]
	[InlineData("error({}, 0)", "unspecified host error")]
	[InlineData("error(string.rep('x', 1024), 0)", null)]
	public void RecordClear_HostThrows_ReportsBoundedDetailAndUnknownEffect(string failure, string? expectedDetail)
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs("""
		             vtAutoAssembler = 11
		             destroyCalls = 0
		             record = {ID = 1, Type = 0, Active = false, Count = 0, Child = {}}
		             addressList = {Count = 1}
		             addressList.getMemoryRecord = function(_) return record end
		             addressList.getMemoryRecordByID = function(_) return record end
		             getAddressList = function() return addressList end
		             """ + "\nrecord.destroy = function() destroyCalls = destroyCalls + 1; " + failure + " end");

		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() =>
			new RecordClearTools(CreateNativeDispatch(new McpFeatureOptions())).Clear(Token));

		Assert.Equal((ToolErrorKind.HostRefused, ToolHostEffect.Unknown),
			(exception.Error.Kind, exception.Error.HostEffect));
		Assert.Equal("Cheat Engine could not clear the address list: " + (expectedDetail ?? new string('x', 512)) +
			"; inspect it before retrying.", exception.Error.Message);
		Assert.Equal((1L, 1L), (ModuleSymbolRead("addressList.Count"), ReadGlobal("destroyCalls")));
	}

	[Fact]
	public void RecordClear_DestroyCallbackAddsARecord_LeavesItAndReportsPartialEffect()
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs(RecordClearStubs + """

		                                  local destroyChild = childC.destroy
		                                  childC.destroy = function()
		                                      destroyChild()
		                                      local record = {ID = 6, Type = 0, Count = 0, Child = {}}
		                                      byId[6] = record
		                                      records[#records + 1] = record
		                                      refreshIndex()
		                                  end
		                                  """);

		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() =>
			new RecordClearTools(CreateNativeDispatch(new McpFeatureOptions())).Clear(Token));

		Assert.Equal((ToolErrorKind.PartialEffect, ToolHostEffect.Started),
			(exception.Error.Kind, exception.Error.HostEffect));
		Assert.Equal((1L, 5L), (ModuleSymbolRead("addressList.Count"), ReadGlobal("destroyCalls")));
		Assert.Equal(6L, ModuleSymbolRead("addressList.getMemoryRecord(0).ID"));
	}

	[Fact]
	public void RecordClear_TooManyRecords_RefusesBeforeEnumeratingOrDeleting()
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs("getAddressList = function() return {Count = 100001} end");

		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() =>
			new RecordClearTools(CreateNativeDispatch(new McpFeatureOptions())).Clear(Token));

		Assert.Equal((ToolErrorKind.LimitExceeded, ToolHostEffect.NotStarted),
			(exception.Error.Kind, exception.Error.HostEffect));
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
