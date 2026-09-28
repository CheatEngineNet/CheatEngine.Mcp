using System.Collections.Immutable;
using System.ComponentModel;
using System.Globalization;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

using CheatEngine.Client;
using CheatEngine.Client.Lua;
using CheatEngine.Client.Results;
using CheatEngine.Client.Tables;
using CheatEngine.Mcp.Core.Contract;
using CheatEngine.Mcp.Core.Features;
using CheatEngine.Mcp.Core.Lua;
using CheatEngine.Mcp.Tests.Support;
using CheatEngine.Mcp.Tools.Record;
using CheatEngine.SDK.Engine.AddressList;
using CheatEngine.SDK.Engine.Enums;
using CheatEngine.SDK.Engine.Values;

using Microsoft.Extensions.Options;

using ModelContextProtocol.Server;

namespace CheatEngine.Mcp.Tests.Tools.Record;

/// <summary>Contract-level behavior of the typed and fixed-Lua record tools.</summary>
public sealed partial class RecordToolsTests
{
	private static CancellationToken Token => TestContext.Current.CancellationToken;

	[Fact]
	public void List_PagesTheWholeListThroughTheTypedTableClient()
	{
		RecordTarget target = new();
		target.Records.AddRange([Snapshot(1, 0, "Health"), Snapshot(2, 1, "Ammo"), Snapshot(3, 2, "Gold")]);

		RecordPage page = new RecordReadTools(target.Dispatch).List(1, 1, cancellationToken: Token);

		Assert.Equal(3, page.Total);
		Assert.Equal("Ammo", Assert.Single(page.Records).Description);
		Assert.Equal(2, page.NextOffset);
		Assert.Equal(["Tables.GetRecordCount", "Tables.GetRecordAt"], target.Calls);
	}

	[Fact]
	public void List_ParentId_CopiesOnlyTheChildrenOfThePage()
	{
		RecordTarget target = new();
		target.Records.AddRange([Snapshot(5, 0, "Player"), Snapshot(6, 1, "Health"), Snapshot(7, 2, "Stats")]);
		target.Answer(static _ => new RecordLuaChildren(3, [7]));

		RecordPage page = new RecordReadTools(target.Dispatch).List(1, 1, 5, Token);

		Assert.Equal((3, 2), (page.Total, page.NextOffset));
		Assert.Equal((7, "Stats"), (Assert.Single(page.Records).Id, page.Records[0].Description));
		Assert.Equal(["Tables.GetRecord", "Lua.Execute", "Tables.TryGetRecord"], target.Calls);
		string source = Assert.Single(target.LuaSources);
		Assert.EndsWith(RecordLuaScripts.ChildIds, source, StringComparison.Ordinal);
		Assert.Contains("[1] = 5, [2] = 1, [3] = 1 }", source, StringComparison.Ordinal);
		LuaFixedScriptAssert.NeverLoadsCode(RecordLuaScripts.ChildIds);
		Assert.Empty(LuaFeatureScan.Scan(RecordLuaScripts.ChildIds));
	}

	[Fact]
	public void List_ParentIdLastPage_OmitsNextOffset()
	{
		RecordTarget target = new();
		target.Records.AddRange([Snapshot(5, 0, "Player"), Snapshot(6, 1, "Health")]);
		target.Answer(static _ => new RecordLuaChildren(1, [6]));

		RecordPage page = new RecordReadTools(target.Dispatch).List(parentId: 5, cancellationToken: Token);

		Assert.Equal((1, null), (page.Total, page.NextOffset));
		Assert.Equal(6, Assert.Single(page.Records).Id);
	}

	[Fact]
	public void List_ParentIdOffsetPastTheChildren_ReturnsAnEmptyPage()
	{
		RecordTarget target = new();
		target.Records.Add(Snapshot(5, 0, "Player"));
		target.Answer(static _ => new RecordLuaChildren(2, []));

		RecordPage page = new RecordReadTools(target.Dispatch).List(4, 10, 5, Token);

		Assert.Equal((2, null), (page.Total, page.NextOffset));
		Assert.Empty(page.Records);
		Assert.Equal(["Tables.GetRecord", "Lua.Execute"], target.Calls);
	}

	[Fact]
	public void List_ParentIdWithAStaleChildId_RenewsTheIdsThroughTheSubtreeCopy()
	{
		RecordTarget target = new();
		target.Records.AddRange([Snapshot(5, 0, "Player"), Snapshot(6, 1, "Health"), Snapshot(7, 2, "Stats")]);
		target.StaleIds.Add(7);
		target.Answer(static _ => new RecordLuaChildren(2, [6, 7]));
		MemoryRecordHierarchySnapshot grandchild = new(Snapshot(9, 4, "Nested"), []);
		target.Hierarchy = new MemoryRecordHierarchySnapshot(Snapshot(5, 0, "Player"),
		[
			new MemoryRecordHierarchySnapshot(Snapshot(6, 1, "Health"), []),
			new MemoryRecordHierarchySnapshot(Snapshot(7, 2, "Stats"), [grandchild])
		]);

		RecordPage page = new RecordReadTools(target.Dispatch).List(parentId: 5, cancellationToken: Token);

		Assert.Equal([6, 7], page.Records.Select(static record => record.Id).ToArray());
		Assert.Equal((2, null), (page.Total, page.NextOffset));
		Assert.Equal(
			["Tables.GetRecord", "Lua.Execute", "Tables.TryGetRecord", "Tables.TryGetRecord", "Tables.GetHierarchy"],
			target.Calls);
		Assert.Equal(5, target.HierarchyRoot);
		Assert.Equal((RecordArguments.MaximumScannedRecords + 1, RecordArguments.MaximumHierarchyDepth),
			(target.HierarchyRequest.MaximumItems, target.HierarchyRequest.MaximumDepth));
	}

	[Fact]
	public void List_ParentIdWithAMissingChild_ReportsTheClientFailure()
	{
		RecordTarget target = new();
		target.Records.Add(Snapshot(5, 0, "Player"));
		target.Answer(static _ => new RecordLuaChildren(1, [6]));

		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() =>
			new RecordReadTools(target.Dispatch).List(parentId: 5, cancellationToken: Token));

		Assert.Equal(ToolErrorKind.NotFound, exception.Error.Kind);
		Assert.DoesNotContain("Tables.GetHierarchy", target.Calls);
	}

	[Fact]
	public void List_NegativeParentId_RefusesBeforeAnyDispatch()
	{
		RecordTarget target = new();

		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() =>
			new RecordReadTools(target.Dispatch).List(parentId: -1, cancellationToken: Token));

		Assert.Equal("parentId", exception.Error.Details!.Value.GetProperty("parameter").GetString());
		Assert.Empty(target.Calls);
		Assert.Equal(0, target.Dispatcher.Calls);
	}

	[Fact]
	public void ListAndFind_DescribeTheWholeListWithNestedRecords()
	{
		// Cheat Engine's AddressList.Count and getMemoryRecord index every tree node, so both tools see nested
		// records; their descriptions must not promise a top-level view.
		string list = ToolDescription(typeof(RecordReadTools), nameof(RecordReadTools.List));
		string find = ToolDescription(typeof(RecordReadTools), nameof(RecordReadTools.Find));

		Assert.Contains("records nested below it", list, StringComparison.Ordinal);
		Assert.Contains("including records nested in groups at any depth", find, StringComparison.Ordinal);
		Assert.DoesNotContain("top-level", list, StringComparison.Ordinal);
		Assert.DoesNotContain("top-level", find, StringComparison.Ordinal);
		Assert.DoesNotContain("top-level", Described(typeof(RecordPage), nameof(RecordPage.Total)),
			StringComparison.Ordinal);
		Assert.DoesNotContain("top-level", Described(typeof(RecordFindResult), nameof(RecordFindResult.Total)),
			StringComparison.Ordinal);
	}

	[Fact]
	public void Create_ValueRecord_CreatesThroughFixedLuaAndWritesTheValueLast()
	{
		RecordTarget target = new();

		RecordCreateResult result = new RecordMutationTools(target.Dispatch).Create(
			[new RecordCreateSpec("Health", "game.exe+20", "100")], Token);

		RecordEntry created = Assert.Single(result.Records);
		Assert.Equal((1, "Health", "game.exe+20", "100"),
			(created.Id, created.Description, created.Address, created.Value));
		Assert.Equal(["Lua.Execute", "Tables.Update"], target.Calls);
		// The typed Client's Create always assigns a value, so it is never used for a new record.
		Assert.Empty(target.Definitions);
		string source = Assert.Single(target.LuaSources);
		Assert.EndsWith(RecordLuaScripts.CreateRecord, source, StringComparison.Ordinal);
		Assert.Contains("[1] = \"Health\", [2] = \"game.exe+20\", [3] = 2 }", source, StringComparison.Ordinal);
		MemoryRecordUpdate value = Assert.Single(target.Updates);
		Assert.Equal(("100", null, null, null),
			(value.Value, value.Description, value.AddressExpression, value.VariableType));
		LuaFixedScriptAssert.NeverLoadsCode(RecordLuaScripts.CreateRecord);
		Assert.Empty(LuaFeatureScan.Scan(RecordLuaScripts.CreateRecord));
		Assert.DoesNotContain(".Value", RecordLuaScripts.CreateRecord, StringComparison.Ordinal);
	}

	[Theory]
	[InlineData(VariableType.Byte)]
	[InlineData(VariableType.Dword)]
	[InlineData(VariableType.Qword)]
	[InlineData(VariableType.Single)]
	[InlineData(VariableType.Double)]
	[InlineData(VariableType.Binary)]
	[InlineData(VariableType.Grouped)]
	public void Create_EmptyValue_NeverAssignsAValue(VariableType variableType)
	{
		RecordTarget target = new();

		RecordCreateResult result = new RecordMutationTools(target.Dispatch).Create(
			[new RecordCreateSpec("Health", "game.exe+20", VariableType: variableType)], Token);

		Assert.Equal(variableType, Assert.Single(result.Records).VariableType);
		Assert.Equal(["Lua.Execute", "Tables.GetRecord"], target.Calls);
		Assert.Empty(target.Updates);
		Assert.Empty(target.Definitions);
	}

	[Fact]
	public void Create_WithOffsets_WritesTheValueOnlyAfterTheOffsets()
	{
		RecordTarget target = new();

		RecordCreateResult result = new RecordMutationTools(target.Dispatch).Create(
			[new RecordCreateSpec("Health", "game.exe+20", "100", Offsets: ["10", "-8", "0x4C8", "+18"])], Token);

		Assert.Equal(1, Assert.Single(result.Records).Id);
		Assert.Equal(["Lua.Execute", "Lua.Execute", "Tables.Update"], target.Calls);
		Assert.EndsWith(RecordLuaScripts.SetOffsets, target.LuaSources[1], StringComparison.Ordinal);
		Assert.Contains("[1] = 1, [2] = {16,-8,1224,24,}", target.LuaSources[1], StringComparison.Ordinal);
		MemoryRecordUpdate value = Assert.Single(target.Updates);
		Assert.Equal(("100", null, null, null),
			(value.Value, value.Description, value.AddressExpression, value.VariableType));
	}

	[Fact]
	public void Create_WithOffsetsAndNoValue_ReadsBackWithoutWritingAValue()
	{
		RecordTarget target = new();

		new RecordMutationTools(target.Dispatch).Create(
			[new RecordCreateSpec("Health", "game.exe+20", string.Empty, Offsets: ["10"])], Token);

		Assert.Equal(["Lua.Execute", "Lua.Execute", "Tables.GetRecord"], target.Calls);
		Assert.Contains("[2] = {16,}", target.LuaSources[1], StringComparison.Ordinal);
		Assert.Empty(target.Updates);
	}

	[Fact]
	public void Create_WithEmptyOffsets_SetsNoChain()
	{
		RecordTarget target = new();

		new RecordMutationTools(target.Dispatch).Create(
			[new RecordCreateSpec("Health", "game.exe+20", "5", Offsets: [])], Token);

		Assert.Equal(["Lua.Execute", "Tables.Update"], target.Calls);
	}

	[Fact]
	public void Create_WithParent_PlacesTheRecordBeforeItsOffsetsAndValue()
	{
		RecordTarget target = new();
		target.Records.Add(Snapshot(40, 0, "Player", variableType: VariableType.Grouped));

		new RecordMutationTools(target.Dispatch).Create(
			[new RecordCreateSpec("Health", "+10", "100", ParentId: 40, Offsets: ["8"])], Token);

		// A relative address resolves from the parent, so the parent is set before the value is written.
		Assert.Equal(["Lua.Execute", "Tables.SetParent", "Lua.Execute", "Tables.Update"], target.Calls);
		Assert.Equal((2, 40), Assert.Single(target.Parents));
	}

	[Fact]
	public void Create_ByteArray_TurnsOnHexadecimalDisplayBeforeTheValue()
	{
		RecordTarget target = new();

		new RecordMutationTools(target.Dispatch).Create(
			[new RecordCreateSpec("Code", "game.exe+20", "48 8B 05", VariableType.ByteArray)], Token);

		Assert.Equal(["Lua.Execute", "Lua.Execute", "Tables.Update"], target.Calls);
		Assert.EndsWith(RecordLuaScripts.SetLayout, target.LuaSources[1], StringComparison.Ordinal);
		Assert.Contains("[1] = 1, [2] = nil, [3] = nil, [4] = 3 }", target.LuaSources[1], StringComparison.Ordinal);
		LuaFixedScriptAssert.NeverLoadsCode(RecordLuaScripts.SetLayout);
		Assert.Empty(LuaFeatureScan.Scan(RecordLuaScripts.SetLayout));
	}

	[Fact]
	public void Create_Utf16StringWithoutValue_SetsItsLengthAndEncoding()
	{
		RecordTarget target = new();

		new RecordMutationTools(target.Dispatch).Create(
			[new RecordCreateSpec("Name", "game.exe+20", VariableType: VariableType.String, Length: 32, Unicode: true)],
			Token);

		Assert.Equal(["Lua.Execute", "Lua.Execute", "Tables.GetRecord"], target.Calls);
		Assert.Contains("[1] = 1, [2] = 32, [3] = true, [4] = nil }", target.LuaSources[1], StringComparison.Ordinal);
	}

	[Fact]
	public void Create_SingleByteStringWithAValue_NeedsNoLayoutStep()
	{
		RecordTarget target = new();

		new RecordMutationTools(target.Dispatch).Create(
			[new RecordCreateSpec("Name", "game.exe+20", "Hero", VariableType.String)], Token);

		Assert.Equal(["Lua.Execute", "Tables.Update"], target.Calls);
	}

	[Fact]
	public void Create_ByteArrayValueWithoutLength_LengthensTheRecordToTheValueBytes()
	{
		RecordTarget target = new();

		new RecordMutationTools(target.Dispatch).Create(
			[new RecordCreateSpec("Nop", "game.exe+20", "90 90", VariableType.ByteArray)], Token);

		// Cheat Engine lengthens a byte array only for a value more than 4 bytes longer, so the layout step does.
		Assert.Equal(["Lua.Execute", "Lua.Execute", "Tables.Update"], target.Calls);
		Assert.Contains("[1] = 1, [2] = nil, [3] = nil, [4] = 2 }", target.LuaSources[1], StringComparison.Ordinal);
		Assert.Contains("if a[4] ~= nil and a[4] > size then size = a[4] end", RecordLuaScripts.SetLayout,
			StringComparison.Ordinal);
	}

	[Theory]
	[InlineData("48 8B 05", 3)]
	[InlineData("488B05", 3)]
	[InlineData("48,8B-05", 3)]
	[InlineData("  90   90 ", 2)]
	[InlineData("4 8B", 2)]
	[InlineData("48B", 2)]
	[InlineData("48 ?? * 05", 4)]
	[InlineData("é", 1)]
	[InlineData("", 0)]
	public void ByteCount_SplitsTheValueAsCheatEngineDoes(string value, int expected)
	{
		Assert.Equal(expected, RecordArguments.ByteCount(value));
	}

	public static TheoryData<RecordCreateSpec, string> InvalidCreates => new()
	{
		{ new RecordCreateSpec("Name", "game.exe+20", VariableType: VariableType.String), "records[0].length" },
		{ new RecordCreateSpec("Code", "game.exe+20", VariableType: VariableType.ByteArray), "records[0].length" },
		{ new RecordCreateSpec("Health", "game.exe+20", "1", Length: 4), "records[0].length" },
		{
			new RecordCreateSpec("Name", "game.exe+20", "a", VariableType.String, Length: 4097),
			"records[0].length"
		},
		{ new RecordCreateSpec("Name", "game.exe+20", "a", VariableType.String, Length: 0), "records[0].length" },
		{
			new RecordCreateSpec("Code", "game.exe+20", "90", VariableType.ByteArray, Unicode: true),
			"records[0].unicode"
		},
		{ new RecordCreateSpec("Health", "game.exe+20", "[os.exit()]"), "records[0].value" },
		{ new RecordCreateSpec("Health", "game.exe+20", "1+2"), "records[0].value" },
		{ new RecordCreateSpec("Health", "game.exe+20", "(Ammo)"), "records[0].value" },
		{ new RecordCreateSpec("Health", "game.exe+20", "max"), "records[0].value" },
		{ new RecordCreateSpec("Speed", "game.exe+20", "2*3", VariableType.Single), "records[0].value" },
		{ new RecordCreateSpec("Speed", "game.exe+20", "1e-5", VariableType.Double), "records[0].value" },
		{ new RecordCreateSpec("Speed", "game.exe+20", "1.5E+3", VariableType.Single), "records[0].value" },
		{ new RecordCreateSpec("Code", "game.exe+20", " (Code) ", VariableType.ByteArray), "records[0].value" },
		{ new RecordCreateSpec("Health", "game.exe+20", null!), "records[0].value" }
	};

	[Theory]
	[MemberData(nameof(InvalidCreates))]
	public void Create_InvalidLayoutOrValue_RefusesBeforeAnyDispatch(RecordCreateSpec spec, string parameter)
	{
		RecordTarget target = new();

		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() =>
			new RecordMutationTools(target.Dispatch).Create([spec], Token));

		Assert.Equal((ToolErrorKind.InvalidArgument, ToolHostEffect.NotStarted, parameter),
			(exception.Error.Kind, exception.Error.HostEffect, Parameter(exception)));
		Assert.Equal(0, target.Dispatcher.Calls);
	}

	[Theory]
	[InlineData(VariableType.Dword, "100")]
	[InlineData(VariableType.Dword, "-5")]
	[InlineData(VariableType.Dword, "+5")]
	[InlineData(VariableType.Dword, "0x64")]
	[InlineData(VariableType.Dword, "$64")]
	[InlineData(VariableType.Dword, "FF")]
	[InlineData(VariableType.Qword, " 7FFFFFFFFFFFFFFF ")]
	[InlineData(VariableType.Single, "1.5")]
	[InlineData(VariableType.Single, "1,5")]
	[InlineData(VariableType.Double, "-1.5e3")]
	[InlineData(VariableType.Single, "1.5E3")]
	[InlineData(VariableType.Double, ".5")]
	[InlineData(VariableType.String, "1+2 (text)")]
	[InlineData(VariableType.ByteArray, "48 8B ?? 05")]
	[InlineData(VariableType.Binary, "101")]
	public void Create_ValuesCheatEngineReadsAsTextOrAPlainNumber_AreAccepted(VariableType variableType,
		string value)
	{
		RecordTarget target = new();

		new RecordMutationTools(target.Dispatch).Create(
			[new RecordCreateSpec("Value", "game.exe+20", value, variableType)], Token);

		Assert.Equal(value, Assert.Single(target.Updates).Value);
	}

	[Theory]
	[InlineData(VariableType.AutoAssembler, "8", "records[0].offsets")]
	[InlineData(VariableType.Dword, "80000000", "records[0].offsets[0]")]
	[InlineData(VariableType.Dword, "-80000001", "records[0].offsets[0]")]
	[InlineData(VariableType.Dword, "FFFFFFF8", "records[0].offsets[0]")]
	[InlineData(VariableType.Dword, "16h", "records[0].offsets[0]")]
	[InlineData(VariableType.Dword, "", "records[0].offsets[0]")]
	public void Create_InvalidOffsets_RefusesBeforeAnyDispatch(VariableType variableType, string offset,
		string parameter)
	{
		RecordTarget target = new(new McpFeatureOptions { EnableAutoAssembler = true });

		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() =>
			new RecordMutationTools(target.Dispatch).Create(
				[new RecordCreateSpec("Patch", "0", string.Empty, variableType, Offsets: [offset])], Token));

		Assert.Equal((ToolErrorKind.InvalidArgument, ToolHostEffect.NotStarted),
			(exception.Error.Kind, exception.Error.HostEffect));
		Assert.Equal(parameter, Parameter(exception));
		Assert.Empty(target.Calls);
	}

	[Fact]
	public void Create_NegativeOffsetWrittenAsTwosComplement_HintsAtTheMinusSign()
	{
		RecordTarget target = new();

		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() =>
			new RecordMutationTools(target.Dispatch).Create(
				[new RecordCreateSpec("Health", "game.exe+20", Offsets: ["FFFFFFF8"])], Token));

		Assert.Contains("-8", exception.Error.Hint, StringComparison.Ordinal);
	}

	[Fact]
	public void Update_OffsetsOnValueRecord_UsesFixedLuaAndReadsBackTheRecord()
	{
		RecordTarget target = new();
		target.Records.Add(Snapshot(7, 0, "Pointer"));

		RecordUpdateResult result = new RecordMutationTools(target.Dispatch).Update(
			[new RecordUpdateSpec(7, Offsets: ["10", "20"])], Token);

		Assert.Equal(7, Assert.Single(result.Records).Id);
		string source = Assert.Single(target.LuaSources);
		Assert.EndsWith(RecordLuaScripts.SetOffsets, source, StringComparison.Ordinal);
		Assert.Contains("[1] = 7, [2] = {16,32,}", source, StringComparison.Ordinal);
		Assert.Equal(["Tables.GetRecord", "Lua.Execute", "Tables.GetRecord"], target.Calls);
	}

	[Fact]
	public void Update_AddressValueAndOffsets_AppliesAddressThenOffsetsThenValue()
	{
		RecordTarget target = new();
		target.Records.Add(Snapshot(7, 0, "Health"));

		RecordUpdateResult result = new RecordMutationTools(target.Dispatch).Update(
			[new RecordUpdateSpec(7, Address: "game.exe+100", Value: "5", Offsets: ["18", "10"])], Token);

		Assert.Equal(["Tables.GetRecord", "Tables.Update", "Lua.Execute", "Tables.Update"], target.Calls);
		Assert.Equal(2, target.Updates.Count);
		Assert.Equal(("game.exe+100", null), (target.Updates[0].AddressExpression, target.Updates[0].Value));
		Assert.Equal((null, "5"), (target.Updates[1].AddressExpression, target.Updates[1].Value));
		Assert.Equal(("game.exe+100", "5"), (result.Records[0].Address, result.Records[0].Value));
	}

	[Fact]
	public void Update_OffsetsOnAutoAssemblerRecord_RefusesBeforeTheFirstChange()
	{
		RecordTarget target = new();
		target.Records.Add(Snapshot(1, 0, "Health"));
		target.Records.Add(Snapshot(2, 1, "Script", "0", string.Empty, VariableType.AutoAssembler));

		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() =>
			new RecordMutationTools(target.Dispatch).Update(
				[new RecordUpdateSpec(1, "Renamed"), new RecordUpdateSpec(2, Offsets: ["8"])], Token));

		Assert.Equal((ToolErrorKind.InvalidArgument, ToolHostEffect.NotStarted),
			(exception.Error.Kind, exception.Error.HostEffect));
		Assert.Equal("updates[1].offsets", Parameter(exception));
		Assert.Equal(["Tables.GetRecord", "Tables.GetRecord"], target.Calls);
		Assert.Equal("Health", target.Records[0].Content.Description);
	}

	[Fact]
	public void Update_ExplicitAutoAssemblerTypeWithOffsets_RefusesBeforeAnyDispatch()
	{
		RecordTarget target = new();

		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() =>
			new RecordMutationTools(target.Dispatch).Update(
				[new RecordUpdateSpec(3, VariableType: VariableType.AutoAssembler, Offsets: ["8"])], Token));

		Assert.Equal("updates[0].offsets", Parameter(exception));
		Assert.Empty(target.Calls);
	}

	[Fact]
	public void Update_OffsetOutsideTheSigned32BitRange_RefusesBeforeAnyDispatch()
	{
		RecordTarget target = new();

		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() =>
			new RecordMutationTools(target.Dispatch).Update(
				[new RecordUpdateSpec(3, Offsets: ["10", "100000000"])], Token));

		Assert.Equal(ToolErrorKind.InvalidArgument, exception.Error.Kind);
		Assert.Equal("updates[0].offsets[1]", Parameter(exception));
		Assert.Empty(target.Calls);
	}

	[Theory]
	[InlineData(VariableType.Dword)]
	[InlineData(VariableType.Single)]
	[InlineData(VariableType.ByteArray)]
	[InlineData(VariableType.Grouped)]
	public void Update_EmptyValueOnARecordOtherThanAString_RefusesBeforeTheFirstChange(VariableType variableType)
	{
		RecordTarget target = new();
		target.Records.Add(Snapshot(7, 0, "Health", variableType: variableType));

		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() =>
			new RecordMutationTools(target.Dispatch).Update([new RecordUpdateSpec(7, Value: string.Empty)], Token));

		Assert.Equal((ToolErrorKind.InvalidArgument, "updates[0].value"), (exception.Error.Kind, Parameter(exception)));
		Assert.Equal("Omit value to keep the current value.", exception.Error.Hint);
		Assert.Equal(["Tables.GetRecord"], target.Calls);
	}

	[Fact]
	public void Update_EmptyValueOnAStringRecord_WritesTheEmptyString()
	{
		RecordTarget target = new();
		target.Records.Add(Snapshot(7, 0, "Name", variableType: VariableType.String));

		new RecordMutationTools(target.Dispatch).Update([new RecordUpdateSpec(7, Value: string.Empty)], Token);

		Assert.Equal(string.Empty, Assert.Single(target.Updates).Value);
	}

	[Fact]
	public void Update_ValueThatCheatEngineWouldEvaluate_IsCheckedAgainstTheStoredType()
	{
		RecordTarget target = new();
		target.Records.AddRange([Snapshot(7, 0, "Name", variableType: VariableType.String), Snapshot(8, 1, "Health")]);
		RecordMutationTools tools = new(target.Dispatch);

		CheatEngineToolException number = Assert.Throws<CheatEngineToolException>(() =>
			tools.Update([new RecordUpdateSpec(7, Value: "1+1"), new RecordUpdateSpec(8, Value: "1+1")], Token));
		CheatEngineToolException retyped = Assert.Throws<CheatEngineToolException>(() =>
			tools.Update([new RecordUpdateSpec(7, Value: "x", VariableType: VariableType.Dword)], Token));

		Assert.Equal("updates[1].value", Parameter(number));
		Assert.Equal("updates[0].value", Parameter(retyped));
		Assert.Empty(target.Updates);
	}

	[Fact]
	public void Update_ValueOnAByteArray_TurnsOnHexadecimalDisplayFirst()
	{
		RecordTarget target = new();
		target.Records.Add(Snapshot(7, 0, "Code", variableType: VariableType.ByteArray));

		new RecordMutationTools(target.Dispatch).Update([new RecordUpdateSpec(7, Value: "90 90")], Token);

		Assert.Equal(["Tables.GetRecord", "Lua.Execute", "Tables.Update"], target.Calls);
		Assert.EndsWith(RecordLuaScripts.SetLayout, Assert.Single(target.LuaSources), StringComparison.Ordinal);
	}

	[Fact]
	public void Update_ByteArrayValueLongerThanTheLength_RaisesTheLengthToTheValueBytes()
	{
		RecordTarget target = new();
		target.Records.Add(Snapshot(7, 0, "Code", variableType: VariableType.ByteArray));

		new RecordMutationTools(target.Dispatch).Update([new RecordUpdateSpec(7, Value: "48 8B 05", Length: 2)],
			Token);

		Assert.Equal(["Tables.GetRecord", "Lua.Execute", "Tables.Update"], target.Calls);
		Assert.Contains("[1] = 7, [2] = 2, [3] = nil, [4] = 3 }", Assert.Single(target.LuaSources),
			StringComparison.Ordinal);
		Assert.Equal("48 8B 05", Assert.Single(target.Updates).Value);
	}

	[Fact]
	public void Update_TextThatARecursiveRecordPassesToANumberRecord_RefusesBeforeTheFirstChange()
	{
		RecordTarget target = new();
		target.Records.AddRange([
			Snapshot(6, 0, "Name", variableType: VariableType.String), Parent(5, "Cheats", VariableType.Grouped)
		]);
		target.Answer(static _ => new RecordLuaReaches([true]));

		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() =>
			new RecordMutationTools(target.Dispatch).Update(
				[new RecordUpdateSpec(6, Value: "[os.exit()]"), new RecordUpdateSpec(5, Value: "[os.exit()]")],
				Token));

		Assert.Equal((ToolErrorKind.InvalidArgument, ToolHostEffect.NotStarted, "updates[1].value"),
			(exception.Error.Kind, exception.Error.HostEffect, Parameter(exception)));
		Assert.Contains("moRecursiveSetValue", exception.Error.Message, StringComparison.Ordinal);
		Assert.Equal(["Tables.GetRecord", "Tables.GetRecord", "Lua.Execute"], target.Calls);
		Assert.Empty(target.Updates);
		// Only the record with children whose own type takes text is checked, in one pass.
		string source = Assert.Single(target.LuaSources);
		Assert.EndsWith(RecordLuaScripts.Reaches, source, StringComparison.Ordinal);
		Assert.Contains("[1] = {5,}, [2] = \"moRecursiveSetValue\", [3] = {0,1,2,3,4,5,13,}, [4] = 4096 }",
			source, StringComparison.Ordinal);
		LuaFixedScriptAssert.NeverLoadsCode(RecordLuaScripts.Reaches);
		Assert.Empty(LuaFeatureScan.Scan(RecordLuaScripts.Reaches));
	}

	[Theory]
	[InlineData(true, "100")]
	[InlineData(false, "[text]")]
	public void Update_RecursiveRecordValue_IsWrittenWhenNoNumberRecordRefusesIt(bool reached, string value)
	{
		RecordTarget target = new();
		target.Records.Add(Parent(5, "Name", VariableType.String));
		target.Answer(_ => new RecordLuaReaches([reached]));

		new RecordMutationTools(target.Dispatch).Update([new RecordUpdateSpec(5, Value: value)], Token);

		Assert.Equal(["Tables.GetRecord", "Lua.Execute", "Tables.Update"], target.Calls);
		Assert.Equal(value, Assert.Single(target.Updates).Value);
	}

	[Fact]
	public void Update_NumberRecordWithChildren_NeedsNoNestedRecordCheck()
	{
		RecordTarget target = new();
		target.Records.Add(Parent(5, "Health", VariableType.Dword));

		new RecordMutationTools(target.Dispatch).Update([new RecordUpdateSpec(5, Value: "5")], Token);

		// A number record takes only a plain number, which every nested record reads without Lua.
		Assert.Equal(["Tables.GetRecord", "Tables.Update"], target.Calls);
	}

	[Fact]
	public void Update_RetypeToByteArray_SetsTheLayoutAfterTheTypeAndBeforeTheValue()
	{
		RecordTarget target = new();
		target.Records.Add(Snapshot(7, 0, "Code"));

		new RecordMutationTools(target.Dispatch).Update(
			[new RecordUpdateSpec(7, VariableType: VariableType.ByteArray, Length: 6)], Token);

		Assert.Equal(["Tables.GetRecord", "Tables.Update", "Lua.Execute", "Tables.GetRecord"], target.Calls);
		Assert.Equal(VariableType.ByteArray, Assert.Single(target.Updates).VariableType);
		Assert.Contains("[1] = 7, [2] = 6, [3] = nil, [4] = nil }", Assert.Single(target.LuaSources),
			StringComparison.Ordinal);
	}

	[Fact]
	public void Update_StringEncodingOnly_UsesOnlyTheLayoutStep()
	{
		RecordTarget target = new();
		target.Records.Add(Snapshot(7, 0, "Name", variableType: VariableType.String));

		new RecordMutationTools(target.Dispatch).Update([new RecordUpdateSpec(7, Unicode: true)], Token);

		Assert.Equal(["Tables.GetRecord", "Lua.Execute", "Tables.GetRecord"], target.Calls);
		Assert.Contains("[1] = 7, [2] = nil, [3] = true, [4] = nil }", Assert.Single(target.LuaSources),
			StringComparison.Ordinal);
	}

	[Theory]
	[InlineData(VariableType.Dword, 4, null, "updates[0].length")]
	[InlineData(VariableType.ByteArray, null, true, "updates[0].unicode")]
	[InlineData(VariableType.String, 5000, null, "updates[0].length")]
	public void Update_LayoutTheStoredTypeDoesNotHave_RefusesBeforeTheFirstChange(VariableType variableType,
		int? length, bool? unicode, string parameter)
	{
		RecordTarget target = new();
		target.Records.Add(Snapshot(7, 0, "Value", variableType: variableType));

		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() =>
			new RecordMutationTools(target.Dispatch).Update([new RecordUpdateSpec(7, Length: length, Unicode: unicode)],
				Token));

		Assert.Equal((ToolErrorKind.InvalidArgument, parameter), (exception.Error.Kind, Parameter(exception)));
		Assert.Empty(target.LuaSources);
		Assert.Empty(target.Updates);
	}

	[Fact]
	public void SetActive_AutoAssemblerRecord_RequiresItsFeatureBeforeMutation()
	{
		RecordTarget target = new(new McpFeatureOptions { EnableAutoAssembler = false });
		target.Records.Add(Snapshot(9, 0, "Patch", variableType: VariableType.AutoAssembler));
		RecordMutationTools tools = new(target.Dispatch);

		CheatEngineToolException exception =
			Assert.Throws<CheatEngineToolException>(() => tools.SetActive([9], true, Token));

		Assert.Equal(ToolErrorKind.CapabilityDisabled, exception.Error.Kind);
		Assert.Equal(["Tables.GetRecord"], target.Calls);
	}

	[Theory]
	[InlineData(true, "moActivateChildrenAsWell")]
	[InlineData(false, "moDeactivateChildrenAsWell")]
	public void SetActive_ChangeThatReachesANestedAutoAssemblerRecord_RequiresItsFeature(bool active,
		string option)
	{
		RecordTarget target = new(new McpFeatureOptions { EnableAutoAssembler = false });
		target.Records.AddRange([Snapshot(4, 0, "Health"), Parent(5, "Cheats", VariableType.Grouped)]);
		target.Answer(static _ => new RecordLuaReaches([true]));

		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() =>
			new RecordMutationTools(target.Dispatch).SetActive([4, 5], active, Token));

		Assert.Equal((ToolErrorKind.CapabilityDisabled, ToolHostEffect.NotStarted),
			(exception.Error.Kind, exception.Error.HostEffect));
		Assert.Equal(["Tables.GetRecord", "Tables.GetRecord", "Lua.Execute"], target.Calls);
		string source = Assert.Single(target.LuaSources);
		Assert.EndsWith(RecordLuaScripts.Reaches, source, StringComparison.Ordinal);
		Assert.Contains($"[1] = {{5,}}, [2] = \"{option}\", [3] = {{11,}}, [4] = 4096 }}", source,
			StringComparison.Ordinal);
	}

	[Fact]
	public void SetActive_ParentThatReachesNoAutoAssemblerRecord_ChangesWithoutTheFeature()
	{
		RecordTarget target = new(new McpFeatureOptions { EnableAutoAssembler = false });
		target.Records.Add(Parent(5, "Cheats", VariableType.Grouped));
		target.Answer(static _ => new RecordLuaReaches([false]));
		target.Answer(static _ => new RecordLuaActivation(true, false));

		RecordActivationResult result = new RecordMutationTools(target.Dispatch).SetActive([5], true, Token);

		Assert.Equal(new RecordActivation(5, true, true, false), Assert.Single(result.Records));
		Assert.Equal(["Tables.GetRecord", "Lua.Execute", "Lua.Execute"], target.Calls);
		Assert.EndsWith(RecordLuaScripts.SetActive, target.LuaSources[1], StringComparison.Ordinal);
	}

	[Fact]
	public void SetActive_FeatureOn_SkipsTheNestedRecordCheck()
	{
		RecordTarget target = new(new McpFeatureOptions { EnableAutoAssembler = true });
		target.Records.Add(Parent(5, "Cheats", VariableType.Grouped));
		target.Answer(static _ => new RecordLuaActivation(true, false));

		new RecordMutationTools(target.Dispatch).SetActive([5], true, Token);

		Assert.EndsWith(RecordLuaScripts.SetActive, Assert.Single(target.LuaSources), StringComparison.Ordinal);
	}

	[Fact]
	public void SetActive_NestedRecordCheckWithAFlagMissing_ReportsAnInternalFault()
	{
		RecordTarget target = new(new McpFeatureOptions { EnableAutoAssembler = false });
		target.Records.Add(Parent(5, "Cheats", VariableType.Grouped));
		target.Answer(static _ => new RecordLuaReaches([]));

		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() =>
			new RecordMutationTools(target.Dispatch).SetActive([5], true, Token));

		Assert.Equal(ToolErrorKind.Internal, exception.Error.Kind);
	}

	[Fact]
	public void SetActive_RefusedActivation_ReportsCheatEngineReasonAndContinues()
	{
		RecordTarget target = new(new McpFeatureOptions { EnableAutoAssembler = true });
		target.Records.AddRange([
			Snapshot(9, 0, "Patch", variableType: VariableType.AutoAssembler), Snapshot(10, 1, "Health")
		]);
		target.Answer(source => source.Contains("[1] = 9,", StringComparison.Ordinal)
			? new RecordLuaActivation(false, false, 9, "Error in line 3 (aobscanmodule(...)): not found")
			: new RecordLuaActivation(true, false));

		RecordActivationResult result = new RecordMutationTools(target.Dispatch).SetActive([9, 10], true, Token);

		Assert.Equal(new RecordActivation(9, true, false, false,
				new RecordActivationFailure(RecordActivationFailureReason.AobNotFound,
					"Error in line 3 (aobscanmodule(...)): not found")),
			result.Records[0]);
		Assert.Equal(new RecordActivation(10, true, true, false), result.Records[1]);
		Assert.Equal(["Tables.GetRecord", "Tables.GetRecord", "Lua.Execute", "Lua.Execute"], target.Calls);
		Assert.All(target.LuaSources, static source =>
			Assert.EndsWith(RecordLuaScripts.SetActive, source, StringComparison.Ordinal));
		Assert.Contains("[1] = 9, [2] = true, [3] = 4096, [4] = 8 }", target.LuaSources[0], StringComparison.Ordinal);
		LuaFixedScriptAssert.NeverLoadsCode(RecordLuaScripts.SetActive);
		Assert.Empty(LuaFeatureScan.Scan(RecordLuaScripts.SetActive));
	}

	[Theory]
	[InlineData(true, true, false, null, null, null, null)]
	[InlineData(true, false, true, 7, "assert", null, null)]
	[InlineData(false, false, false, null, null, null, null)]
	[InlineData(true, false, false, 7, "The bytes at game.exe+10 are not what was expected",
		RecordActivationFailureReason.AssertFailed, "The bytes at game.exe+10 are not what was expected")]
	[InlineData(true, false, false, 1, "setValue failed", RecordActivationFailureReason.Inaccessible,
		"setValue failed")]
	[InlineData(true, false, false, 0, "Unknown", RecordActivationFailureReason.Unknown, "Unknown")]
	[InlineData(true, false, false, 9, "not found", RecordActivationFailureReason.AobNotFound, "not found")]
	[InlineData(true, false, false, 12, "future", RecordActivationFailureReason.Unknown, "future")]
	[InlineData(true, false, false, -1, "negative", RecordActivationFailureReason.Unknown, "negative")]
	[InlineData(true, false, false, 8, "", RecordActivationFailureReason.AobModuleNotFound, null)]
	[InlineData(true, false, false, null, null, RecordActivationFailureReason.NotReported, null)]
	[InlineData(false, true, false, 7, "stale", RecordActivationFailureReason.NotReported, null)]
	public void SetActiveFailure_MapsTheStateCheatEngineLeft(bool requested, bool active, bool pending, int? reason,
		string? text, RecordActivationFailureReason? expectedReason, string? expectedText)
	{
		RecordActivationFailure? failure =
			RecordMutationTools.Failure(requested, new RecordLuaActivation(active, pending, reason, text));

		Assert.Equal(expectedReason is { } expected ? new RecordActivationFailure(expected, expectedText) : null,
			failure);
	}

	[Fact]
	public void ActivationFailureReasons_AreCheatEngineTFailReasonInOrderAndSerializeInSnakeCase()
	{
		// Cheat Engine 7.7's TFailReason: afUnknown first, so every value is one above defines.lua's constant.
		string[] names =
		[
			"unknown", "inaccessible", "auto_assembler_error", "allocation_failed", "syntax_error", "lua_syntax_error",
			"structure_definition_error", "assert_failed", "aob_module_not_found", "aob_not_found",
			"include_not_found", "dll_injection_failed", "not_reported"
		];

		string[] serialized =
		[
			.. Enum.GetValues<RecordActivationFailureReason>().Select(static reason =>
				JsonSerializer.Serialize(reason, RecordJsonContext.Default.RecordActivationFailureReason).Trim('"'))
		];

		Assert.Equal(names, serialized);
		Assert.Equal(11, (int) RecordActivationFailureReason.DllInjectionFailed);
		string description = Described(typeof(RecordActivationFailure), nameof(RecordActivationFailure.Reason));
		Assert.All(names, name => Assert.Contains(name, description, StringComparison.Ordinal));
		string json = JsonSerializer.Serialize(
			new RecordActivationResult([new RecordActivation(1, true, true, false)]),
			RecordJsonContext.Default.RecordActivationResult);
		Assert.DoesNotContain("failure", json, StringComparison.Ordinal);
	}

	[Fact]
	public void Delete_UsesTypedDeleteAfterItCopiesEveryRequestedRecord()
	{
		RecordTarget target = new();
		target.Records.Add(Snapshot(4, 0, "Health"));
		target.Records.Add(Snapshot(8, 1, "Ammo"));

		RecordDeleteResult result = new RecordMutationTools(target.Dispatch).Delete([4, 8], Token);

		Assert.Equal(2, result.Deleted);
		Assert.Equal([4, 8], target.Deleted);
		Assert.Equal(["Tables.GetRecord", "Tables.GetRecord", "Tables.Delete", "Tables.Delete"], target.Calls);
	}

	[Fact]
	public void SetScript_AutoAssemblerRecord_UsesFixedLuaAndReadsBack()
	{
		RecordTarget target = new(new McpFeatureOptions { EnableAutoAssembler = true });
		target.Records.Add(Snapshot(12, 0, "Patch", variableType: VariableType.AutoAssembler));

		RecordScriptResult result = new RecordMutationTools(target.Dispatch).SetScript(12, "[ENABLE]\nnop", Token);

		Assert.Equal(12, result.Record.Id);
		string source = Assert.Single(target.LuaSources);
		Assert.EndsWith(RecordLuaScripts.SetScript, source, StringComparison.Ordinal);
		Assert.Equal(["Tables.GetRecord", "Lua.Execute", "Tables.GetRecord"], target.Calls);
	}

	[Theory]
	[InlineData(true, false)]
	[InlineData(false, true)]
	public void SetScript_ActiveOrActivatingRecord_RefusesBeforeTheLuaChange(bool active, bool activating)
	{
		RecordTarget target = new(new McpFeatureOptions { EnableAutoAssembler = true });
		target.Records.Add(new MemoryRecordSnapshot(new MemoryRecordId(12), 0,
			new MemoryRecordContentSnapshot("Patch", "0", string.Empty, VariableType.AutoAssembler),
			new MemoryRecordStateSnapshot(null, active, 0, activating, activating)));

		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() =>
			new RecordMutationTools(target.Dispatch).SetScript(12, "[ENABLE]\nnop", Token));

		Assert.Equal((ToolErrorKind.InvalidState, ToolHostEffect.NotStarted),
			(exception.Error.Kind, exception.Error.HostEffect));
		Assert.Contains("record_set_active", exception.Error.Hint, StringComparison.Ordinal);
		Assert.Equal(["Tables.GetRecord"], target.Calls);
		Assert.Contains("if record.Active or record.AsyncProcessing then", RecordLuaScripts.SetScript,
			StringComparison.Ordinal);
	}

	[Fact]
	public void Create_LaterFailure_RollsBackPriorCreatedRecordsInReverseOrder()
	{
		RecordTarget target = new()
		{
			RefusedCreate = 3
		};

		Assert.Throws<CheatEngineToolException>(() => new RecordMutationTools(target.Dispatch).Create(
		[
			new RecordCreateSpec("One", "1000", "1"), new RecordCreateSpec("Two", "2000", "2"),
			new RecordCreateSpec("Three", "3000", "3")
		], Token));

		Assert.Equal([2, 1], target.Deleted);
	}

	[Fact]
	public void Create_ValueRefusedByCheatEngine_DeletesTheRecordsItCreated()
	{
		RecordTarget target = new()
		{
			RefusedValue = "7"
		};

		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() =>
			new RecordMutationTools(target.Dispatch).Create(
				[new RecordCreateSpec("One", "1000", "1"), new RecordCreateSpec("Two", "2000", "7")], Token));

		Assert.Equal(ToolErrorKind.HostRefused, exception.Error.Kind);
		Assert.Equal([2, 1], target.Deleted);
	}

	[Theory]
	[InlineData(-1)]
	[InlineData(1001)]
	public void List_InvalidPage_RefusesBeforeAnyDispatch(int limit)
	{
		RecordTarget target = new();

		Assert.Throws<CheatEngineToolException>(() =>
			new RecordReadTools(target.Dispatch).List(0, limit, cancellationToken: Token));

		Assert.Empty(target.Calls);
		Assert.Equal(0, target.Dispatcher.Calls);
	}

	[Fact]
	public void Update_TooManyOffsets_RefusesBeforeAnyDispatch()
	{
		RecordTarget target = new();
		string[] offsets = [.. Enumerable.Repeat("8", RecordArguments.MaximumOffsets + 1)];

		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() =>
			new RecordMutationTools(target.Dispatch).Update([new RecordUpdateSpec(1, Offsets: offsets)], Token));

		Assert.Equal(ToolErrorKind.LimitExceeded, exception.Error.Kind);
		Assert.Empty(target.Calls);
	}

	[Fact]
	public void Get_PointerRecord_CopiesItsOffsetsInOneLuaPassInTheContractForm()
	{
		RecordTarget target = new();
		target.Records.AddRange([Snapshot(4, 0, "Health", offsetCount: 6), Snapshot(5, 1, "Ammo")]);
		target.Answer(static _ => new RecordLuaOffsets([
			new RecordLuaOffsetList(4, ["4c8", "0x10", "-8", "FFFFFFF8", "hpOffset+4", ""])
		]));

		RecordGetResult result = new RecordReadTools(target.Dispatch).Get([4, 5], cancellationToken: Token);

		Assert.Equal(["4C8", "10", "-8", "-8", "hpOffset+4", "0"], result.Records[0].Offsets!);
		Assert.Null(result.Records[1].Offsets);
		Assert.Equal(["Tables.GetRecord", "Tables.GetRecord", "Lua.Execute"], target.Calls);
		string source = Assert.Single(target.LuaSources);
		Assert.EndsWith(RecordLuaScripts.ReadOffsets, source, StringComparison.Ordinal);
		// Only the pointer record is read, within the per-call copy bounds.
		Assert.Contains("[1] = {4,}, [2] = 65536, [3] = 1048576 }", source, StringComparison.Ordinal);
		string json = JsonSerializer.Serialize(result, RecordJsonContext.Default.RecordGetResult);
		Assert.Contains("\"offsetCount\":6,\"offsets\":[\"4C8\",\"10\",\"-8\",\"-8\",\"hpOffset", json,
			StringComparison.Ordinal);
		Assert.Equal(json.IndexOf("\"offsets\"", StringComparison.Ordinal),
			json.LastIndexOf("\"offsets\"", StringComparison.Ordinal));
		LuaFixedScriptAssert.NeverLoadsCode(RecordLuaScripts.ReadOffsets);
		Assert.Empty(LuaFeatureScan.Scan(RecordLuaScripts.ReadOffsets));
		// The copy reads the stored texts and never evaluates an offset through Offset[] or getAddress().
		Assert.DoesNotContain(".Offset[", RecordLuaScripts.ReadOffsets, StringComparison.Ordinal);
		Assert.DoesNotContain(".getAddress(", RecordLuaScripts.ReadOffsets, StringComparison.Ordinal);
		Assert.DoesNotContain(".getOffset(", RecordLuaScripts.ReadOffsets, StringComparison.Ordinal);
	}

	[Fact]
	public void ListFindAndSelection_ShareTheOffsetCopy()
	{
		RecordTarget target = new();
		target.Records.AddRange([Snapshot(4, 0, "Health", offsetCount: 1), Snapshot(5, 1, "Ammo")]);
		target.Answer(static source => new RecordLuaOffsets(
			source.Contains("[1] = {4,}", StringComparison.Ordinal) ? [new RecordLuaOffsetList(4, ["18"])] : []));
		RecordReadTools tools = new(target.Dispatch);

		RecordPage page = tools.List(cancellationToken: Token);
		RecordFindResult found = tools.Find(descriptionContains: "Health", cancellationToken: Token);

		Assert.Equal(["18"], page.Records[0].Offsets!);
		Assert.Null(page.Records[1].Offsets);
		Assert.Equal(["18"], Assert.Single(found.Records, static record => record.Id == 4).Offsets!);
		Assert.Equal(2, target.LuaSources.Count);
	}

	[Fact]
	public void Get_OffsetCopyForAnotherRecord_ReportsAnInternalFault()
	{
		RecordTarget target = new();
		target.Records.Add(Snapshot(4, 0, "Health", offsetCount: 1));
		target.Answer(static _ => new RecordLuaOffsets([new RecordLuaOffsetList(5, ["8"])]));

		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() =>
			new RecordReadTools(target.Dispatch).Get([4], cancellationToken: Token));

		Assert.Equal(ToolErrorKind.Internal, exception.Error.Kind);
	}

	[Theory]
	[InlineData("10", "10")]
	[InlineData("4c8", "4C8")]
	[InlineData(" -8 ", "-8")]
	[InlineData("+18", "18")]
	[InlineData("0x10", "10")]
	[InlineData("-0x10", "-10")]
	[InlineData("$20", "20")]
	[InlineData("FFFFFFF8", "-8")]
	[InlineData("80000000", "-80000000")]
	[InlineData("100000010", "10")]
	[InlineData("-80000000", "-80000000")]
	[InlineData("", "0")]
	[InlineData(null, "0")]
	[InlineData("hpOffset+4", "hpOffset+4")]
	[InlineData("[[base]+8]", "[[base]+8]")]
	[InlineData("#16", "#16")]
	[InlineData("0x", "0x")]
	public void CopiedOffset_FormatsNumbersAsCheatEngineAppliesThemAndKeepsOtherText(string? stored, string expected)
	{
		Assert.Equal(expected, RecordArguments.CopiedOffset(stored));
	}

	[Fact]
	public void CopiedOffsets_RoundTripIntoTheOffsetInput()
	{
		string[] stored = ["4C8", "-8", "0x7fffffff", "80000000"];
		string[] copied = [.. stored.Select(RecordArguments.CopiedOffset)];

		int[] parsed = RecordArguments.Offsets(copied, "offsets");

		Assert.Equal([0x4C8, -8, int.MaxValue, int.MinValue], parsed);
	}

	[Fact]
	public void OffsetDescriptions_NameTheSignedHexadecimalFormOfThePointerTools()
	{
		string create = Described(typeof(RecordCreateSpec), nameof(RecordCreateSpec.Offsets));
		string update = Described(typeof(RecordUpdateSpec), nameof(RecordUpdateSpec.Offsets));
		string entry = Described(typeof(RecordEntry), nameof(RecordEntry.Offsets));

		Assert.All([create, update], text =>
		{
			Assert.Contains(RecordArguments.OffsetBounds, text, StringComparison.Ordinal);
			Assert.Contains("dereference order, nearest the base first", text, StringComparison.Ordinal);
		});
		Assert.Contains("pointer_list_paths and pointer_read_chain", RecordArguments.OffsetBounds,
			StringComparison.Ordinal);
		Assert.Contains("omitted when offsetCount is 0", entry, StringComparison.Ordinal);
		Assert.Contains("without being evaluated", entry, StringComparison.Ordinal);
		Assert.Equal(typeof(string[]), typeof(RecordCreateSpec).GetProperty(nameof(RecordCreateSpec.Offsets))!
			.PropertyType);
	}

	[Fact]
	public void ValueDescriptions_StateTheEmptyValueAndNumberRules()
	{
		string create = Described(typeof(RecordCreateSpec), nameof(RecordCreateSpec.Value));
		string update = Described(typeof(RecordUpdateSpec), nameof(RecordUpdateSpec.Value));

		Assert.Contains("Empty, the default, writes nothing and leaves memory unchanged for every type", create,
			StringComparison.Ordinal);
		Assert.Contains("Lua expression", create, StringComparison.Ordinal);
		Assert.Contains("accepted only for a string record (6), where it writes an empty string", update,
			StringComparison.Ordinal);
		Assert.Equal(string.Empty, new RecordCreateSpec("Health", "game.exe+20").Value);
		// A signed exponent puts a + or - after the first character, which Cheat Engine sends to Lua.
		Assert.Contains("unsigned exponent", create, StringComparison.Ordinal);
		Assert.Contains("lengthened to the number of bytes in the value", create, StringComparison.Ordinal);
		Assert.Contains("moRecursiveSetValue", update, StringComparison.Ordinal);
		Assert.Contains("the tool sets a byte array record to the number of bytes in the value",
			RecordArguments.LengthText, StringComparison.Ordinal);
		string setActive = ToolDescription(typeof(RecordMutationTools), nameof(RecordMutationTools.SetActive));
		Assert.Contains("moActivateChildrenAsWell", setActive, StringComparison.Ordinal);
		Assert.Contains("moDeactivateChildrenAsWell", setActive, StringComparison.Ordinal);
	}

	[Fact]
	public void VariableTypeDescriptions_NameTheCheatEngineIntegers()
	{
		(VariableType Type, string Text)[] accepted =
		[
			(VariableType.Byte, "0 byte"), (VariableType.Word, "1 2-byte integer"),
			(VariableType.Dword, "2 4-byte integer"), (VariableType.Qword, "3 8-byte integer"),
			(VariableType.Single, "4 float"), (VariableType.Double, "5 double"), (VariableType.String, "6 string"),
			(VariableType.ByteArray, "8 byte array"), (VariableType.Binary, "9 binary"),
			(VariableType.AutoAssembler, "11 Auto Assembler script"), (VariableType.Custom, "13 custom"),
			(VariableType.Grouped, "14 group header")
		];
		(VariableType Type, string Text)[] refused =
		[
			(VariableType.WideString, "7 UTF-16 string"), (VariableType.All, "10 all types"),
			(VariableType.Pointer, "12 pointer-sized hexadecimal value")
		];
		string find = typeof(RecordReadTools).GetMethod(nameof(RecordReadTools.Find))!.GetParameters()
			.Single(static parameter => parameter.Name == "variableType")
			.GetCustomAttribute<DescriptionAttribute>()!.Description;
		VariableType[] named = [.. accepted.Concat(refused).Select(static entry => entry.Type).Order()];

		// Every Cheat Engine value type is named: the input list and the refused types together cover the enum.
		Assert.Equal(Enum.GetValues<VariableType>(), named);
		Assert.All(accepted.Concat(refused), static entry =>
		{
			Assert.StartsWith(((int) entry.Type).ToString(CultureInfo.InvariantCulture) + " ",
				entry.Text, StringComparison.Ordinal);
			Assert.Contains(entry.Text, RecordArguments.StoredVariableTypes, StringComparison.Ordinal);
		});
		Assert.All(accepted, static entry =>
			Assert.Contains(entry.Text, RecordArguments.VariableTypes, StringComparison.Ordinal));
		// Cheat Engine keeps 7, 10 and 12 on a record unchanged, but its value code has no case for them.
		Assert.All(refused, static entry =>
			Assert.DoesNotContain(entry.Text, RecordArguments.VariableTypes, StringComparison.Ordinal));
		Assert.Contains("7 (UTF-16 string), 10 (all types) and 12 (pointer-sized value) are refused",
			RecordArguments.VariableTypes, StringComparison.Ordinal);
		Assert.Contains("use 6 with unicode true for UTF-16 text", RecordArguments.VariableTypes,
			StringComparison.Ordinal);
		Assert.Contains("has 7, 10 or 12, and Cheat Engine reads its value as empty text",
			RecordArguments.StoredVariableTypes, StringComparison.Ordinal);
		Assert.Contains(RecordArguments.StoredVariableTypes,
			Described(typeof(RecordEntry), nameof(RecordEntry.VariableType)), StringComparison.Ordinal);
		Assert.Contains(RecordArguments.StoredVariableTypes, find, StringComparison.Ordinal);
		Assert.Contains(RecordArguments.VariableTypes,
			Described(typeof(RecordCreateSpec), nameof(RecordCreateSpec.VariableType)), StringComparison.Ordinal);
		Assert.Contains(RecordArguments.VariableTypes,
			Described(typeof(RecordUpdateSpec), nameof(RecordUpdateSpec.VariableType)), StringComparison.Ordinal);
	}

	[Theory]
	[InlineData(VariableType.WideString)]
	[InlineData(VariableType.All)]
	[InlineData(VariableType.Pointer)]
	public void Find_TypeOnlyAScriptOrTableGivesARecord_SearchesTheStoredType(VariableType variableType)
	{
		RecordTarget target = new();
		target.Records.AddRange([Snapshot(3, 0, "Name", variableType: variableType), Snapshot(4, 1, "Health")]);

		RecordFindResult result =
			new RecordReadTools(target.Dispatch).Find(variableType: variableType, cancellationToken: Token);

		Assert.Equal(variableType, Assert.Single(target.Searches).VariableType);
		Assert.Equal((1, false), (result.Total, result.Truncated));
		Assert.Equal((3, variableType), (Assert.Single(result.Records).Id, result.Records[0].VariableType));
	}

	[Fact]
	public void Find_UndefinedType_RefusesBeforeAnyDispatch()
	{
		RecordTarget target = new();

		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() =>
			new RecordReadTools(target.Dispatch).Find(variableType: (VariableType) 15, cancellationToken: Token));

		Assert.Equal((ToolErrorKind.InvalidArgument, "variableType"), (exception.Error.Kind, Parameter(exception)));
		Assert.Empty(target.Calls);
	}

	[Theory]
	[InlineData(VariableType.WideString, "Use 6 with unicode true for a UTF-16 string record.")]
	[InlineData(VariableType.All, "Use the type of the value")]
	[InlineData(VariableType.Pointer, "Use 3 on a 64-bit process or 2 otherwise; pass offsets")]
	public void CreateAndUpdate_TypeWithoutCheatEngineValueCode_RefuseBeforeAnyDispatch(VariableType variableType,
		string hint)
	{
		RecordTarget target = new();
		RecordMutationTools tools = new(target.Dispatch);

		CheatEngineToolException create = Assert.Throws<CheatEngineToolException>(() =>
			tools.Create([new RecordCreateSpec("Typed", "game.exe+20", "0", variableType)], Token));
		CheatEngineToolException update = Assert.Throws<CheatEngineToolException>(() =>
			tools.Update([new RecordUpdateSpec(1, VariableType: variableType)], Token));

		Assert.Equal(("records[0].variableType", "updates[0].variableType"), (Parameter(create), Parameter(update)));
		Assert.All([create, update], exception =>
		{
			Assert.Equal((ToolErrorKind.InvalidArgument, ToolHostEffect.NotStarted),
				(exception.Error.Kind, exception.Error.HostEffect));
			Assert.StartsWith(hint, exception.Error.Hint, StringComparison.Ordinal);
		});
		Assert.Empty(target.Calls);
		Assert.Equal(0, target.Dispatcher.Calls);
	}

	[Fact]
	public void ScriptOverTheUtf8Bound_IsRefusedBeforeAnyDispatch()
	{
		RecordTarget target = new(new McpFeatureOptions { EnableAutoAssembler = true });
		// 400000 characters is under the character bound, but 1200000 bytes as UTF-8.
		string script = new('€', 400_000);
		RecordMutationTools tools = new(target.Dispatch);

		CheatEngineToolException set =
			Assert.Throws<CheatEngineToolException>(() => tools.SetScript(12, script, Token));
		CheatEngineToolException create = Assert.Throws<CheatEngineToolException>(() => tools.Create(
			[new RecordCreateSpec("Patch", "0", string.Empty, VariableType.AutoAssembler, Script: script)], Token));

		Assert.Equal((ToolErrorKind.LimitExceeded, "script"), (set.Error.Kind, Parameter(set)));
		Assert.Equal((ToolErrorKind.LimitExceeded, "records[0].script"), (create.Error.Kind, Parameter(create)));
		Assert.Empty(target.Calls);
		Assert.Equal(0, target.Dispatcher.Calls);
	}

	[Fact]
	public void Get_WithoutIncludeDropdown_CopiesThroughTheClientOnlyAndOmitsDropdown()
	{
		RecordTarget target = new();
		target.Records.Add(Snapshot(4, 0, "Mode"));

		RecordGetResult result = new RecordReadTools(target.Dispatch).Get([4], cancellationToken: Token);

		Assert.Null(Assert.Single(result.Records).Dropdown);
		Assert.Equal(["Tables.GetRecord"], target.Calls);
		string json = JsonSerializer.Serialize(result, RecordJsonContext.Default.RecordGetResult);
		Assert.DoesNotContain("dropdown", json, StringComparison.Ordinal);
		Assert.DoesNotContain("offsets", json, StringComparison.Ordinal);
	}

	[Fact]
	public void Get_IncludeDropdown_CopiesEveryDropdownInOneBoundedLuaPass()
	{
		RecordTarget target = new();
		target.Records.AddRange([Snapshot(4, 0, "Mode"), Snapshot(8, 1, "Linked")]);
		RecordDropdown mode = new([new RecordDropdownItem("0", "Off"), new RecordDropdownItem("1", "On")], 2, false,
			true, false, true);
		RecordDropdown linked = new([], 0, false, false, false, false, "Missing source");
		target.Answer(_ => new RecordLuaDropdowns([new RecordLuaDropdown(4, mode), new RecordLuaDropdown(8, linked)]));

		RecordGetResult result = new RecordReadTools(target.Dispatch).Get([4, 8], true, Token);

		Assert.Equal([mode, linked], result.Records.Select(static record => record.Dropdown).ToArray());
		Assert.Equal(["Tables.GetRecord", "Tables.GetRecord", "Lua.Execute"], target.Calls);
		string source = Assert.Single(target.LuaSources);
		Assert.EndsWith(RecordLuaScripts.ReadDropdowns, source, StringComparison.Ordinal);
		Assert.Contains("[1] = {4,8,}, [2] = 1024, [3] = 4096, [4] = 1048576 }", source, StringComparison.Ordinal);
		string json = JsonSerializer.Serialize(result, RecordJsonContext.Default.RecordGetResult);
		Assert.Contains(
			"\"dropdown\":{\"items\":[{\"value\":\"0\",\"description\":\"Off\"}," +
			"{\"value\":\"1\",\"description\":\"On\"}]," +
			"\"itemCount\":2,\"truncated\":false,\"disallowManualInput\":true,\"descriptionOnly\":false," +
			"\"displayAsListItem\":true}", json, StringComparison.Ordinal);
		Assert.Contains("\"linkedTo\":\"Missing source\"", json, StringComparison.Ordinal);
		LuaFixedScriptAssert.NeverLoadsCode(RecordLuaScripts.ReadDropdowns);
		Assert.Empty(LuaFeatureScan.Scan(RecordLuaScripts.ReadDropdowns));
	}

	[Fact]
	public void Get_DropdownCopyForAnotherRecord_ReportsAnInternalFault()
	{
		RecordTarget target = new();
		target.Records.Add(Snapshot(4, 0, "Mode"));
		target.Answer(static _ =>
			new RecordLuaDropdowns([new RecordLuaDropdown(5, new RecordDropdown([], 0, false, false, false, false))]));

		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() =>
			new RecordReadTools(target.Dispatch).Get([4], true, Token));

		Assert.Equal(ToolErrorKind.Internal, exception.Error.Kind);
	}

	[Fact]
	public void SetDropdown_ReplacesTheListThroughFixedLuaAndReadsItBack()
	{
		RecordTarget target = new();
		target.Records.Add(Snapshot(4, 0, "Mode"));
		RecordDropdown stored = new([new RecordDropdownItem("0", "Off"), new RecordDropdownItem("1", "On: full")], 2,
			false, true, false, false);
		target.Answer(_ => new RecordLuaDropdowns([new RecordLuaDropdown(4, stored)]));

		RecordDropdownResult result = new RecordMutationTools(target.Dispatch).SetDropdown(4,
			[new RecordDropdownItem("0", "Off"), new RecordDropdownItem("1", "On: full")], true,
			cancellationToken: Token);

		Assert.Equal((4, stored), (result.Record.Id, result.Record.Dropdown));
		Assert.Equal(["Tables.GetRecord", "Lua.Execute", "Lua.Execute", "Tables.GetRecord"], target.Calls);
		Assert.EndsWith(RecordLuaScripts.SetDropdown, target.LuaSources[0], StringComparison.Ordinal);
		Assert.Contains("[1] = 4, [2] = {\"0:Off\",\"1:On: full\",}, [3] = true, [4] = nil, [5] = nil }",
			target.LuaSources[0], StringComparison.Ordinal);
		Assert.EndsWith(RecordLuaScripts.ReadDropdowns, target.LuaSources[1], StringComparison.Ordinal);
		Assert.Contains("[1] = {4,}", target.LuaSources[1], StringComparison.Ordinal);
		LuaFixedScriptAssert.NeverLoadsCode(RecordLuaScripts.SetDropdown);
		Assert.Empty(LuaFeatureScan.Scan(RecordLuaScripts.SetDropdown));
	}

	[Fact]
	public void SetDropdown_EmptyList_RemovesItAndTurnsEveryOptionOff()
	{
		RecordTarget target = new();
		target.Records.Add(Snapshot(4, 0, "Mode"));
		target.Answer(static _ =>
			new RecordLuaDropdowns([new RecordLuaDropdown(4, new RecordDropdown([], 0, false, false, false, false))]));

		RecordDropdownResult result = new RecordMutationTools(target.Dispatch).SetDropdown(4, [], false,
			cancellationToken: Token);

		Assert.Empty(result.Record.Dropdown!.Items);
		Assert.Contains("[1] = 4, [2] = {}, [3] = false, [4] = false, [5] = false }", target.LuaSources[0],
			StringComparison.Ordinal);
	}

	[Fact]
	public void SetDropdown_EmptyListWithAnOptionOn_RefusesBeforeAnyDispatch()
	{
		RecordTarget target = new();

		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() =>
			new RecordMutationTools(target.Dispatch).SetDropdown(4, [], descriptionOnly: true,
				cancellationToken: Token));

		Assert.Equal((ToolErrorKind.InvalidArgument, "descriptionOnly"), (exception.Error.Kind, Parameter(exception)));
		Assert.Equal(0, target.Dispatcher.Calls);
	}

	public static TheoryData<RecordDropdownItem[]?, ToolErrorKind, string> InvalidDropdownItems => new()
	{
		{ null, ToolErrorKind.InvalidArgument, "items" },
		{ [new RecordDropdownItem("1:2", "Two")], ToolErrorKind.InvalidArgument, "items[0].value" },
		{ [new RecordDropdownItem(null!, "Missing")], ToolErrorKind.InvalidArgument, "items[0].value" },
		{ [new RecordDropdownItem("1\t", "Tab")], ToolErrorKind.InvalidArgument, "items[0].value" },
		{ [new RecordDropdownItem(new string('7', 1025))], ToolErrorKind.InvalidArgument, "items[0].value" },
		{ [new RecordDropdownItem("1", "Line\nbreak")], ToolErrorKind.InvalidArgument, "items[0].description" },
		{
			[new RecordDropdownItem("A", "Upper"), new RecordDropdownItem("a", "Lower")], ToolErrorKind.InvalidArgument,
			"items[1].value"
		},
		{
			[new RecordDropdownItem("", "Unknown"), new RecordDropdownItem("Max-É"), new RecordDropdownItem("mAX-É")],
			ToolErrorKind.InvalidArgument, "items[2].value"
		},
		{
			[
				.. Enumerable.Range(0, 1025).Select(static index =>
					new RecordDropdownItem(index.ToString(CultureInfo.InvariantCulture)))
			],
			ToolErrorKind.LimitExceeded, "items"
		},
		{
			[
				.. Enumerable.Range(0, 300).Select(static index =>
					new RecordDropdownItem(index.ToString(CultureInfo.InvariantCulture), new string('d', 1000)))
			],
			ToolErrorKind.LimitExceeded, "items"
		}
	};

	[Theory]
	[MemberData(nameof(InvalidDropdownItems))]
	public void SetDropdown_InvalidItems_RefuseBeforeAnyDispatch(RecordDropdownItem[]? items, ToolErrorKind kind,
		string parameter)
	{
		RecordTarget target = new();

		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() =>
			new RecordMutationTools(target.Dispatch).SetDropdown(4, items!, cancellationToken: Token));

		Assert.Equal((kind, ToolHostEffect.NotStarted, parameter),
			(exception.Error.Kind, exception.Error.HostEffect, Parameter(exception)));
		Assert.Equal(0, target.Dispatcher.Calls);
	}

	[Fact]
	public void SetDropdown_EmptyValues_AreStoredAsDescriptionLinesThatCheatEngineReadsLikeLinesWithoutAColon()
	{
		RecordTarget target = new();
		target.Records.Add(Snapshot(4, 0, "Mode"));
		target.Answer(static _ =>
			new RecordLuaDropdowns([new RecordLuaDropdown(4, new RecordDropdown([], 0, false, false, false, false))]));

		// record_get reports a line without a colon as an empty value, so several may appear in one copied list.
		new RecordMutationTools(target.Dispatch).SetDropdown(4,
		[
			new RecordDropdownItem("", "Unknown"), new RecordDropdownItem("", "Other"),
			new RecordDropdownItem(" ", "Blank"), new RecordDropdownItem("1", "On")
		], cancellationToken: Token);

		Assert.Contains("[2] = {\":Unknown\",\":Other\",\" :Blank\",\"1:On\",}", target.LuaSources[0],
			StringComparison.Ordinal);
	}

	[Fact]
	public void DropdownLines_CompareValuesWithOnlyAsciiLettersFolded()
	{
		// Cheat Engine's LowerCase and UpperCase change only A to Z, so É and é are two values to it.
		string[] lines = RecordDropdowns.Lines(
		[
			new RecordDropdownItem("É", "upper"), new RecordDropdownItem("é", "lower"),
			new RecordDropdownItem("Ω"), new RecordDropdownItem("ω")
		], "items");

		Assert.Equal(["É:upper", "é:lower", "Ω:", "ω:"], lines);
		Assert.Equal("max-É ǆ 9", RecordDropdowns.AsciiLowerCase("MAX-É ǆ 9"));
	}

	[Fact]
	public void SetDropdown_AutoAssemblerRecord_RefusesBeforeTheLuaChange()
	{
		RecordTarget target = new();
		target.Records.Add(Snapshot(9, 0, "Patch", variableType: VariableType.AutoAssembler));

		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() =>
			new RecordMutationTools(target.Dispatch).SetDropdown(9, [new RecordDropdownItem("1", "On")],
				cancellationToken: Token));

		Assert.Equal((ToolErrorKind.InvalidState, ToolHostEffect.NotStarted),
			(exception.Error.Kind, exception.Error.HostEffect));
		Assert.Equal(["Tables.GetRecord"], target.Calls);
	}

	[Fact]
	public void SetDropdown_LinkedListRefusedByLua_ReportsItWithoutReadingBack()
	{
		RecordTarget target = new();
		target.Records.Add(Snapshot(4, 0, "Mode"));
		target.Declare<RecordLuaChanged>("invalid_state", "The record uses the dropdown list of another record.",
			"not_started");

		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() =>
			new RecordMutationTools(target.Dispatch).SetDropdown(4, [new RecordDropdownItem("1", "On")],
				cancellationToken: Token));

		Assert.Equal((ToolErrorKind.InvalidState, ToolHostEffect.NotStarted),
			(exception.Error.Kind, exception.Error.HostEffect));
		Assert.Equal(["Tables.GetRecord", "Lua.Execute"], target.Calls);
	}

	[Fact]
	public void SetDropdown_ToolMetadata_IsAnUngatedDestructiveIdempotentShortMutation()
	{
		MethodInfo method = typeof(RecordMutationTools).GetMethod(nameof(RecordMutationTools.SetDropdown))!;
		McpServerToolAttribute tool = method.GetCustomAttribute<McpServerToolAttribute>()!;

		Assert.Equal((CheatEngineToolNames.RecordSetDropdown, "Set address-list record dropdown"),
			(tool.Name, tool.Title));
		Assert.Equal((false, true, true, false), (tool.ReadOnly, tool.Destructive, tool.Idempotent, tool.OpenWorld));
		McpMetaAttribute meta = Assert.Single(method.GetCustomAttributes<McpMetaAttribute>(),
			static attribute => attribute.Name == McpDispatchClass.MetaKey);
		Assert.Equal(McpDispatchClass.Short, JsonNode.Parse(meta.JsonValue)!.GetValue<string>());
		Assert.Empty(method.GetCustomAttributes<RequiresFeatureAttribute>());
	}

	private static string Described(Type type, string property)
	{
		return type.GetProperty(property)!.GetCustomAttribute<DescriptionAttribute>()!.Description;
	}

	private static string ToolDescription(Type type, string method)
	{
		return type.GetMethods().Single(candidate => candidate.Name == method &&
													 candidate.GetCustomAttribute<McpServerToolAttribute>() is not null)
			.GetCustomAttribute<DescriptionAttribute>()!.Description;
	}

	private static string? Parameter(CheatEngineToolException exception)
	{
		return exception.Error.Details?.GetProperty("parameter").GetString();
	}

	/// <summary>A record with immediate children, as the Client copies one.</summary>
	private static MemoryRecordSnapshot Parent(int id, string description, VariableType variableType)
	{
		return new MemoryRecordSnapshot(new MemoryRecordId(id), 0,
			new MemoryRecordContentSnapshot(description, "game.exe+10", "0", variableType),
			new MemoryRecordStateSnapshot(new Address(0x401000), false, 2));
	}

	private static MemoryRecordSnapshot Snapshot(int id, int index, string description, string address = "game.exe+10",
		string value = "0", VariableType variableType = VariableType.Dword, int offsetCount = 0)
	{
		return new MemoryRecordSnapshot(new MemoryRecordId(id), index,
			new MemoryRecordContentSnapshot(description, address, value, variableType, null, offsetCount),
			new MemoryRecordStateSnapshot(new Address(0x401000)));
	}

	/// <summary>The description, address and type a fixed create body receives as its first three arguments.</summary>
	[GeneratedRegex("""local a = \{ n = 3, \[1\] = "([^"]*)", \[2\] = "([^"]*)", \[3\] = (\d+) \}""")]
	private static partial Regex CreateArguments();

	private sealed class RecordTarget
	{
		private readonly Dictionary<Type, Func<string, object>> _answers = [];
		private readonly Dictionary<Type, LuaScriptError> _errors = [];
		private int _luaCreates;

		internal RecordTarget(McpFeatureOptions? features = null)
		{
			ITableClient tables = ClientTestDouble.Create<ITableClient>(Tables);
			ILuaClient lua = ClientTestDouble.Create<ILuaClient>(Lua);
			Client = ClientTestDouble.Client(Dispatcher.Dispatcher, CancellationToken.None,
				(nameof(ICheatEngineClient.Tables), tables), (nameof(ICheatEngineClient.Lua), lua));
			IOptions<McpExecutionOptions> execution = Options.Create(new McpExecutionOptions());
			Dispatch = new ToolDispatch(Client, new McpFeatureGate(Options.Create(features ?? new McpFeatureOptions())),
				execution, new DispatchStatistics(execution), TimeProvider.System, new RecordingLogger<ToolDispatch>(),
				new PluginFixedLuaExecutor(Client));
		}

		internal RecordingDispatcher Dispatcher
		{
			get;
		} = new();

		internal ICheatEngineClient Client
		{
			get;
		}

		internal ToolDispatch Dispatch
		{
			get;
		}

		internal List<MemoryRecordSnapshot> Records
		{
			get;
		} = [];

		internal List<string> Calls
		{
			get;
		} = [];

		internal List<int> Deleted
		{
			get;
		} = [];

		internal List<string> LuaSources
		{
			get;
		} = [];

		internal List<MemoryRecordDefinition> Definitions
		{
			get;
		} = [];

		internal List<MemoryRecordUpdate> Updates
		{
			get;
		} = [];

		internal List<MemoryRecordSearch> Searches
		{
			get;
		} = [];

		internal List<(int Child, int? Parent)> Parents
		{
			get;
		} = [];

		/// <summary>Ids the Client reports as handed out before a later table load.</summary>
		internal HashSet<int> StaleIds
		{
			get;
		} = [];

		/// <summary>The one-based number of the fixed Lua create that Cheat Engine refuses; 0 for none.</summary>
		internal int RefusedCreate
		{
			get;
			init;
		}

		/// <summary>
		///     A value text that Cheat Engine refuses to write, as its SetValue refuses text it cannot convert.
		/// </summary>
		internal string? RefusedValue
		{
			get;
			init;
		}

		internal MemoryRecordHierarchySnapshot Hierarchy
		{
			get;
			set;
		}

		internal int HierarchyRoot
		{
			get;
			private set;
		}

		internal MemoryRecordHierarchyRequest HierarchyRequest
		{
			get;
			private set;
		}

		private object? Tables(MethodInfo method, object?[]? arguments)
		{
			object?[] values = arguments ?? throw new ArgumentNullException(nameof(arguments));
			Calls.Add("Tables." + method.Name);
			return method.Name switch
			{
				nameof(ITableClient.GetRecordCount) => Records.Count,
				nameof(ITableClient.GetRecordAt) => Records[(int) values[0]!],
				nameof(ITableClient.GetRecord) => Get(new MemoryRecordId(((MemoryRecordId) values[0]!).Value)),
				nameof(ITableClient.TryGetRecord) => TryGet(values),
				nameof(ITableClient.GetHierarchy) => Tree((MemoryRecordId) values[0]!,
					(MemoryRecordHierarchyRequest) values[1]!),
				nameof(ITableClient.Create) => Add((MemoryRecordDefinition) values[0]!),
				nameof(ITableClient.Update) => Update((MemoryRecordId) values[0]!, (MemoryRecordUpdate) values[1]!),
				nameof(ITableClient.SetParent) => Parent((MemoryRecordId) values[0]!, (MemoryRecordId?) values[1]),
				nameof(ITableClient.Delete) => Delete((MemoryRecordId) values[0]!),
				nameof(ITableClient.Find) => Search((MemoryRecordSearch) values[0]!),
				_ => throw new NotSupportedException($"Unexpected table call {method.Name}.")
			};
		}

		/// <summary>
		///     Answers every Lua operation whose result is <typeparamref name="T" />, given its complete source.
		/// </summary>
		internal void Answer<T>(Func<string, T> answer) where T : notnull
		{
			_answers[typeof(T)] = source => answer(source);
		}

		/// <summary>
		///     Makes every Lua operation whose result is <typeparamref name="T" /> declare an <c>mcp_error</c>.
		/// </summary>
		internal void Declare<T>(string kind, string message, string hostEffect)
		{
			_errors[typeof(T)] = new LuaScriptError(kind, message, hostEffect, null);
		}

		private object? Lua(MethodInfo method, object?[]? arguments)
		{
			object?[] values = arguments ?? throw new ArgumentNullException(nameof(arguments));
			Calls.Add("Lua." + method.Name);
			string source = (string) values[0]!.GetType().GetProperty("Source")!.GetValue(values[0])!;
			LuaSources.Add(source);
			Type resultType = method.GetGenericArguments()[1];
			Type valueType = resultType.GetGenericArguments()[0];
			if (source.EndsWith(RecordLuaScripts.CreateRecord, StringComparison.Ordinal))
			{
				return ++_luaCreates == RefusedCreate
					? Activator.CreateInstance(resultType, null,
						new LuaScriptError("host_refused", "Cheat Engine refused the new record.", "not_started", null),
						0)
					: Activator.CreateInstance(resultType, CreateFromLua(source), null, 0);
			}

			if (_errors.TryGetValue(valueType, out LuaScriptError? error))
			{
				return Activator.CreateInstance(resultType, null, error, 0);
			}

			object value = _answers.TryGetValue(valueType, out Func<string, object>? answer)
				? answer(source)
				: new RecordLuaChanged(7);
			return Activator.CreateInstance(resultType, value, null, 0);
		}

		/// <summary>
		///     Adds the record a fixed create body makes: its description, address and type, and no value.
		/// </summary>
		private RecordLuaChanged CreateFromLua(string source)
		{
			Match arguments = CreateArguments().Match(source);
			Assert.True(arguments.Success, "The create body receives exactly its description, address and type.");
			int id = Records.Count + 1;
			Records.Add(Snapshot(id, Records.Count, arguments.Groups[1].Value, arguments.Groups[2].Value,
				string.Empty,
				(VariableType) int.Parse(arguments.Groups[3].Value, CultureInfo.InvariantCulture)));
			return new RecordLuaChanged(id);
		}

		private bool TryGet(object?[] values)
		{
			MemoryRecordId id = (MemoryRecordId) values[0]!;
			if (StaleIds.Contains(id.Value))
			{
				values[1] = default(MemoryRecordSnapshot);
				values[2] = new CheatEngineFailure(CheatEngineFailureKind.InvalidState, "Tables.GetRecord",
					"The id was handed out before the last trusted table load.",
					hostEffect: CheatEngineHostEffect.NotStarted);
				return false;
			}

			int index = Records.FindIndex(record => record.Id == id);
			if (index < 0)
			{
				values[1] = default(MemoryRecordSnapshot);
				values[2] = new CheatEngineFailure(CheatEngineFailureKind.NotFound, "Tables.GetRecord",
					"No record has that id.", hostEffect: CheatEngineHostEffect.NotStarted);
				return false;
			}

			values[1] = Records[index];
			values[2] = default(CheatEngineFailure);
			return true;
		}

		private MemoryRecordSnapshot Add(MemoryRecordDefinition definition)
		{
			Definitions.Add(definition);
			int id = Records.Count + 1;
			MemoryRecordSnapshot snapshot = Snapshot(id, Records.Count, definition.Description,
				definition.AddressExpression, definition.Value, definition.VariableType);
			Records.Add(snapshot);
			return snapshot;
		}

		private MemoryRecordHierarchySnapshot Tree(MemoryRecordId root, MemoryRecordHierarchyRequest request)
		{
			HierarchyRoot = root.Value;
			HierarchyRequest = request;
			return Hierarchy;
		}

		private MemoryRecordSnapshot Get(MemoryRecordId id)
		{
			return Records.Single(record => record.Id == id);
		}

		/// <summary>Compares the stored type as the Client's search does; the tests need no other predicate.</summary>
		private ImmutableArray<MemoryRecordSnapshot> Search(MemoryRecordSearch search)
		{
			Searches.Add(search);
			return
			[
				.. Records.Where(record =>
					(search.VariableType is not { } type || record.Content.VariableType == type) &&
					(search.DescriptionContains is not { } text ||
					 record.Content.Description.Contains(text, StringComparison.OrdinalIgnoreCase)))
			];
		}

		private MemoryRecordSnapshot Update(MemoryRecordId id, MemoryRecordUpdate update)
		{
			Updates.Add(update);
			if (update.Value is { } refused && refused == RefusedValue)
			{
				throw new CheatEngineFailure(CheatEngineFailureKind.OperationRejected, "Tables.Update",
					"Cheat Engine refused the value.", hostEffect: CheatEngineHostEffect.Started).ToException();
			}

			MemoryRecordSnapshot old = Get(id);
			MemoryRecordSnapshot changed = Snapshot(id.Value, old.Index, update.Description ?? old.Content.Description,
				update.AddressExpression ?? old.Content.AddressExpression, update.Value ?? old.Content.Value,
				update.VariableType ?? old.Content.VariableType);
			Records[Records.FindIndex(record => record.Id == id)] = changed;
			return changed;
		}

		private MemoryRecordSnapshot Parent(MemoryRecordId child, MemoryRecordId? parent)
		{
			Parents.Add((child.Value, parent?.Value));
			return Get(child);
		}

		private object? Delete(MemoryRecordId id)
		{
			Deleted.Add(id.Value);
			Records.RemoveAll(record => record.Id == id);
			return null;
		}
	}
}
