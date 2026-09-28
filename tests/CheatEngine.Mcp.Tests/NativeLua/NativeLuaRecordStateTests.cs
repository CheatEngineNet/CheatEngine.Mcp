using System.Globalization;
using System.Reflection;

using CheatEngine.Client;
using CheatEngine.Client.Lua;
using CheatEngine.Client.Results;
using CheatEngine.Client.Tables;
using CheatEngine.Mcp.Core.Contract;
using CheatEngine.Mcp.Core.Features;
using CheatEngine.Mcp.Tests.Support;
using CheatEngine.Mcp.Tools.Record;
using CheatEngine.SDK.Engine.AddressList;
using CheatEngine.SDK.Engine.Enums;
using CheatEngine.SDK.Engine.Values;

using Microsoft.Extensions.Options;

namespace CheatEngine.Mcp.Tests.NativeLua;

/// <summary>
///     Record creation, layout, offset copies, script storage and activation against real Lua with only the CE
///     globals stubbed.
/// </summary>
public sealed partial class NativeLuaToolRuntimeTests
{
	/// <summary>
	///     Address-list records that behave like Cheat Engine's where the record bodies depend on it: offsets are kept
	///     as texts, <c>setOffset</c> stores a number as <c>inttohex</c> text and <c>Offset[]</c> evaluates a text;
	///     <c>String</c> and <c>Aob</c> are sub-objects; assigning <c>Active</c> runs an activation that calls
	///     <c>OnActivationFailure(memrec, reason, text)</c> through a protected call and repeats while it returns true.
	///     Record 2 is a 4-byte value, 4 a pointer with three offsets, 6 a string, 8 a byte array, 11 an Auto
	///     Assembler record; <c>createMemoryRecord</c> numbers new records from 20. Records have no children and
	///     their <c>Options</c> are <c>[]</c> unless <see cref="RecordTreeStubs" /> adds them.
	/// </summary>
	private const string RecordStateStubs = """
	                                        vtAutoAssembler = 11
	                                        vtString = 6
	                                        vtByteArray = 8
	                                        records = {}
	                                        nextId = 20
	                                        destroyed = 0
	                                        refuseType = false
	                                        failDestroy = false
	                                        offsetReads = 0
	                                        local function hex(value)
	                                        	if value < 0 then return '-' .. string.format('%X', -value) end
	                                        	return string.format('%X', value)
	                                        end
	                                        function activate(record, value)
	                                        	if value == rawget(record, 'state') then return end
	                                        	local outcome = record.outcome
	                                        	if not value then
	                                        		if not outcome.keepActive then record.state = false end
	                                        		return
	                                        	end
	                                        	record.attempts = record.attempts + 1
	                                        	if outcome.raise then error('activation raised') end
	                                        	if outcome.async then record.AsyncProcessing = true return end
	                                        	if outcome.reason == nil or record.attempts > (outcome.failures or math.huge) then
	                                        		record.state = true
	                                        		return
	                                        	end
	                                        	local handler = rawget(record, 'handler')
	                                        	if handler ~= nil then
	                                        		local ok, retry = pcall(handler, record, outcome.reason, outcome.text)
	                                        		if ok and retry == true then activate(record, true) end
	                                        	end
	                                        end
	                                        function makeRecord(id, type)
	                                        	local record = {ID = id, Type = type, Description = '', Address = '0', AsyncProcessing = false,
	                                        		OffsetCount = 0, texts = {}, state = false, attempts = 0, handlerSets = 0, outcome = {},
	                                        		String = {Size = 0, Unicode = false}, Aob = {Size = 0}, ShowAsHex = false, Script = '',
	                                        		Options = '[]', Count = 0, Child = {}}
	                                        	record.OffsetText = setmetatable({}, {__index = function(_, index) return record.texts[index] end})
	                                        	record.Offset = setmetatable({}, {__index = function(_, index)
	                                        		offsetReads = offsetReads + 1
	                                        		return tonumber(record.texts[index], 16)
	                                        	end})
	                                        	record.setOffsetCount = function(count)
	                                        		for index = count, record.OffsetCount - 1 do record.texts[index] = nil end
	                                        		for index = record.OffsetCount, count - 1 do record.texts[index] = '' end
	                                        		record.OffsetCount = count
	                                        	end
	                                        	record.getOffsetCount = function() return record.OffsetCount end
	                                        	record.setOffset = function(index, value)
	                                        		if index < record.OffsetCount then record.texts[index] = hex(value) end
	                                        	end
	                                        	record.getOffset = function(index)
	                                        		local text = record.texts[index]
	                                        		if string.sub(text, 1, 1) == '-' then return -tonumber(string.sub(text, 2), 16) end
	                                        		return tonumber(text, 16)
	                                        	end
	                                        	record.destroy = function()
	                                        		if failDestroy then error('destroy refused') end
	                                        		destroyed = destroyed + 1
	                                        		records[id] = nil
	                                        	end
	                                        	records[id] = record
	                                        	return setmetatable(record, {
	                                        		__index = function(self, key)
	                                        			if key == 'Active' then return rawget(self, 'state') end
	                                        			if key == 'OnActivationFailure' then return rawget(self, 'handler') end
	                                        		end,
	                                        		__newindex = function(self, key, value)
	                                        			if key == 'Active' then activate(self, value) return end
	                                        			if key == 'OnActivationFailure' then
	                                        				rawset(self, 'handler', value)
	                                        				self.handlerSets = self.handlerSets + 1
	                                        				return
	                                        			end
	                                        			rawset(self, key, value)
	                                        		end})
	                                        end
	                                        makeRecord(2, 2)
	                                        local pointer = makeRecord(4, 2)
	                                        pointer.OffsetCount = 3
	                                        pointer.texts = {[0] = '-8', [1] = 'hpOff+4', [2] = '4c8'}
	                                        makeRecord(6, vtString)
	                                        makeRecord(8, vtByteArray)
	                                        makeRecord(11, vtAutoAssembler)
	                                        addressList = {}
	                                        addressList.getMemoryRecordByID = function(id) return records[id] end
	                                        addressList.createMemoryRecord = function()
	                                        	local record = makeRecord(nextId, nil)
	                                        	record.Type = nil
	                                        	nextId = nextId + 1
	                                        	getmetatable(record).__newindex = function(self, key, value)
	                                        		if key == 'Type' and refuseType then return end
	                                        		rawset(self, key, value)
	                                        	end
	                                        	return record
	                                        end
	                                        getAddressList = function() return addressList end
	                                        """;

	/// <summary>
	///     Nested records added to <see cref="RecordStateStubs" />: group 30 sets its value recursively into string 31,
	///     which passes it on to 4-byte value 32; group 34 deactivates its Auto Assembler child 33 and group 35
	///     activates it too. <c>Options</c> is the bracketed, comma-separated text Cheat Engine returns.
	/// </summary>
	private const string RecordTreeStubs = """

	                                       local group = makeRecord(30, 14)
	                                       group.Options = '[moHideChildren, moRecursiveSetValue]'
	                                       local name = makeRecord(31, vtString)
	                                       name.Options = '[moRecursiveSetValue]'
	                                       local health = makeRecord(32, 2)
	                                       group.Count = 1
	                                       group.Child = {[0] = name}
	                                       name.Count = 1
	                                       name.Child = {[0] = health}
	                                       local script = makeRecord(33, vtAutoAssembler)
	                                       local cheats = makeRecord(34, 14)
	                                       cheats.Options = '[moDeactivateChildrenAsWell]'
	                                       cheats.Count = 1
	                                       cheats.Child = {[0] = script}
	                                       local more = makeRecord(35, 14)
	                                       more.Options = '[moActivateChildrenAsWell,moDeactivateChildrenAsWell]'
	                                       more.Count = 1
	                                       more.Child = {[0] = script}
	                                       """;

	[Fact]
	public void RecordCreate_SetsDescriptionAddressAndTypeAndNeverAValue()
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs(RecordStateStubs);

		RecordLuaChanged created = CreateRecord("Health", "game.exe+20", 2);

		Assert.Equal(new RecordLuaChanged(20), created);
		Assert.Equal(("Health", "game.exe+20", 2L), (ModuleSymbolRead("records[20].Description"),
			ModuleSymbolRead("records[20].Address"), ModuleSymbolRead("records[20].Type")));
		// Cheat Engine refuses an empty value for a number type, so the body never assigns one.
		Assert.Null(ModuleSymbolRead("rawget(records[20], 'Value')"));
		LuaFixedScriptAssert.NeverLoadsCode(RecordLuaScripts.CreateRecord);
	}

	[Fact]
	public void RecordCreate_TypeCheatEngineDidNotKeep_RemovesTheRecordBeforeRefusing()
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs(RecordStateStubs + "\nrefuseType = true");

		CheatEngineToolException removed =
			Assert.Throws<CheatEngineToolException>(() => CreateRecord("Health", "game.exe+20", 2));
		InstallStubs("failDestroy = true");
		CheatEngineToolException retained =
			Assert.Throws<CheatEngineToolException>(() => CreateRecord("Ammo", "game.exe+24", 2));

		Assert.Equal((ToolErrorKind.HostRefused, ToolHostEffect.NotStarted),
			(removed.Error.Kind, removed.Error.HostEffect));
		Assert.Equal((ToolErrorKind.PartialEffect, ToolHostEffect.Started),
			(retained.Error.Kind, retained.Error.HostEffect));
		Assert.Equal((1L, null), (ReadGlobal("destroyed"), ModuleSymbolRead("records[20]")));
	}

	[Fact]
	public void RecordSetLayout_StringAndByteArray_StoreLengthEncodingAndHexadecimalDisplay()
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs(RecordStateStubs);

		SetLayout(6L, 32, true);
		SetLayout(8L, 6, null);

		Assert.Equal((32L, true), (ModuleSymbolRead("records[6].String.Size"),
			ModuleSymbolRead("records[6].String.Unicode")));
		Assert.Equal((true, 6L), (ModuleSymbolRead("records[8].ShowAsHex"), ModuleSymbolRead("records[8].Aob.Size")));
		// A nil argument keeps the setting; a byte array always switches to hexadecimal display.
		InstallStubs("records[8].ShowAsHex = false");
		SetLayout(8L, null, null);
		SetLayout(6L, null, false);
		Assert.Equal((true, 6L), (ModuleSymbolRead("records[8].ShowAsHex"), ModuleSymbolRead("records[8].Aob.Size")));
		Assert.Equal((32L, false), (ModuleSymbolRead("records[6].String.Size"),
			ModuleSymbolRead("records[6].String.Unicode")));
		LuaFixedScriptAssert.NeverLoadsCode(RecordLuaScripts.SetLayout);
	}

	[Fact]
	public void RecordSetLayout_OptionTheTypeDoesNotHave_RefusesWithoutAChange()
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs(RecordStateStubs);

		CheatEngineToolException bytes = Assert.Throws<CheatEngineToolException>(() => SetLayout(8L, null, true));
		CheatEngineToolException number = Assert.Throws<CheatEngineToolException>(() => SetLayout(2L, 4, null));
		CheatEngineToolException missing = Assert.Throws<CheatEngineToolException>(() => SetLayout(99L, 4, null));
		RecordLuaChanged plain = SetLayout(2L, null, null);

		Assert.All([bytes, number], static exception => Assert.Equal(
			(ToolErrorKind.InvalidState, ToolHostEffect.NotStarted),
			(exception.Error.Kind, exception.Error.HostEffect)));
		Assert.Equal(ToolErrorKind.NotFound, missing.Error.Kind);
		Assert.Equal(new RecordLuaChanged(2), plain);
		Assert.False((bool) ModuleSymbolRead("records[8].ShowAsHex")!);
	}

	[Fact]
	public void RecordSetLayout_HostKeepsAnotherLength_ReportsTheStartedRefusal()
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs(RecordStateStubs + """

		                                records[6].String = setmetatable({}, {__index = function() return 0 end,
		                                	__newindex = function() end})
		                                """);

		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() => SetLayout(6L, 16, null));

		Assert.Equal((ToolErrorKind.HostRefused, ToolHostEffect.Started),
			(exception.Error.Kind, exception.Error.HostEffect));
	}

	[Fact]
	public void RecordReadOffsets_CopiesTheStoredTextsInDereferenceOrderWithoutEvaluatingThem()
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs(RecordStateStubs);

		RecordLuaOffsets copy = ReadOffsets([4L, 2L], 65536, 1_048_576);

		Assert.Equal([4, 2], copy.Records.Select(static record => record.Id).ToArray());
		// Cheat Engine applies Offset[2] first, so the copy starts with the highest index.
		Assert.Equal(["4c8", "hpOff+4", "-8"], copy.Records[0].Offsets);
		Assert.Empty(copy.Records[1].Offsets);
		Assert.Equal(0L, ReadGlobal("offsetReads"));
		LuaFixedScriptAssert.NeverLoadsCode(RecordLuaScripts.ReadOffsets);
	}

	[Fact]
	public void RecordReadOffsets_ItemAndByteBounds_CutTheCopy()
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs(RecordStateStubs);

		RecordLuaOffsets items = ReadOffsets([4L], 2, 1_048_576);
		// '4c8' fills 3 of 5 bytes, and 'hpOff+4' does not fit in the 2 left.
		RecordLuaOffsets bytes = ReadOffsets([4L], 65536, 5);
		CheatEngineToolException missing =
			Assert.Throws<CheatEngineToolException>(() => ReadOffsets([4L, 99L], 65536, 1_048_576));

		Assert.Equal(["4c8", "hpOff+4"], items.Records[0].Offsets);
		Assert.Equal(["4c8"], bytes.Records[0].Offsets);
		Assert.Equal((ToolErrorKind.NotFound, ToolHostEffect.NotStarted),
			(missing.Error.Kind, missing.Error.HostEffect));
	}

	[Fact]
	public void RecordSetScript_ActiveOrActivatingRecord_RefusesWithoutStoringTheText()
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs(RecordStateStubs + "\nrecords[11].state = true");

		CheatEngineToolException active =
			Assert.Throws<CheatEngineToolException>(() => SetScript(11L, "[ENABLE]\nnop"));
		InstallStubs("records[11].state = false; records[11].AsyncProcessing = true");
		CheatEngineToolException activating =
			Assert.Throws<CheatEngineToolException>(() => SetScript(11L, "[ENABLE]\nnop"));
		InstallStubs("records[11].AsyncProcessing = false");
		RecordLuaChanged stored = SetScript(11L, "[ENABLE]\nnop");

		Assert.All([active, activating], static exception =>
		{
			Assert.Equal((ToolErrorKind.InvalidState, ToolHostEffect.NotStarted),
				(exception.Error.Kind, exception.Error.HostEffect));
			Assert.Contains("record_set_active", exception.Error.Hint, StringComparison.Ordinal);
		});
		Assert.Equal(new RecordLuaChanged(11), stored);
		Assert.Equal("[ENABLE]\nnop", ModuleSymbolRead("records[11].Script"));
	}

	[Fact]
	public void RecordSetActive_Success_RestoresTheMissingHandler()
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs(RecordStateStubs);

		RecordLuaActivation activation = SetActive(2L, true);

		Assert.Equal(new RecordLuaActivation(true, false), activation);
		Assert.Equal((null, 2L), (ModuleSymbolRead("rawget(records[2], 'handler')"),
			ModuleSymbolRead("records[2].handlerSets")));
		LuaFixedScriptAssert.NeverLoadsCode(RecordLuaScripts.SetActive);
	}

	[Fact]
	public void RecordSetActive_FailedActivation_ReportsCheatEngineReasonAndText()
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs(RecordStateStubs + "\nrecords[11].outcome = {reason = 9, text = 'aobscan found nothing'}");

		RecordLuaActivation activation = SetActive(11L, true);

		Assert.Equal(new RecordLuaActivation(false, false, 9, "aobscan found nothing"), activation);
		Assert.Null(ModuleSymbolRead("rawget(records[11], 'handler')"));
	}

	[Fact]
	public void RecordSetActive_RecordsOwnHandler_RunsAndIsRestored()
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs(RecordStateStubs + """

		                                ownCalls = {}
		                                own = function(memrec, reason, text)
		                                	ownCalls[#ownCalls + 1] = memrec.ID .. ':' .. reason .. ':' .. text
		                                	return false
		                                end
		                                records[11].outcome = {reason = 7, text = 'assert'}
		                                records[11].handler = own
		                                """);

		RecordLuaActivation activation = SetActive(11L, true);

		Assert.Equal(new RecordLuaActivation(false, false, 7, "assert"), activation);
		Assert.Equal(("11:7:assert", true), (ModuleSymbolRead("ownCalls[1]"),
			ModuleSymbolRead("rawget(records[11], 'handler') == own")));
	}

	[Fact]
	public void RecordSetActive_HandlerThatAsksForARetry_RunsAtMostTheBoundAndMayRecover()
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs(RecordStateStubs + """

		                                ownCalls = 0
		                                records[11].outcome = {reason = 9, text = 'retry'}
		                                records[11].handler = function() ownCalls = ownCalls + 1 return true end
		                                records[2].outcome = {reason = 9, text = 'fixed', failures = 1}
		                                records[2].handler = function() return true end
		                                """);

		RecordLuaActivation endless = SetActive(11L, true);
		RecordLuaActivation recovered = SetActive(2L, true);

		// Cheat Engine repeats an activation while the handler returns true; the bound stops a handler that always
		// does.
		Assert.Equal(new RecordLuaActivation(false, false, 9, "retry"), endless);
		Assert.Equal((8L, 9L), (ReadGlobal("ownCalls"), ModuleSymbolRead("records[11].attempts")));
		Assert.Equal(new RecordLuaActivation(true, false, 9, "fixed"), recovered);
	}

	[Fact]
	public void RecordSetActive_DeactivationAsyncAndRaisedActivation_KeepTheHandlerUntouched()
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs(RecordStateStubs + """

		                                records[2].state = true
		                                records[6].state = true
		                                records[6].outcome = {keepActive = true}
		                                records[8].outcome = {async = true}
		                                records[11].outcome = {raise = true}
		                                ownHandler = function() return false end
		                                records[11].handler = ownHandler
		                                """);

		RecordLuaActivation off = SetActive(2L, false);
		RecordLuaActivation kept = SetActive(6L, false);
		RecordLuaActivation pending = SetActive(8L, true);
		CheatEngineToolException raised = Assert.Throws<CheatEngineToolException>(() => SetActive(11L, true));

		Assert.Equal(new RecordLuaActivation(false, false), off);
		Assert.Equal(new RecordLuaActivation(true, false), kept);
		Assert.Equal(new RecordLuaActivation(false, true), pending);
		Assert.Equal((ToolErrorKind.HostRefused, ToolHostEffect.Unknown), (raised.Error.Kind, raised.Error.HostEffect));
		// A raised activation still assigns the record's own handler back.
		Assert.Equal((0L, true, 2L), (ModuleSymbolRead("records[2].handlerSets"),
			ModuleSymbolRead("rawget(records[11], 'handler') == ownHandler"),
			ModuleSymbolRead("records[11].handlerSets")));
	}

	[Fact]
	public void RecordSetActive_LongReasonText_IsCutWithoutSplittingAUtf8Sequence()
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs(RecordStateStubs + "\nrecords[11].outcome = {reason = 4, text = string.rep('\\195\\169', 10)}");

		ToolDispatch dispatch = CreateNativeDispatch(new McpFeatureOptions());

		RecordLuaActivation split = dispatch.RunLua(CheatEngineToolNames.RecordSetActive, RecordLuaScripts.SetActive,
			RecordLuaJsonContext.Default.RecordLuaActivation, Token, 11L, true, 5, 8);
		RecordLuaActivation whole = dispatch.RunLua(CheatEngineToolNames.RecordSetActive, RecordLuaScripts.SetActive,
			RecordLuaJsonContext.Default.RecordLuaActivation, Token, 11L, true, 4, 8);

		// Five bytes hold two whole characters and the lead byte of a third, which is dropped; four hold two.
		Assert.Equal(("éé", "éé"), (split.Text, whole.Text));
		Assert.Equal(4, split.Reason);
	}

	[Fact]
	public void RecordTools_CreateReadAndActivate_MapTheNativeLuaResults()
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs(RecordStateStubs + """

		                                records[11].outcome = {reason = 7, text = 'The bytes at game.exe+10 differ'}
		                                """);
		ToolDispatch dispatch = RecordStateDispatch();

		RecordCreateResult created = new RecordMutationTools(dispatch).Create(
			[new RecordCreateSpec("Health", "game.exe+20", Offsets: ["10", "-8"])], Token);
		RecordGetResult read = new RecordReadTools(dispatch).Get([4], cancellationToken: Token);
		RecordActivationResult activation = new RecordMutationTools(dispatch).SetActive([2, 11], true, Token);

		RecordEntry record = Assert.Single(created.Records);
		Assert.Equal((20, 2), (record.Id, record.OffsetCount));
		Assert.Equal(["10", "-8"], record.Offsets!);
		Assert.Equal(("-8", "10"),
			(ModuleSymbolRead("records[20].texts[0]"), ModuleSymbolRead("records[20].texts[1]")));
		Assert.Null(ModuleSymbolRead("rawget(records[20], 'Value')"));
		Assert.Equal(["4C8", "hpOff+4", "-8"], read.Records[0].Offsets!);
		Assert.Equal(new RecordActivation(2, true, true, false), activation.Records[0]);
		Assert.Equal(new RecordActivation(11, true, false, false,
				new RecordActivationFailure(RecordActivationFailureReason.AssertFailed,
					"The bytes at game.exe+10 differ")),
			activation.Records[1]);
	}

	[Fact]
	public void RecordReaches_FollowsOnlyTheChildrenOfRecordsWithTheOption()
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs(RecordStateStubs + RecordTreeStubs);
		int[] numbers = [.. RecordArguments.NumberTypes];

		bool[] values = Reaches([30L, 34L, 2L], RecordArguments.RecursiveSetValueOption, numbers, 4096);
		bool[] activation = Reaches([34L, 35L], RecordMutationTools.ActivateChildrenOption, [11], 4096);
		bool[] deactivation = Reaches([34L, 35L], RecordMutationTools.DeactivateChildrenOption, [11], 4096);
		InstallStubs("records[31].Options = '[]'");
		bool[] stopped = Reaches([30L], RecordArguments.RecursiveSetValueOption, numbers, 4096);

		// Group 30 passes its value through string 31 to 4-byte value 32.
		Assert.Equal([true, false, false], values);
		// moActivateChildrenAsWell is not found inside moDeactivateChildrenAsWell.
		Assert.Equal([false, true], activation);
		Assert.Equal([true, true], deactivation);
		// Without the option on 31 the value stops there, and 31 itself takes text.
		Assert.Equal([false], stopped);
		LuaFixedScriptAssert.NeverLoadsCode(RecordLuaScripts.Reaches);
	}

	[Fact]
	public void RecordReaches_BudgetRunOutOrMissingRecord_ReportsReachedOrNotFound()
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs(RecordStateStubs + RecordTreeStubs);

		bool[] whole = Reaches([30L], RecordArguments.RecursiveSetValueOption, [11], 4096);
		bool[] cut = Reaches([30L], RecordArguments.RecursiveSetValueOption, [11], 1);
		CheatEngineToolException missing = Assert.Throws<CheatEngineToolException>(() =>
			Reaches([30L, 99L], RecordArguments.RecursiveSetValueOption, [11], 4096));

		// Nothing below 30 is an Auto Assembler record, but a walk cut by its budget counts as reaching one.
		Assert.Equal([false], whole);
		Assert.Equal([true], cut);
		Assert.Equal((ToolErrorKind.NotFound, ToolHostEffect.NotStarted),
			(missing.Error.Kind, missing.Error.HostEffect));
	}

	[Fact]
	public void RecordSetLayout_ByteArrayValue_RaisesTheLengthToTheValueBytesOnly()
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs(RecordStateStubs);

		SetLayout(8L, null, null, 3);
		long fromValue = (long) ModuleSymbolRead("records[8].Aob.Size")!;
		SetLayout(8L, 6, null, 3);
		long fromLength = (long) ModuleSymbolRead("records[8].Aob.Size")!;
		SetLayout(8L, 2, null, 3);
		long longerValue = (long) ModuleSymbolRead("records[8].Aob.Size")!;
		SetLayout(8L, null, null, 2);
		long shorterValue = (long) ModuleSymbolRead("records[8].Aob.Size")!;

		Assert.Equal((3L, 6L, 3L, 3L), (fromValue, fromLength, longerValue, shorterValue));
		Assert.True((bool) ModuleSymbolRead("records[8].ShowAsHex")!);
	}

	[Fact]
	public void RecordSetActive_HandlerThatIsNotALuaFunction_IsLeftUntouchedAndNoReasonIsCaptured()
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs(RecordStateStubs + """

		                                ownHandler = {}
		                                records[11].outcome = {reason = 9, text = 'not found'}
		                                records[11].handler = ownHandler
		                                """);

		RecordLuaActivation activation = SetActive(11L, true);

		// Cheat Engine returns the global a handler name is bound to, which need not be a function.
		Assert.Equal(new RecordLuaActivation(false, false), activation);
		Assert.Equal((0L, true), (ModuleSymbolRead("records[11].handlerSets"),
			ModuleSymbolRead("rawget(records[11], 'handler') == ownHandler")));
	}

	[Fact]
	public void RecordSetActive_HandlerThatInstallsAnotherHandler_KeepsTheNewHandler()
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs(RecordStateStubs + """

		                                replacement = function() return false end
		                                records[11].outcome = {reason = 9, text = 'not found'}
		                                records[11].handler = function(memrec)
		                                	memrec.OnActivationFailure = replacement
		                                	return false
		                                end
		                                """);

		RecordLuaActivation activation = SetActive(11L, true);

		Assert.Equal(new RecordLuaActivation(false, false, 9, "not found"), activation);
		Assert.True((bool) ModuleSymbolRead("rawget(records[11], 'handler') == replacement")!);
	}

	[Fact]
	public void RecordTools_ByteArrayCreateWithoutLength_LengthensTheRecordBeforeTheValue()
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs(RecordStateStubs);
		ToolDispatch dispatch = RecordStateDispatch();

		RecordCreateResult created = new RecordMutationTools(dispatch).Create(
			[new RecordCreateSpec("Nop", "game.exe+20", "90 90", VariableType.ByteArray)], Token);

		Assert.Equal((20, "90 90"), (Assert.Single(created.Records).Id, created.Records[0].Value));
		Assert.Equal((2L, true),
			(ModuleSymbolRead("records[20].Aob.Size"), ModuleSymbolRead("records[20].ShowAsHex")));
	}

	[Fact]
	public void RecordTools_ChangesPassedToNestedRecords_AreCheckedAgainstThem()
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs(RecordStateStubs + RecordTreeStubs);
		ToolDispatch dispatch = RecordStateDispatch(new McpFeatureOptions { EnableAutoAssembler = false });
		RecordMutationTools tools = new(dispatch);

		CheatEngineToolException text = Assert.Throws<CheatEngineToolException>(() =>
			tools.Update([new RecordUpdateSpec(30, Value: "[os.exit()]")], Token));
		RecordUpdateResult number = tools.Update([new RecordUpdateSpec(30, Value: "7")], Token);
		CheatEngineToolException script = Assert.Throws<CheatEngineToolException>(() =>
			tools.SetActive([35], true, Token));
		RecordActivationResult group = tools.SetActive([34], true, Token);

		Assert.Equal((ToolErrorKind.InvalidArgument, "updates[0].value"),
			(text.Error.Kind, text.Error.Details?.GetProperty("parameter").GetString()));
		Assert.Equal("7", Assert.Single(number.Records).Value);
		Assert.Equal(ToolErrorKind.CapabilityDisabled, script.Error.Kind);
		Assert.Equal(new RecordActivation(34, true, true, false), Assert.Single(group.Records));
	}

	private static RecordLuaChanged CreateRecord(string description, string address, int type)
	{
		return CreateNativeDispatch(new McpFeatureOptions()).RunLua(CheatEngineToolNames.RecordCreate,
			RecordLuaScripts.CreateRecord, RecordJsonContext.Default.RecordLuaChanged, Token, description, address,
			type);
	}

	private static RecordLuaChanged SetLayout(long id, int? length, bool? unicode, int? valueBytes = null)
	{
		return CreateNativeDispatch(new McpFeatureOptions()).RunLua(CheatEngineToolNames.RecordUpdate,
			RecordLuaScripts.SetLayout, RecordJsonContext.Default.RecordLuaChanged, Token, id, length, unicode,
			valueBytes);
	}

	private static bool[] Reaches(long[] ids, string option, int[] types, int budget)
	{
		return CreateNativeDispatch(new McpFeatureOptions()).RunLua(CheatEngineToolNames.RecordUpdate,
			RecordLuaScripts.Reaches, RecordLuaJsonContext.Default.RecordLuaReaches, Token, ids, option, types,
			budget).Reaches;
	}

	private static RecordLuaOffsets ReadOffsets(long[] ids, int items, int bytes)
	{
		return CreateNativeDispatch(new McpFeatureOptions()).RunLua(CheatEngineToolNames.RecordGet,
			RecordLuaScripts.ReadOffsets, RecordLuaJsonContext.Default.RecordLuaOffsets, Token, ids, items, bytes);
	}

	private static RecordLuaChanged SetScript(long id, string script)
	{
		return CreateNativeDispatch(new McpFeatureOptions()).RunLua(CheatEngineToolNames.RecordSetScript,
			RecordLuaScripts.SetScript, RecordJsonContext.Default.RecordLuaChanged, Token, id, script);
	}

	private static RecordLuaActivation SetActive(long id, bool active)
	{
		return CreateNativeDispatch(new McpFeatureOptions()).RunLua(CheatEngineToolNames.RecordSetActive,
			RecordLuaScripts.SetActive, RecordLuaJsonContext.Default.RecordLuaActivation, Token, id, active,
			RecordMutationTools.MaximumFailureTextBytes, RecordMutationTools.MaximumFailureHandlerCalls);
	}

	/// <summary>
	///     A dispatch whose Client copies records from the stubbed Lua state, writes values into it and places
	///     records, and whose Lua facade runs typed operations on the same state.
	/// </summary>
	private static ToolDispatch RecordStateDispatch(McpFeatureOptions? features = null)
	{
		ITableClient tables = ClientTestDouble.Create<ITableClient>((method, arguments) =>
		{
			object?[] values = arguments!;
			MemoryRecordId id = (MemoryRecordId) values[0]!;
			switch (method.Name)
			{
				case nameof(ITableClient.GetRecord):
					return StateSnapshot(id.Value);
				case nameof(ITableClient.Delete):
					InstallStubs($"records[{id.Value}] = nil");
					return null;
				case nameof(ITableClient.Update):
					MemoryRecordUpdate update = (MemoryRecordUpdate) values[1]!;
					Assert.Null(update.Description);
					InstallStubs($"records[{id.Value}].Value = '{update.Value}'");
					return StateSnapshot(id.Value);
				default:
					throw new NotSupportedException($"Unexpected table call {method.Name}.");
			}
		});
		ILuaClient lua = ClientTestDouble.Create<ILuaClient>((method, arguments) =>
		{
			Assert.Equal(nameof(ILuaClient.Execute), method.Name);
			Type resultType = method.GetGenericArguments()[1];
			MethodInfo tryExecute = typeof(ILuaOperation<>).MakeGenericType(resultType)
				.GetMethod(nameof(ILuaOperation<>.TryExecute))!;
			object?[] call = [ActiveContext.Instance, null, null];
			if ((bool) tryExecute.Invoke(arguments![0], call)!)
			{
				return call[1];
			}

			((CheatEngineFailure) call[2]!).Throw();
			return null;
		});
		ICheatEngineClient client = ClientTestDouble.Client((nameof(ICheatEngineClient.Lua), lua),
			(nameof(ICheatEngineClient.Tables), tables));
		IOptions<McpExecutionOptions> options = Options.Create(new McpExecutionOptions());
		return new ToolDispatch(client, new McpFeatureGate(Options.Create(features ?? new McpFeatureOptions())),
			options, new DispatchStatistics(options), TimeProvider.System, new RecordingLogger<ToolDispatch>(),
			new PluginFixedLuaExecutor(client));
	}

	/// <summary>Copies one stubbed record as the Client would: content, offset count, state and child count.</summary>
	private static MemoryRecordSnapshot StateSnapshot(int id)
	{
		string record = "records[" + id.ToString(CultureInfo.InvariantCulture) + "]";
		Assert.True((bool) ModuleSymbolRead(record + " ~= nil")!);
		return new MemoryRecordSnapshot(new MemoryRecordId(id), 0,
			new MemoryRecordContentSnapshot((string) ModuleSymbolRead(record + ".Description")!,
				(string) ModuleSymbolRead(record + ".Address")!,
				(string?) ModuleSymbolRead($"rawget({record}, 'Value')") ?? string.Empty,
				(VariableType) (long) ModuleSymbolRead(record + ".Type")!, null,
				(int) (long) ModuleSymbolRead(record + ".OffsetCount")!),
			new MemoryRecordStateSnapshot(new Address(0x401000), (bool) ModuleSymbolRead(record + ".Active")!,
				(int) (long) ModuleSymbolRead(record + ".Count")!));
	}
}
