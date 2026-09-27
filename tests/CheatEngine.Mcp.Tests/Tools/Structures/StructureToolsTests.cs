using System.Text.Json;

using CheatEngine.Mcp.Core.Contract;
using CheatEngine.Mcp.Core.Values;
using CheatEngine.Mcp.Tests.Support;
using CheatEngine.Mcp.Tools.Structures;

using Microsoft.Extensions.DependencyInjection;

using ModelContextProtocol.Protocol;

namespace CheatEngine.Mcp.Tests.Tools.Structures;

/// <summary>The structure tools over a Client double: argument refusals, script arguments and result mapping.</summary>
public sealed class StructureToolsTests
{
	private const ulong Base = 0x7FF6A1B2C000;

	private static CancellationToken Token => TestContext.Current.CancellationToken;

	public static TheoryData<StructureElementSpec, ToolErrorKind, string> InvalidSpecs => new()
	{
		{
			new StructureElementSpec("0", "name", McpValueType.String), ToolErrorKind.InvalidArgument,
			"elements[0].byteSize"
		},
		{
			new StructureElementSpec("0", "hp", McpValueType.Int32, ByteSize: 4), ToolErrorKind.InvalidArgument,
			"elements[0].byteSize"
		},
		{
			new StructureElementSpec("0", "raw", McpValueType.Bytes, ByteSize: 65537), ToolErrorKind.LimitExceeded,
			"elements[0].byteSize"
		},
		{
			new StructureElementSpec("0", "hp", McpValueType.Int32, ChildStructure: "Other"),
			ToolErrorKind.InvalidArgument, "elements[0].childStructure"
		},
		{
			new StructureElementSpec("0", "p", McpValueType.Pointer, ChildStructureStart: "8"),
			ToolErrorKind.InvalidArgument, "elements[0].childStructureStart"
		},
		{
			new StructureElementSpec("0", "hp", McpValueType.UInt32, StructureDisplay.Signed),
			ToolErrorKind.InvalidArgument, "elements[0].display"
		},
		{
			new StructureElementSpec("G", "hp", McpValueType.Int32), ToolErrorKind.InvalidArgument, "elements[0].offset"
		},
		{
			new StructureElementSpec("100000000", "hp", McpValueType.Int32), ToolErrorKind.InvalidArgument,
			"elements[0].offset"
		},
		{
			new StructureElementSpec("0", new string('n', 257), McpValueType.Int32), ToolErrorKind.LimitExceeded,
			"elements[0].name"
		}
	};

	[Fact]
	public void List_LimitAboveMaximum_IsLimitExceededWithoutDispatch()
	{
		StructureToolHarness harness = new();

		CheatEngineToolException exception =
			Assert.Throws<CheatEngineToolException>(() =>
				harness.Structures.List(limit: 1001, cancellationToken: Token));

		Assert.Equal(ToolErrorKind.LimitExceeded, exception.Error.Kind);
		Assert.Equal(0, harness.Dispatcher.Calls);
	}

	[Fact]
	public void List_Filter_ReachesTheScriptAsArgumentsAndItsPageIsReturned()
	{
		StructureToolHarness harness = new()
		{
			Lua = static call => call.Runs(StructureLuaScripts.List)
				? """{"structures":[{"name":"Player","size":16,"elementCount":2}],"total":3,"nextOffset":1,"truncated":false}"""
				: null
		};

		StructurePage page = harness.Structures.List("Play", 0, 1, Token);

		StructureLuaCall call = Assert.Single(harness.LuaCalls);
		Assert.Contains("[1] = \"Play\", [2] = 0, [3] = 1, [4] = 65536", call.Arguments, StringComparison.Ordinal);
		Assert.Equal(new StructureSummary("Player", 16, 2), Assert.Single(page.Structures));
		Assert.Equal((3, 1, false), (page.Total, page.NextOffset, page.Truncated));
	}

	[Fact]
	public void Get_CheatEngineElementFields_MapToContractTypesAndOffsets()
	{
		StructureToolHarness harness = new()
		{
			Lua = static call => StructureToolHarness.Definition("Player", 5,
				StructureToolHarness.Element(0, 0, "vtable", 12, "dtUnsignedInteger", 8, "PlayerVtable"),
				StructureToolHarness.Element(1, 8, "health", 2, "dtSignedInteger", 4),
				StructureToolHarness.Element(2, 12, "flags", 2, "dtHexadecimal", 4),
				StructureToolHarness.Element(3, 16, "name", 6, "dtUnsignedInteger", 32),
				StructureToolHarness.Element(4, -8, "header", 13, "dtUnsignedInteger", 4))
		};

		StructureDefinition definition =
			harness.Structures.Get("Player", format: ResultFormat.Detailed, cancellationToken: Token);

		Assert.Contains("[6] = true", Assert.Single(harness.LuaCalls).Arguments, StringComparison.Ordinal);
		Assert.Equal(5, definition.Total);
		Assert.Null(definition.NextOffset);
		StructureElement[] elements = definition.Elements;
		Assert.Equal((StructureElementType.Pointer, (StructureDisplay?) null, "PlayerVtable", "8"),
			(elements[0].ValueType, elements[0].Display, elements[0].ChildStructure, elements[0].ChildStructureStart));
		Assert.Equal((StructureElementType.Int32, StructureDisplay.Signed, "8"),
			(elements[1].ValueType, elements[1].Display!.Value, elements[1].Offset));
		Assert.Equal((StructureElementType.UInt32, StructureDisplay.Hex, "C"),
			(elements[2].ValueType, elements[2].Display!.Value, elements[2].Offset));
		Assert.Equal((StructureElementType.String, 32), (elements[3].ValueType, elements[3].ByteSize));
		Assert.Equal((StructureElementType.Custom, "-8"), (elements[4].ValueType, elements[4].Offset));
	}

	[Theory]
	[MemberData(nameof(InvalidSpecs))]
	public void Create_InvalidElement_IsRefusedWithoutDispatch(StructureElementSpec spec, ToolErrorKind kind,
		string parameter)
	{
		StructureToolHarness harness = new();

		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() =>
			harness.Structures.Create("Player", [spec], cancellationToken: Token));

		Assert.Equal(kind, exception.Error.Kind);
		Assert.Equal(ToolHostEffect.NotStarted, exception.Error.HostEffect);
		Assert.Equal(parameter, exception.Error.Details!.Value.GetProperty("parameter").GetString());
		Assert.Equal(0, harness.Dispatcher.Calls);
	}

	[Fact]
	public void Create_TwoSources_IsInvalidArgumentWithoutDispatch()
	{
		StructureToolHarness harness = new();

		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() =>
			harness.Structures.Create("Player", [], "Enemy", cancellationToken: Token));

		Assert.Equal(ToolErrorKind.InvalidArgument, exception.Error.Kind);
		Assert.Equal(0, harness.Dispatcher.Calls);
	}

	[Fact]
	public void Create_Elements_AreEncodedAsVartypeDisplayAndByteSize()
	{
		StructureToolHarness harness = new()
		{
			Lua = static call => call.Runs(StructureLuaScripts.CreateFromElements)
				? """{"name":"Player","size":40,"elementCount":3}"""
				: null
		};

		StructureSummary created = harness.Structures.Create("Player",
		[
			new StructureElementSpec("-8", "health", McpValueType.Int32),
			new StructureElementSpec("10", "name", McpValueType.WString, ByteSize: 32),
			new StructureElementSpec("0", "next", McpValueType.Pointer, ChildStructure: "Player",
				ChildStructureStart: "4")
		], @internal: true, cancellationToken: Token);

		string arguments = Assert.Single(harness.LuaCalls).Arguments;
		Assert.Contains("[2] = true", arguments, StringComparison.Ordinal);
		Assert.Contains("{-8,\"health\",2,\"dtSignedInteger\",nil,nil,nil,}", arguments, StringComparison.Ordinal);
		Assert.Contains("{16,\"name\",7,nil,32,nil,nil,}", arguments, StringComparison.Ordinal);
		Assert.Contains("{0,\"next\",12,nil,nil,\"Player\",4,}", arguments, StringComparison.Ordinal);
		Assert.Equal(new StructureSummary("Player", 40, 3), created);
	}

	[Theory]
	[InlineData("invalid_state", ToolErrorKind.InvalidState)]
	[InlineData("not_found", ToolErrorKind.NotFound)]
	public void Create_DeclaredRefusal_IsTheContractErrorNotStarted(string declared, ToolErrorKind kind)
	{
		StructureToolHarness harness = new()
		{
			Lua = _ => StructureToolHarness.Declared(declared)
		};

		CheatEngineToolException exception =
			Assert.Throws<CheatEngineToolException>(() =>
				harness.Structures.Create("Player", cloneFrom: "Enemy", cancellationToken: Token));

		Assert.Equal((kind, ToolHostEffect.NotStarted), (exception.Error.Kind, exception.Error.HostEffect));
		Assert.True(Assert.Single(harness.LuaCalls).Runs(StructureLuaScripts.CreateClone));
	}

	[Fact]
	public void UpdateElements_UpdateThatChangesNothing_IsInvalidArgumentWithoutDispatch()
	{
		StructureToolHarness harness = new();

		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() =>
			harness.Elements.UpdateElements("Player", [new StructureElementUpdate(0)], Token));

		Assert.Equal(ToolErrorKind.InvalidArgument, exception.Error.Kind);
		Assert.Equal(0, harness.Dispatcher.Calls);
	}

	[Fact]
	public void UpdateElements_DisplayWithoutType_IsCheckedByTheScriptAgainstTheCurrentType()
	{
		StructureToolHarness harness = new()
		{
			Lua = static _ => """{"name":"Player","size":8,"elementCount":2,"indices":[1],"applied":1}"""
		};

		StructureChange change = harness.Elements.UpdateElements("Player",
			[new StructureElementUpdate(1, Name: "flags", Display: StructureDisplay.Hex)], Token);

		Assert.Contains("{1,nil,\"flags\",nil,\"dtHexadecimal\",nil,}", Assert.Single(harness.LuaCalls).Arguments,
			StringComparison.Ordinal);
		Assert.Equal([1], change.Indices);
	}

	[Fact]
	public void UpdateElements_ScriptStoppedAtARefusal_IsPartialEffectWithTheBatchDetails()
	{
		StructureToolHarness harness = new()
		{
			Lua = static _ =>
				"""{"name":"Player","size":8,"elementCount":2,"indices":[0],"applied":1,"failedIndex":1,"failure":"read only"}"""
		};

		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() =>
			harness.Elements.UpdateElements("Player",
				[new StructureElementUpdate(0, Name: "a"), new StructureElementUpdate(1, Name: "b")], Token));

		Assert.Equal((ToolErrorKind.PartialEffect, ToolHostEffect.Started, false),
			(exception.Error.Kind, exception.Error.HostEffect, exception.Error.Retryable));
		JsonElement details = exception.Error.Details!.Value;
		Assert.Equal((1, 1, "read only"), (details.GetProperty("applied").GetInt32(),
			details.GetProperty("failedIndex").GetInt32(), details.GetProperty("failure").GetString()));
	}

	[Fact]
	public void RemoveElements_DuplicateIndex_IsInvalidArgumentWithoutDispatch()
	{
		StructureToolHarness harness = new();

		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() =>
			harness.Elements.RemoveElements("Player", [2, 2], Token));

		Assert.Equal("indices[1]", exception.Error.Details!.Value.GetProperty("parameter").GetString());
		Assert.Equal(0, harness.Dispatcher.Calls);
	}

	[Fact]
	public void RemoveElements_Indices_GoHighestFirstAndARefusalNamesTheCallersPosition()
	{
		StructureToolHarness harness = new()
		{
			Lua = static _ =>
				"""{"name":"Player","size":8,"elementCount":2,"indices":[5],"applied":1,"failedIndex":1,"failure":"busy"}"""
		};

		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() =>
			harness.Elements.RemoveElements("Player", [1, 5, 3], Token));

		Assert.Contains("[2] = {5,3,1,}", Assert.Single(harness.LuaCalls).Arguments, StringComparison.Ordinal);
		Assert.Equal(ToolErrorKind.PartialEffect, exception.Error.Kind);
		Assert.Equal(2, exception.Error.Details!.Value.GetProperty("failedIndex").GetInt32());
	}

	[Theory]
	[InlineData(0, "0", ToolErrorKind.InvalidArgument, "size")]
	[InlineData(65537, "0", ToolErrorKind.LimitExceeded, "size")]
	[InlineData(16, "-8", ToolErrorKind.InvalidArgument, "offset")]
	public void Autoguess_InvalidRange_IsRefusedWithoutDispatch(int size, string offset, ToolErrorKind kind,
		string parameter)
	{
		StructureToolHarness harness = new();

		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() =>
			harness.Structures.Autoguess("Player", "game.exe+10", offset, size, cancellationToken: Token));

		Assert.Equal(kind, exception.Error.Kind);
		Assert.Equal(parameter, exception.Error.Details!.Value.GetProperty("parameter").GetString());
		Assert.Equal(0, harness.Dispatcher.Calls);
	}

	[Fact]
	public void Autoguess_Address_IsResolvedByTheClientAndPassedAsHexText()
	{
		StructureToolHarness harness = new()
		{
			Lua = static _ => """{"name":"Player","size":1024,"elementCount":40}"""
		};
		harness.Symbols["game.exe+10"] = Base;

		StructureSummary summary = harness.Structures.Autoguess("Player", "game.exe+10", "10", 1024, false, Token);

		Assert.Equal(["game.exe+10"], harness.Resolutions);
		Assert.Contains("[2] = \"0x7FF6A1B2C000\", [3] = 16, [4] = 1024, [5] = false",
			Assert.Single(harness.LuaCalls).Arguments, StringComparison.Ordinal);
		Assert.Equal(1, harness.Dispatcher.Calls);
		Assert.Equal(40, summary.ElementCount);
	}

	[Fact]
	public void Autoguess_UnresolvableAddress_IsNotFoundBeforeAnyScript()
	{
		StructureToolHarness harness = new();

		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() =>
			harness.Structures.Autoguess("Player", "missing.dll+10", cancellationToken: Token));

		Assert.Equal((ToolErrorKind.NotFound, ToolHostEffect.NotStarted),
			(exception.Error.Kind, exception.Error.HostEffect));
		Assert.Empty(harness.LuaCalls);
	}

	[Fact]
	public void FillFromDotNet_TargetHalted_IsBusyNotStarted()
	{
		StructureToolHarness harness = new()
		{
			Lua = static _ => StructureToolHarness.Declared("busy")
		};

		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() =>
			harness.Structures.FillFromDotNet("Player", "1000", cancellationToken: Token));

		Assert.Equal((ToolErrorKind.Busy, ToolHostEffect.NotStarted),
			(exception.Error.Kind, exception.Error.HostEffect));
		Assert.Contains("[2] = \"0x1000\", [3] = false, [4] = true", harness.LuaCalls[0].Arguments,
			StringComparison.Ordinal);
	}

	[Fact]
	public void GetPdbLayout_Fields_MapOffsetsAndReportedTypes()
	{
		StructureToolHarness harness = new()
		{
			Lua = static _ =>
				"""{"found":true,"elements":[{"offset":16,"name":"Peb","vartype":12},{"offset":24,"name":"Flags"}],"truncated":true}"""
		};

		PdbLayout layout = harness.Structures.GetPdbLayout("_EPROCESS", 2, Token);

		Assert.Equal("_EPROCESS", layout.TypeName);
		Assert.True(layout.Found && layout.Truncated);
		Assert.Equal(new PdbLayoutElement("10", "Peb", StructureElementType.Pointer), layout.Elements[0]);
		Assert.Equal(new PdbLayoutElement("18", "Flags", null), layout.Elements[1]);
	}

	[Theory]
	[InlineData(17, null, null, ToolErrorKind.LimitExceeded, "addresses")]
	[InlineData(0, null, null, ToolErrorKind.InvalidArgument, "addresses")]
	[InlineData(1, "10", "8", ToolErrorKind.InvalidArgument, "toOffset")]
	public void Read_InvalidArguments_AreRefusedWithoutDispatch(int addresses, string? from, string? to,
		ToolErrorKind kind, string parameter)
	{
		StructureToolHarness harness = new();

		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() =>
			harness.Values.Read("Player", [.. Enumerable.Repeat("1000", addresses)], from, to,
				cancellationToken: Token));

		Assert.Equal(kind, exception.Error.Kind);
		Assert.Equal(parameter, exception.Error.Details!.Value.GetProperty("parameter").GetString());
		Assert.Equal(0, harness.Dispatcher.Calls);
	}

	[Fact]
	public void Read_TwoAddresses_DecodesEveryElementAndNullsWhatIsUnreadable()
	{
		StructureToolHarness harness = new()
		{
			Lua = static _ => StructureToolHarness.Definition("Player", 4,
				StructureToolHarness.Element(0, 0, "health", 2, "dtSignedInteger", 4),
				StructureToolHarness.Element(1, 4, "speed", 4, "dtUnsignedInteger", 4),
				StructureToolHarness.Element(2, 8, "owner", 12, "dtUnsignedInteger", 8),
				StructureToolHarness.Element(3, 16, "name", 6, "dtUnsignedInteger", 8))
		};
		harness.Map(0x1000, 0x9C, 0xFF, 0xFF, 0xFF, 0x00, 0x00, 0xC0, 0x3F);
		harness.Map(0x1008, BitConverter.GetBytes(Base));
		harness.Map(0x1010, "Hero\0zzz"u8.ToArray());
		harness.Map(0x2000, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00);

		StructureReadResult result = harness.Values.Read("Player", ["1000", "2000"], format: ResultFormat.Detailed,
			cancellationToken: Token);

		Assert.Equal([new StructureReadColumn("1000", 24), new StructureReadColumn("2000", 6)], result.Columns);
		Assert.Equal([(0x1000UL, 24), (0x2000UL, 24)], harness.Reads);
		Assert.Equal(["-100", "1"], result.Elements[0].Values);
		Assert.Equal(["1.5", null], result.Elements[1].Values);
		Assert.Equal(["7FF6A1B2C000", null], result.Elements[2].Values);
		Assert.Equal(["Hero", null], result.Elements[3].Values);
		Assert.Equal(["9C FF FF FF", "01 00 00 00"], result.Elements[0].Raw!);
		Assert.Equal(StructureDisplay.Signed, result.Elements[0].Display);
		Assert.Single(harness.LuaCalls);
	}

	[Fact]
	public void Read_BitField_IsFormattedByCheatEngineInTheSameDispatch()
	{
		StructureToolHarness harness = new()
		{
			Lua = static call => call.Runs(StructureLuaScripts.FormattedValues)
				? """{"values":[["3",null]]}"""
				: StructureToolHarness.Definition("Player", 1,
					StructureToolHarness.Element(4, 32, "bits", 9, "dtUnsignedInteger", 1))
		};

		StructureReadResult result = harness.Values.Read("Player", ["1000", "2000"], cancellationToken: Token);

		StructureReadRow row = Assert.Single(result.Elements);
		Assert.Equal((StructureElementType.Binary, "20"), (row.ValueType, row.Offset));
		Assert.Equal(["3", null], row.Values);
		Assert.Contains("[2] = {4,}, [3] = {0x1000,0x2000,}", harness.LuaCalls[1].Arguments, StringComparison.Ordinal);
		Assert.Equal(1, harness.Dispatcher.Calls);
	}

	[Fact]
	public void Read_PageSpanningMoreThan64KiB_IsLimitExceeded()
	{
		StructureToolHarness harness = new()
		{
			Lua = static _ => StructureToolHarness.Definition("Player", 2,
				StructureToolHarness.Element(0, 0, "first", 2, null, 4),
				StructureToolHarness.Element(1, 0x10000, "far", 2, null, 4))
		};

		CheatEngineToolException exception =
			Assert.Throws<CheatEngineToolException>(() =>
				harness.Values.Read("Player", ["1000"], cancellationToken: Token));

		Assert.Equal((ToolErrorKind.LimitExceeded, ToolHostEffect.NotStarted),
			(exception.Error.Kind, exception.Error.HostEffect));
		Assert.Empty(harness.Reads);
	}

	[Fact]
	public void WriteElement_Int32_WritesTheEncodedBytesAtTheElementAndReadsThemBack()
	{
		StructureToolHarness harness = new()
		{
			Lua = static _ =>
				$$"""{"name":"Player","element":{{StructureToolHarness.Element(1, 16, "health", 2, "dtSignedInteger", 4)}}}"""
		};

		StructureWriteResult result = harness.Values.WriteElement("Player", "1000", 1, "-100", Token);

		(ulong address, byte[] bytes) = Assert.Single(harness.Writes);
		Assert.Equal((0x1010UL, "9C FF FF FF"), (address, HexFormat.Bytes(bytes)));
		Assert.Equal(new StructureWriteResult("1010", 4, "-100"), result);
		Assert.Contains("[1] = \"Player\", [2] = 1", Assert.Single(harness.LuaCalls).Arguments,
			StringComparison.Ordinal);
	}

	[Fact]
	public void WriteElement_ValueThatDoesNotFitTheType_IsInvalidArgumentWithoutWrite()
	{
		StructureToolHarness harness = new()
		{
			Lua = static _ =>
				$$"""{"name":"Player","element":{{StructureToolHarness.Element(1, 16, "flags", 0, "dtUnsignedInteger", 1)}}}"""
		};

		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() =>
			harness.Values.WriteElement("Player", "1000", 1, "300", Token));

		Assert.Equal((ToolErrorKind.InvalidArgument, ToolHostEffect.NotStarted),
			(exception.Error.Kind, exception.Error.HostEffect));
		Assert.Empty(harness.Writes);
	}

	[Fact]
	public void WriteElement_CustomElement_IsWrittenAndReadBackByCheatEngine()
	{
		StructureToolHarness harness = new()
		{
			Lua = static call => call.Runs(StructureLuaScripts.SetValue)
				? """{"value":"7"}"""
				: $$"""{"name":"Player","element":{{StructureToolHarness.Element(2, 8, "kind", 13, null, 4)}}}"""
		};

		StructureWriteResult result = harness.Values.WriteElement("Player", "1000", 2, "7", Token);

		Assert.Equal(new StructureWriteResult("1008", 4, "7"), result);
		Assert.Contains("[3] = 0x1000, [4] = \"7\"", harness.LuaCalls[1].Arguments, StringComparison.Ordinal);
		Assert.Empty(harness.Writes);
	}

	[Theory]
	[InlineData(16, "Player", 4, StructureCompareFormat.Auto, StructureCompareMode.All, 1, "size")]
	[InlineData(null, null, 4, StructureCompareFormat.Auto, StructureCompareMode.All, 1, "size")]
	[InlineData(16, null, 3, StructureCompareFormat.Auto, StructureCompareMode.All, 1, "granularity")]
	[InlineData(16, null, 2, StructureCompareFormat.Float, StructureCompareMode.All, 1, "interpretAs")]
	[InlineData(16, null, 4, StructureCompareFormat.Auto, StructureCompareMode.Discriminate, 0, "groupB")]
	[InlineData(16385, null, 4, StructureCompareFormat.Auto, StructureCompareMode.All, 1, "size")]
	public void Compare_InvalidArguments_AreRefusedWithoutDispatch(int? size, string? structureName,
		int granularity, StructureCompareFormat format, StructureCompareMode mode, int groupB, string parameter)
	{
		StructureToolHarness harness = new();

		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() =>
			harness.Comparisons.Compare(["1000"], [.. Enumerable.Repeat("2000", groupB)], size, structureName,
				granularity, format, mode, cancellationToken: Token));

		Assert.Equal(parameter, exception.Error.Details!.Value.GetProperty("parameter").GetString());
		Assert.Equal(0, harness.Dispatcher.Calls);
	}

	[Fact]
	public void Compare_RawCells_FindTheFieldThatSeparatesTheGroups()
	{
		StructureToolHarness harness = new();
		harness.Map(0x1000, 9, 0, 0, 0, 1, 0, 0, 0, 5, 0, 0, 0);
		harness.Map(0x2000, 9, 0, 0, 0, 1, 0, 0, 0, 6, 0, 0, 0);
		harness.Map(0x3000, 9, 0, 0, 0, 2, 0, 0, 0, 5, 0, 0, 0);

		StructureComparison discriminators =
			harness.Comparisons.Compare(["1000", "2000"], ["3000"], 12, cancellationToken: Token);
		StructureComparison all = harness.Comparisons.Compare(["1000", "2000"], ["3000"], 12,
			mode: StructureCompareMode.All, cancellationToken: Token);

		StructureCompareRow row = Assert.Single(discriminators.Rows);
		Assert.Equal(("4", 4, StructureFieldClassification.Discriminator), (row.Offset, row.Size, row.Classification));
		Assert.Equal(["1", "1"], row.ValuesA);
		Assert.Equal(["2"], row.ValuesB);
		Assert.Equal(["1000", "2000"], discriminators.GroupA);
		Assert.Equal(
			[
				StructureFieldClassification.Constant, StructureFieldClassification.Discriminator,
				StructureFieldClassification.VariesA
			],
			all.Rows.Select(static compared => compared.Classification));
		Assert.Empty(harness.LuaCalls);
	}

	[Fact]
	public void Compare_AlongAStructure_UsesItsElementsNamesAndTypes()
	{
		StructureToolHarness harness = new()
		{
			Lua = static _ => StructureToolHarness.Definition("Player", 2,
				StructureToolHarness.Element(0, 4, "team", 0, "dtUnsignedInteger", 1),
				StructureToolHarness.Element(1, 8, "health", 4, null, 4))
		};
		harness.Map(0x1004, 1, 0, 0, 0, 0, 0, 0xC8, 0x42);
		harness.Map(0x2004, 2, 0, 0, 0, 0, 0, 0xC8, 0x42);

		StructureComparison comparison = harness.Comparisons.Compare(["1000"], ["2000"], structureName: "Player",
			mode: StructureCompareMode.All, cancellationToken: Token);

		Assert.Equal([(0x1004UL, 8), (0x2004UL, 8)], harness.Reads);
		Assert.Equal(("4", "team", StructureFieldClassification.Discriminator),
			(comparison.Rows[0].Offset, comparison.Rows[0].Name, comparison.Rows[0].Classification));
		Assert.Equal(["100"], comparison.Rows[1].ValuesA);
		Assert.Equal(StructureFieldClassification.Constant, comparison.Rows[1].Classification);
	}

	[Fact]
	public async Task Pipeline_ServedTools_DispatchNothingUntilCalledAndReportErrorsAsTheEnvelope()
	{
		StructureToolHarness harness = new()
		{
			Lua = static _ => StructureToolHarness.Declared("not_found")
		};
		(ServiceProvider root, AsyncServiceScope scope, TestMcpPipeline pipeline) = await harness.ServeAsync();
		try
		{
			Assert.Equal(0, harness.Dispatcher.Calls);

			ToolError limit = TestMcpPipeline.AssertError(
				await pipeline.CallAsync(CheatEngineToolNames.StructureList, """{"limit":1001}"""),
				ToolErrorKind.LimitExceeded);
			Assert.Equal("limit", limit.Details!.Value.GetProperty("parameter").GetString());
			Assert.Equal(0, harness.Dispatcher.Calls);

			ToolError missing = TestMcpPipeline.AssertError(
				await pipeline.CallAsync(CheatEngineToolNames.StructureGet, """{"name":"Player"}"""),
				ToolErrorKind.NotFound);
			Assert.Equal((ToolHostEffect.NotStarted, false, CheatEngineToolNames.StructureGet),
				(missing.HostEffect, missing.Retryable, missing.Operation));
		}
		finally
		{
			await pipeline.DisposeAsync();
			await scope.DisposeAsync();
			await root.DisposeAsync();
		}
	}

	[Fact]
	public async Task Pipeline_PartialUpdate_CarriesTheBatchDetailsInTheEnvelope()
	{
		StructureToolHarness harness = new()
		{
			Lua = static _ =>
				"""{"name":"Player","size":8,"elementCount":2,"indices":[],"applied":0,"failedIndex":0,"failure":"read only"}"""
		};
		(ServiceProvider root, AsyncServiceScope scope, TestMcpPipeline pipeline) = await harness.ServeAsync();
		try
		{
			ToolError error = TestMcpPipeline.AssertError(
				await pipeline.CallAsync(CheatEngineToolNames.StructureUpdateElements,
					"""{"name":"Player","updates":[{"index":0,"name":"hp"}]}"""), ToolErrorKind.PartialEffect);

			Assert.Equal(ToolHostEffect.Unknown, error.HostEffect);
			Assert.Equal(0, error.Details!.Value.GetProperty("failedIndex").GetInt32());
			Assert.Contains("[2] = {{0,nil,\"hp\",nil,nil,nil,},}", Assert.Single(harness.LuaCalls).Arguments,
				StringComparison.Ordinal);
		}
		finally
		{
			await pipeline.DisposeAsync();
			await scope.DisposeAsync();
			await root.DisposeAsync();
		}
	}

	[Fact]
	public async Task Pipeline_Read_ReturnsStructuredContentWithNullCells()
	{
		StructureToolHarness harness = new()
		{
			Lua = static _ => StructureToolHarness.Definition("Player", 1,
				StructureToolHarness.Element(0, 0, "health", 2, "dtSignedInteger", 4))
		};
		harness.Map(0x1000, 5, 0, 0, 0);
		(ServiceProvider root, AsyncServiceScope scope, TestMcpPipeline pipeline) = await harness.ServeAsync();
		try
		{
			CallToolResult result = await pipeline.CallAsync(CheatEngineToolNames.StructureRead,
				"""{"name":"Player","addresses":["1000","2000"]}""");

			Assert.NotEqual(true, result.IsError);
			JsonElement content = result.StructuredContent!.Value;
			JsonElement values = content.GetProperty("elements")[0].GetProperty("values");
			Assert.Equal(("5", JsonValueKind.Null), (values[0].GetString(), values[1].ValueKind));
			Assert.Equal("int32", content.GetProperty("elements")[0].GetProperty("valueType").GetString());
			Assert.False(content.TryGetProperty("nextOffset", out _));
		}
		finally
		{
			await pipeline.DisposeAsync();
			await scope.DisposeAsync();
			await root.DisposeAsync();
		}
	}
}
