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

/// <summary>Record child paging and dropdown lists against real Lua with only the CE globals stubbed.</summary>
public sealed partial class NativeLuaToolRuntimeTests
{
	/// <summary>
	///     Address-list records whose dropdown storage behaves like Cheat Engine's: <c>DropDownList</c> is a Strings
	///     object of <c>value:description</c> lines, the <c>DropDownValue</c> and <c>DropDownDescription</c>
	///     accessors split a line at its first colon (a line without one has an empty value), and a linked record
	///     reads its source's list. Record 4 is a value record with a list, 5 a parent with four children, 6 an Auto
	///     Assembler record, 7 a record linked to 4 and 8 a value record with a longer list.
	/// </summary>
	private const string RecordDropdownStubs = """
	                                           vtAutoAssembler = 11
	                                           function makeList(lines)
	                                           	local list = {items = {}, Count = 0}
	                                           	list.clear = function() list.items = {}; list.Count = 0 end
	                                           	list.add = function(line) list.items[list.Count] = line; list.Count = list.Count + 1; return list.Count - 1 end
	                                           	list.getString = function(index) return list.items[index] end
	                                           	for _, line in ipairs(lines) do list.add(line) end
	                                           	return list
	                                           end
	                                           function makeRecord(id, type, lines, source)
	                                           	local record = {ID = id, Type = type, DropDownList = makeList(lines), DropDownLinked = source ~= nil,
	                                           		DropDownLinkedMemrec = source and source.name or '', DropDownReadOnly = false,
	                                           		DropDownDescriptionOnly = false, DisplayAsDropDownListItem = false, Count = 0, Child = {}}
	                                           	local function effective() return (source and source.record or record).DropDownList end
	                                           	record.DropDownValue = setmetatable({}, {__index = function(_, index)
	                                           		local line = effective().items[index]
	                                           		local colon = string.find(line, ':', 1, true)
	                                           		if colon == nil then return '' end
	                                           		return string.sub(line, 1, colon - 1)
	                                           	end})
	                                           	record.DropDownDescription = setmetatable({}, {__index = function(_, index)
	                                           		local line = effective().items[index]
	                                           		local colon = string.find(line, ':', 1, true)
	                                           		if colon == nil then return line end
	                                           		return string.sub(line, colon + 1)
	                                           	end})
	                                           	return setmetatable(record, {__index = function(self, key)
	                                           		if key == 'DropDownCount' then return effective().Count end
	                                           	end})
	                                           end
	                                           records = {}
	                                           records[4] = makeRecord(4, 2, {'0:Off', '1:On: full', 'raw'})
	                                           records[4].DropDownDescriptionOnly = true
	                                           records[5] = makeRecord(5, 14, {})
	                                           records[5].Count = 4
	                                           for index = 0, 3 do records[5].Child[index] = makeRecord(10 + index, 2, {}) end
	                                           records[6] = makeRecord(6, vtAutoAssembler, {})
	                                           records[7] = makeRecord(7, 2, {'stale:own'}, {name = 'Mode', record = records[4]})
	                                           records[8] = makeRecord(8, 2, {'a:1', 'b:2', 'c:3'})
	                                           addressList = {}
	                                           addressList.getMemoryRecordByID = function(id) return records[id] end
	                                           getAddressList = function() return addressList end
	                                           """;

	[Fact]
	public void RecordChildIds_Page_ReturnsTheChildIdsAndTheChildCount()
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs(RecordDropdownStubs);

		RecordLuaChildren page = ChildIds(5L, 1, 2);
		RecordLuaChildren past = ChildIds(5L, 6, 10);

		Assert.Equal(4, page.Total);
		Assert.Equal([11, 12], page.Ids);
		Assert.Equal(4, past.Total);
		Assert.Empty(past.Ids);
		Assert.Equal([10, 11, 12, 13], ChildIds(5L, 0, 1000).Ids);
		LuaFixedScriptAssert.NeverLoadsCode(RecordLuaScripts.ChildIds);
	}

	[Fact]
	public void RecordChildIds_MissingParentOrChild_ReportsTheRefusal()
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs(RecordDropdownStubs + "\nrecords[5].Child[2] = nil");

		CheatEngineToolException missing = Assert.Throws<CheatEngineToolException>(() => ChildIds(99L, 0, 10));
		CheatEngineToolException hole = Assert.Throws<CheatEngineToolException>(() => ChildIds(5L, 0, 10));

		Assert.Equal((ToolErrorKind.NotFound, ToolHostEffect.NotStarted),
			(missing.Error.Kind, missing.Error.HostEffect));
		Assert.Equal((ToolErrorKind.HostRefused, ToolHostEffect.NotStarted), (hole.Error.Kind, hole.Error.HostEffect));
	}

	[Fact]
	public void RecordReadDropdowns_SplitsEachLineAtItsFirstColonAndCopiesTheOptions()
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs(RecordDropdownStubs);

		RecordLuaDropdowns copy = ReadDropdowns([4L, 6L], 1024, 4096, 1_048_576);

		Assert.Equal([4, 6], copy.Records.Select(static record => record.Id).ToArray());
		RecordDropdown mode = copy.Records[0].Dropdown;
		Assert.Equal(
			[
				new RecordDropdownItem("0", "Off"), new RecordDropdownItem("1", "On: full"),
				new RecordDropdownItem("", "raw")
			], mode.Items);
		Assert.Equal((3, false, false, true, false, null),
			(mode.ItemCount, mode.Truncated, mode.DisallowManualInput, mode.DescriptionOnly, mode.DisplayAsListItem,
				mode.LinkedTo));
		RecordDropdown none = copy.Records[1].Dropdown;
		Assert.Equal((0, 0, false), (none.Items.Length, none.ItemCount, none.Truncated));
		LuaFixedScriptAssert.NeverLoadsCode(RecordLuaScripts.ReadDropdowns);
	}

	[Fact]
	public void RecordReadDropdowns_LinkedRecord_ReportsTheSourceListAndItsDescription()
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs(RecordDropdownStubs);

		RecordDropdown linked = ReadDropdowns([7L], 1024, 4096, 1_048_576).Records[0].Dropdown;

		Assert.Equal(("Mode", 3, "0"), (linked.LinkedTo, linked.ItemCount, linked.Items[0].Value));
	}

	[Fact]
	public void RecordReadDropdowns_ItemAndByteBounds_MarkEveryCutListTruncated()
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs(RecordDropdownStubs);

		// Two items per record and three per call: record 4 keeps 2 of 3, record 8 the last 1 of 3.
		RecordLuaDropdowns items = ReadDropdowns([4L, 8L], 2, 3, 1_048_576);
		// Eleven bytes per call: "0" + "Off" and "1" + "On: full" fill 4 + 9 = 13, so only the first item fits.
		RecordLuaDropdowns bytes = ReadDropdowns([4L], 1024, 4096, 11);

		Assert.Equal((2, 3, true), (items.Records[0].Dropdown.Items.Length, items.Records[0].Dropdown.ItemCount,
			items.Records[0].Dropdown.Truncated));
		Assert.Equal((1, 3, true), (items.Records[1].Dropdown.Items.Length, items.Records[1].Dropdown.ItemCount,
			items.Records[1].Dropdown.Truncated));
		Assert.Equal((1, true), (bytes.Records[0].Dropdown.Items.Length, bytes.Records[0].Dropdown.Truncated));
	}

	[Fact]
	public void RecordReadDropdowns_MissingRecord_ReportsNotFound()
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs(RecordDropdownStubs);

		CheatEngineToolException exception =
			Assert.Throws<CheatEngineToolException>(() => ReadDropdowns([4L, 99L], 1024, 4096, 1_048_576));

		Assert.Equal((ToolErrorKind.NotFound, ToolHostEffect.NotStarted),
			(exception.Error.Kind, exception.Error.HostEffect));
	}

	[Fact]
	public void RecordSetDropdown_ReplacesTheLinesAndSetsOnlyTheGivenOptions()
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs(RecordDropdownStubs);

		RecordLuaChanged changed = SetDropdown(8L, ["0:Off", "1:On"], true, null, false);

		Assert.Equal(new RecordLuaChanged(8), changed);
		Assert.Equal((2L, "0:Off", "1:On"), (ModuleSymbolRead("records[8].DropDownList.Count"),
			ModuleSymbolRead("records[8].DropDownList.items[0]"),
			ModuleSymbolRead("records[8].DropDownList.items[1]")));
		Assert.Equal((true, false, false), (ModuleSymbolRead("records[8].DropDownReadOnly"),
			ModuleSymbolRead("records[8].DropDownDescriptionOnly"),
			ModuleSymbolRead("records[8].DisplayAsDropDownListItem")));
		// An omitted option keeps its current value.
		SetDropdown(4L, [], false, null, false);
		Assert.Equal((0L, true), (ModuleSymbolRead("records[4].DropDownList.Count"),
			ModuleSymbolRead("records[4].DropDownDescriptionOnly")));
		LuaFixedScriptAssert.NeverLoadsCode(RecordLuaScripts.SetDropdown);
	}

	[Fact]
	public void RecordSetDropdown_LinkedOrAutoAssemblerRecord_RefusesWithoutChangingTheList()
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs(RecordDropdownStubs);

		CheatEngineToolException linked =
			Assert.Throws<CheatEngineToolException>(() => SetDropdown(7L, ["1:On"], null, null, null));
		CheatEngineToolException script =
			Assert.Throws<CheatEngineToolException>(() => SetDropdown(6L, ["1:On"], null, null, null));
		CheatEngineToolException missing =
			Assert.Throws<CheatEngineToolException>(() => SetDropdown(99L, ["1:On"], null, null, null));

		Assert.Equal((ToolErrorKind.InvalidState, ToolHostEffect.NotStarted),
			(linked.Error.Kind, linked.Error.HostEffect));
		Assert.Contains("linkedTo", linked.Error.Hint, StringComparison.Ordinal);
		Assert.Equal((ToolErrorKind.InvalidState, ToolHostEffect.NotStarted),
			(script.Error.Kind, script.Error.HostEffect));
		Assert.Equal(ToolErrorKind.NotFound, missing.Error.Kind);
		Assert.Equal((1L, 0L), (ModuleSymbolRead("records[7].DropDownList.Count"),
			ModuleSymbolRead("records[6].DropDownList.Count")));
	}

	[Fact]
	public void RecordSetDropdown_HostDropsAnItem_ReportsTheStartedRefusal()
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs(RecordDropdownStubs + """

		                                   local list = records[8].DropDownList
		                                   local add = list.add
		                                   list.add = function(line) if line ~= '1:On' then return add(line) end return -1 end
		                                   """);

		CheatEngineToolException exception =
			Assert.Throws<CheatEngineToolException>(() => SetDropdown(8L, ["0:Off", "1:On"], null, null, null));

		Assert.Equal((ToolErrorKind.HostRefused, ToolHostEffect.Started),
			(exception.Error.Kind, exception.Error.HostEffect));
	}

	[Fact]
	public void RecordTools_DropdownAndChildPaths_MapTheNativeLuaResults()
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs(RecordDropdownStubs);
		ToolDispatch dispatch = RecordDispatch([4, 5, 7, 8, 10, 11, 12, 13]);

		RecordGetResult read = new RecordReadTools(dispatch).Get([4, 7], true, Token);
		RecordDropdownResult set = new RecordMutationTools(dispatch).SetDropdown(8,
			[new RecordDropdownItem("0", "Off"), new RecordDropdownItem("1")], displayAsListItem: true,
			cancellationToken: Token);
		RecordPage children = new RecordReadTools(dispatch).List(2, 1, 5, Token);

		Assert.Equal(("On: full", "Mode"), (read.Records[0].Dropdown!.Items[1].Description,
			read.Records[1].Dropdown!.LinkedTo));
		Assert.Equal([new RecordDropdownItem("0", "Off"), new RecordDropdownItem("1", "")], set.Record.Dropdown!.Items);
		Assert.True(set.Record.Dropdown.DisplayAsListItem);
		Assert.Equal((4, 3, 12), (children.Total, children.NextOffset, Assert.Single(children.Records).Id));
	}

	[Fact]
	public void RecordTools_CopiedListWithALineWithoutAColon_WritesBackAndReadsBackUnchanged()
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs(RecordDropdownStubs);
		ToolDispatch dispatch = RecordDispatch([4]);

		// Record 4 holds 'raw', a line without a colon, which Cheat Engine reads as an empty value.
		RecordDropdown before = new RecordReadTools(dispatch).Get([4], true, Token).Records[0].Dropdown!;
		RecordDropdown after = new RecordMutationTools(dispatch)
			.SetDropdown(4, before.Items, cancellationToken: Token).Record.Dropdown!;

		Assert.Equal(new RecordDropdownItem("", "raw"), before.Items[2]);
		Assert.Equal(before.Items, after.Items);
		Assert.Equal((before.ItemCount, before.DescriptionOnly), (after.ItemCount, after.DescriptionOnly));
		Assert.Equal((3L, ":raw"), (ModuleSymbolRead("records[4].DropDownList.Count"),
			ModuleSymbolRead("records[4].DropDownList.items[2]")));
	}

	private static RecordLuaChildren ChildIds(long id, int offset, int limit)
	{
		return CreateNativeDispatch(new McpFeatureOptions()).RunLua(CheatEngineToolNames.RecordList,
			RecordLuaScripts.ChildIds, RecordLuaJsonContext.Default.RecordLuaChildren, Token, id, offset, limit);
	}

	private static RecordLuaDropdowns ReadDropdowns(long[] ids, int perRecord, int items, int bytes)
	{
		return CreateNativeDispatch(new McpFeatureOptions()).RunLua(CheatEngineToolNames.RecordGet,
			RecordLuaScripts.ReadDropdowns, RecordLuaJsonContext.Default.RecordLuaDropdowns, Token, ids, perRecord,
			items, bytes);
	}

	private static RecordLuaChanged SetDropdown(long id, string[] lines, bool? readOnly, bool? descriptionOnly,
		bool? displayAsListItem)
	{
		return CreateNativeDispatch(new McpFeatureOptions()).RunLua(CheatEngineToolNames.RecordSetDropdown,
			RecordLuaScripts.SetDropdown, RecordJsonContext.Default.RecordLuaChanged, Token, id, lines, readOnly,
			descriptionOnly, displayAsListItem);
	}

	/// <summary>
	///     A dispatch whose Client copies the given record ids as value records and whose Lua facade runs typed
	///     operations on the test state, so a record tool runs its Client calls and fixed Lua together.
	/// </summary>
	private static ToolDispatch RecordDispatch(int[] ids)
	{
		ITableClient tables = ClientTestDouble.Create<ITableClient>((method, arguments) =>
		{
			object?[] values = arguments!;
			MemoryRecordId id = (MemoryRecordId) values[0]!;
			Assert.Contains(id.Value, ids);
			MemoryRecordSnapshot record = new(id, 0,
				new MemoryRecordContentSnapshot("Record", "game.exe+10", "0", VariableType.Dword),
				new MemoryRecordStateSnapshot(new Address(0x401000)));
			switch (method.Name)
			{
				case nameof(ITableClient.GetRecord):
					return record;
				case nameof(ITableClient.TryGetRecord):
					values[1] = record;
					values[2] = default(CheatEngineFailure);
					return true;
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
		return new ToolDispatch(client, new McpFeatureGate(Options.Create(new McpFeatureOptions())), options,
			new DispatchStatistics(options), TimeProvider.System, new RecordingLogger<ToolDispatch>(),
			new PluginFixedLuaExecutor(client));
	}
}
