using System.Reflection;
using System.Text;

using CheatEngine.Client.Lua;
using CheatEngine.Client.Results;
using CheatEngine.Mcp.Core.Contract;
using CheatEngine.Mcp.Core.Values;
using CheatEngine.Mcp.Tests.Support;
using CheatEngine.Mcp.Tests.Tools.Structures;
using CheatEngine.Mcp.Tools.Structures;
using CheatEngine.SDK.Lua.Calls;
using CheatEngine.SDK.Lua.Runtime;
using CheatEngine.SDK.Lua.State;

namespace CheatEngine.Mcp.Tests.NativeLua;

public sealed partial class NativeLuaToolRuntimeTests
{
	// Cheat Engine's structure API as plain Lua tables: getStructure(index or name), a global list, elements kept
	// sorted by offset on endUpdate, and element indices through the published index property.
	private const string StructureHostStubs = """
	                                          structures = {}
	                                          pdb = {}
	                                          created = 0
	                                          function makeElement(owner)
	                                            local e = {Offset = 0, Name = '', Vartype = 2, DisplayMethod = 'dtUnsignedInteger', bytesize = 4, childStart = 0}
	                                            e.getBytesize = function() return e.bytesize end
	                                            e.setBytesize = function(n) if n > 4096 then error('byte size too large') end e.bytesize = n end
	                                            e.getChildStructStart = function() return e.childStart end
	                                            e.setChildStructStart = function(n) e.childStart = n end
	                                            e.destroy = function()
	                                              for i = #owner.elements, 1, -1 do if owner.elements[i] == e then table.remove(owner.elements, i) end end
	                                            end
	                                            e.getValueFromBase = function(base) if base == 0x2000 then return '??' end return 'v' .. string.format('%X', base) end
	                                            e.setValueFromBase = function(base, value) written = {base = base, value = value} end
	                                            setmetatable(e, {__index = function(t, k)
	                                              if k == 'index' then for i, x in ipairs(owner.elements) do if x == t then return i - 1 end end end
	                                            end})
	                                            return e
	                                          end
	                                          function makeStructure(name)
	                                            local s = {Name = name, elements = {}, Internal = false, updating = 0}
	                                            setmetatable(s, {__index = function(t, k)
	                                              if k == 'Count' then return #t.elements end
	                                              if k == 'Size' then
	                                                local size = 0
	                                                for _, e in ipairs(t.elements) do size = math.max(size, e.Offset + e.bytesize) end
	                                                return size
	                                              end
	                                            end})
	                                            s.getElement = function(i) return s.elements[i + 1] end
	                                            s.addElement = function() local e = makeElement(s); s.elements[#s.elements + 1] = e; return e end
	                                            s.beginUpdate = function() s.updating = s.updating + 1 end
	                                            s.endUpdate = function()
	                                              s.updating = s.updating - 1
	                                              table.sort(s.elements, function(x, y) return x.Offset < y.Offset end)
	                                            end
	                                            s.addToGlobalStructureList = function() structures[#structures + 1] = s end
	                                            s.destroy = function()
	                                              s.destroyed = true
	                                              for i = #structures, 1, -1 do if structures[i] == s then table.remove(structures, i) end end
	                                            end
	                                            s.clone = function(newName)
	                                              local c = makeStructure(newName)
	                                              for _, e in ipairs(s.elements) do
	                                                local copy = c.addElement(); copy.Offset = e.Offset; copy.Name = e.Name; copy.Vartype = e.Vartype
	                                              end
	                                              return c
	                                            end
	                                            s.autoGuess = function(base, offset, size)
	                                              if guessFails then error('guess failed') end
	                                              guessed = {base = base, offset = offset, size = size}
	                                              local e = s.addElement(); e.Offset = offset; e.Name = 'guess'
	                                            end
	                                            s.fillFromDotNetAddress = function(address, rename)
	                                              filled = {address = address, rename = rename}
	                                              if dotnetClass ~= nil then
	                                                local e = s.addElement(); e.Name = 'Vtable'; e.Vartype = 12; e.bytesize = 8
	                                                if rename then s.Name = dotnetClass end
	                                              end
	                                            end
	                                            return s
	                                          end
	                                          function getStructureCount() return #structures end
	                                          function getStructure(x)
	                                            if math.type(x) == 'integer' then
	                                              local s = structures[x + 1]
	                                              if s == nil then return nil, 'Invalid index' end
	                                              return s
	                                            end
	                                            for _, s in ipairs(structures) do if s.Name == x then return s end end
	                                            return nil
	                                          end
	                                          function createStructure(name) created = created + 1; return makeStructure(name) end
	                                          function getStructureElementsFromName(name) return pdb[name] end
	                                          function define(name, fields)
	                                            local s = makeStructure(name)
	                                            for _, f in ipairs(fields) do
	                                              local e = s.addElement(); e.Offset = f[1]; e.Name = f[2]; e.Vartype = f[3]
	                                              e.DisplayMethod = f[4] or 'dtUnsignedInteger'; e.bytesize = f[5] or 4
	                                            end
	                                            s.addToGlobalStructureList()
	                                            return s
	                                          end
	                                          player = define('Player', {{0, 'health', 2, 'dtSignedInteger'}, {8, 'speed', 4}, {16, 'flags', 9, nil, 1}})
	                                          enemy = define('Enemy', {{0, 'health', 2, 'dtSignedInteger'}})
	                                          define('PlayerBase', {})
	                                          """;

	private static readonly int[] StructureRemovedIndices = [2, 0];
	private static readonly int[] StructureFormattedIndices = [2];
	private static readonly ulong[] StructureFormattedBases = [0x1000UL];

	public static TheoryData<string, object?[]> StructureScripts => new()
	{
		{ nameof(StructureLuaScripts.Helpers), [] },
		{ nameof(StructureLuaScripts.List), ["play", 0, 100, 65536] },
		{ nameof(StructureLuaScripts.Elements), ["Player", null, 16, 0, 100, true] },
		{ nameof(StructureLuaScripts.ElementAt), ["Player", 1] },
		{
			nameof(StructureLuaScripts.CreateFromElements),
			["New", true, new object?[] { new object?[] { 0, "a", 2, "dtSignedInteger", null, "Player", 4 } }]
		},
		{ nameof(StructureLuaScripts.CreateClone), ["Copy", false, "Player"] },
		{ nameof(StructureLuaScripts.CreateFromPdb), ["Peb", false, "_PEB", 4096] },
		{ nameof(StructureLuaScripts.Delete), ["Player"] },
		{
			nameof(StructureLuaScripts.AddElements),
			["Player", new object?[] { new object?[] { 4, "b", 6, null, 16, null, null } }]
		},
		{
			nameof(StructureLuaScripts.UpdateElements),
			["Player", new object?[] { new object?[] { 0, 20, "c", null, "dtHexadecimal", null } }]
		},
		{ nameof(StructureLuaScripts.RemoveElements), ["Player", StructureRemovedIndices] },
		{ nameof(StructureLuaScripts.AutoGuess), ["Player", "0x1000", 0, 4096, true] },
		{ nameof(StructureLuaScripts.FillFromDotNet), ["Player", "0x1000", false, true] },
		{ nameof(StructureLuaScripts.PdbLayout), ["_PEB", 1024] },
		{
			nameof(StructureLuaScripts.FormattedValues),
			["Player", StructureFormattedIndices, StructureFormattedBases]
		},
		{ nameof(StructureLuaScripts.SetValue), ["Player", 2, 0x1000UL, "1"] }
	};

	private static CancellationToken StructureToken => TestContext.Current.CancellationToken;

	[Theory]
	[MemberData(nameof(StructureScripts))]
	public void StructureScript_RepresentativeArguments_CompilesAndLoadsNoCode(string script, object?[] arguments)
	{
		string body = (string) typeof(StructureLuaScripts)
			.GetField(script, BindingFlags.Static | BindingFlags.NonPublic)!.GetRawConstantValue()!;
		LuaFixedScriptAssert.NeverLoadsCode(body);
		using RuntimeScope scope = CreateScope();
		using LuaRuntimeOperation operation = AcquireOperation();
		LuaState state = operation.State;
		using LuaFrame frame = new(state);

		LuaStatus status = state.TryLoad(Encoding.UTF8.GetBytes(LuaToolRuntime.BuildSource(body, 100, arguments)),
			"=CheatEngine.Mcp/structure_compile"u8);

		Assert.True(status.IsOk, status.IsOk ? null : LuaError.FromStack(state, status).Message);
	}

	[Fact]
	public void StructureList_StubbedStructures_FiltersIgnoringCaseAndPages()
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs(StructureHostStubs);
		StructureTools tools = NativeStructureHarness().Structures;

		StructurePage filtered = tools.List("PLAYER", 0, 1, StructureToken);
		StructurePage tail = tools.List(null, 2, 10, StructureToken);

		Assert.Equal((2, (int?) 1, false), (filtered.Total, filtered.NextOffset, filtered.Truncated));
		Assert.Equal(new StructureSummary("Player", 17, 3), Assert.Single(filtered.Structures));
		Assert.Equal((3, (int?) null), (tail.Total, tail.NextOffset));
		Assert.Equal("PlayerBase", Assert.Single(tail.Structures).Name);
	}

	[Fact]
	public void StructureGet_StubbedElements_MapTypesAndReportMissingNames()
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs(StructureHostStubs);
		InstallStubs("player.elements[2].ChildStruct = enemy; player.elements[2].childStart = 4");
		StructureTools tools = NativeStructureHarness().Structures;

		StructureDefinition definition = tools.Get("Player", 1, 2, ResultFormat.Detailed, StructureToken);

		Assert.Equal(("Player", 3, false, (int?) null), (definition.Name, definition.Total, definition.Internal,
			definition.NextOffset));
		Assert.Equal(new StructureElement(1, "8", "speed", StructureElementType.Float, null, 4, "Enemy", "4"),
			definition.Elements[0]);
		Assert.Equal((StructureElementType.Binary, 1),
			(definition.Elements[1].ValueType, definition.Elements[1].ByteSize));
		AssertDeclared(() => tools.Get("player", cancellationToken: StructureToken), ToolErrorKind.NotFound,
			ToolHostEffect.NotStarted);
	}

	[Fact]
	public void StructureLookup_NumericNameResolvedByIndex_IsNotMistakenForAnotherStructure()
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs(StructureHostStubs);
		// Cheat Engine builds that read a numeric text as an index return the structure at that index.
		InstallStubs("local byName = getStructure; getStructure = function(x) local i = math.tointeger(tonumber(x)); " +
					 "if i ~= nil then return byName(i) end return byName(x) end");
		StructureTools tools = NativeStructureHarness().Structures;

		AssertDeclared(() => tools.Delete("0", StructureToken), ToolErrorKind.NotFound, ToolHostEffect.NotStarted);
		Assert.Equal(3L, LuaValue("#structures"));
	}

	[Fact]
	public void StructureCreate_EveryMode_CreatesListsAndRefusesBeforeAnyEffect()
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs(StructureHostStubs);
		StructureToolHarness harness = NativeStructureHarness();
		StructureTools tools = harness.Structures;
		InstallStubs(
			"pdb['_PEB'] = {{offset = 0, name = 'InheritedAddressSpace', vartype = 0}, {offset = 16, name = 'Ldr', vartype = 12}}");

		StructureSummary fromElements = tools.Create("Npc",
		[
			new StructureElementSpec("10", "target", McpValueType.Pointer, ChildStructure: "Enemy",
				ChildStructureStart: "4"),
			new StructureElementSpec("0", "health", McpValueType.Int32)
		], @internal: true, cancellationToken: StructureToken);
		StructureSummary clone = tools.Create("PlayerCopy", cloneFrom: "Player", cancellationToken: StructureToken);
		StructureSummary fromPdb = tools.Create("Peb", pdbTypeName: "_PEB", cancellationToken: StructureToken);

		Assert.Equal(new StructureSummary("Npc", 20, 2), fromElements);
		Assert.Equal(("dtSignedInteger", "Enemy", 4L, true), (LuaValue("structures[4].elements[1].DisplayMethod"),
			LuaValue("structures[4].elements[2].ChildStruct.Name"), LuaValue("structures[4].elements[2].childStart"),
			LuaValue("structures[4].Internal")));
		Assert.Equal(3, clone.ElementCount);
		Assert.Equal(new StructureSummary("Peb", 20, 2), fromPdb);
		Assert.Equal(6L, LuaValue("#structures"));

		long before = (long) LuaValue("created")!;
		AssertDeclared(() => tools.Create("Player", cancellationToken: StructureToken), ToolErrorKind.InvalidState,
			ToolHostEffect.NotStarted);
		AssertDeclared(() => tools.Create("Other",
				[new StructureElementSpec("0", "p", McpValueType.Pointer, ChildStructure: "Missing")],
				cancellationToken: StructureToken),
			ToolErrorKind.NotFound, ToolHostEffect.NotStarted);
		AssertDeclared(() => tools.Create("Other", cloneFrom: "Missing", cancellationToken: StructureToken),
			ToolErrorKind.NotFound, ToolHostEffect.NotStarted);
		AssertDeclared(() => tools.Create("Other", pdbTypeName: "_MISSING", cancellationToken: StructureToken),
			ToolErrorKind.NotFound, ToolHostEffect.NotStarted);
		Assert.Equal(before, (long) LuaValue("created")!);

		AssertDeclared(() => tools.Create("Broken",
				[new StructureElementSpec("0", "text", McpValueType.String, ByteSize: 5000)],
				cancellationToken: StructureToken),
			ToolErrorKind.HostRefused, ToolHostEffect.NotApplied);
		Assert.Equal(6L, LuaValue("#structures"));
		LuaJsonResult<StructureSummary> limited = ReadJson(
			LuaToolRuntime.BuildSource(StructureLuaScripts.CreateFromPdb, 100, ["Peb2", false, "_PEB", 1]),
			StructuresJsonContext.Default.StructureSummary);
		Assert.Equal(("limit_exceeded", "not_started"), (limited.Error!.Kind, limited.Error.HostEffect));
	}

	[Fact]
	public void StructureDelete_KnownThenAgain_DestroysOnceThenIsNotFound()
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs(StructureHostStubs);
		StructureTools tools = NativeStructureHarness().Structures;

		Assert.Equal(new StructureDeleted("Enemy"), tools.Delete("Enemy", StructureToken));

		Assert.Equal(true, LuaValue("enemy.destroyed"));
		AssertDeclared(() => tools.Delete("Enemy", StructureToken), ToolErrorKind.NotFound, ToolHostEffect.NotStarted);
	}

	[Fact]
	public void StructureAddElements_NewOffsets_ReportTheSortedIndicesAndRollBackARefusal()
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs(StructureHostStubs);
		StructureElementTools tools = NativeStructureHarness().Elements;

		StructureChange change = tools.AddElements("Player",
		[
			new StructureElementSpec("18", "tail", McpValueType.UInt16),
			new StructureElementSpec("4", "armor", McpValueType.Int32)
		], StructureToken);

		Assert.Equal([4, 1], change.Indices);
		Assert.Equal(5, change.ElementCount);
		Assert.Equal(0L, LuaValue("player.updating"));
		AssertDeclared(() => tools.AddElements("Player",
		[
			new StructureElementSpec("20", "ok", McpValueType.Int32),
			new StructureElementSpec("24", "text", McpValueType.String, ByteSize: 5000)
		], StructureToken), ToolErrorKind.HostRefused, ToolHostEffect.NotApplied);
		Assert.Equal(5L, LuaValue("#player.elements"));
	}

	[Fact]
	public void StructureUpdateElements_Reordering_KeepsEachUpdateOnItsElementAndStopsAtARefusal()
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs(StructureHostStubs);
		StructureElementTools tools = NativeStructureHarness().Elements;

		StructureChange change = tools.UpdateElements("Player",
		[
			new StructureElementUpdate(0, "20"),
			new StructureElementUpdate(1, Name: "velocity", ValueType: McpValueType.Double)
		], StructureToken);

		Assert.Equal([2, 0], change.Indices);
		Assert.Equal(("health", 32L, "velocity", 5L), (LuaValue("player.elements[3].Name"),
			LuaValue("player.elements[3].Offset"), LuaValue("player.elements[1].Name"),
			LuaValue("player.elements[1].Vartype")));
		AssertDeclared(() => tools.UpdateElements("Player",
			[new StructureElementUpdate(0, Display: StructureDisplay.Hex)],
			StructureToken), ToolErrorKind.InvalidArgument, ToolHostEffect.NotStarted);
		AssertDeclared(() => tools.UpdateElements("Player", [new StructureElementUpdate(7, Name: "x")], StructureToken),
			ToolErrorKind.NotFound, ToolHostEffect.NotStarted);

		// Elements are now velocity (double), flags (binary), health (int32); health refuses a new display.
		InstallStubs("local e = player.elements[3]; e.DisplayMethod = nil; local index = getmetatable(e).__index; " +
					 "setmetatable(e, {__index = index, __newindex = function(t, k, v) " +
					 "if k == 'DisplayMethod' then error('read only') end rawset(t, k, v) end})");
		CheatEngineToolException partial = Assert.Throws<CheatEngineToolException>(() => tools.UpdateElements("Player",
		[
			new StructureElementUpdate(0, Name: "first"),
			new StructureElementUpdate(2, Display: StructureDisplay.Hex)
		], StructureToken));
		Assert.Equal((ToolErrorKind.PartialEffect, ToolHostEffect.Started),
			(partial.Error.Kind, partial.Error.HostEffect));
		Assert.Equal(1, partial.Error.Details!.Value.GetProperty("failedIndex").GetInt32());
		Assert.Equal("first", LuaValue("player.elements[1].Name"));
	}

	[Fact]
	public void StructureRemoveElements_Indices_RemoveHighestFirstAndRefuseOutOfRange()
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs(StructureHostStubs);
		StructureElementTools tools = NativeStructureHarness().Elements;

		AssertDeclared(() => tools.RemoveElements("Player", [0, 3], StructureToken), ToolErrorKind.NotFound,
			ToolHostEffect.NotStarted);
		StructureChange change = tools.RemoveElements("Player", [0, 2], StructureToken);

		Assert.Equal([2, 0], change.Indices);
		Assert.Equal(1, change.ElementCount);
		Assert.Equal("speed", LuaValue("player.elements[1].Name"));
	}

	[Fact]
	public void StructureAutoGuess_MissingStructure_IsCreatedGuessedFromHexTextOrDiscarded()
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs(StructureHostStubs);
		StructureTools tools = NativeStructureHarness().Structures;

		StructureSummary guessed = tools.Autoguess("Guess", "1000", "8", 64, cancellationToken: StructureToken);

		Assert.Equal(new StructureSummary("Guess", 12, 1), guessed);
		Assert.Equal(("0x1000", 8L, 64L), (LuaValue("guessed.base"), LuaValue("guessed.offset"),
			LuaValue("guessed.size")));
		AssertDeclared(
			() => tools.Autoguess("Other", "1000", createIfMissing: false, cancellationToken: StructureToken),
			ToolErrorKind.NotFound, ToolHostEffect.NotStarted);
		InstallStubs("guessFails = true");
		AssertDeclared(() => tools.Autoguess("Failed", "1000", cancellationToken: StructureToken),
			ToolErrorKind.HostRefused, ToolHostEffect.NotApplied);
		AssertDeclared(() => tools.Autoguess("Player", "1000", cancellationToken: StructureToken),
			ToolErrorKind.HostRefused, ToolHostEffect.Unknown);
		Assert.Equal(4L, LuaValue("#structures"));
	}

	[Fact]
	public void StructureFillFromDotNet_HaltedMissingOrFound_FollowsTheCollector()
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs(StructureHostStubs);
		StructureTools tools = NativeStructureHarness().Structures;

		InstallStubs("debug_isBroken = function() return true end");
		AssertDeclared(() => tools.FillFromDotNet("Managed", "1000", cancellationToken: StructureToken),
			ToolErrorKind.Busy, ToolHostEffect.NotStarted);
		InstallStubs("debug_isBroken = function() return false end; isPaused = function() return true end");
		AssertDeclared(() => tools.FillFromDotNet("Managed", "1000", cancellationToken: StructureToken),
			ToolErrorKind.Busy, ToolHostEffect.NotStarted);
		InstallStubs("isPaused = function() return false end");
		AssertDeclared(() => tools.FillFromDotNet("Managed", "1000", cancellationToken: StructureToken),
			ToolErrorKind.NotFound, ToolHostEffect.NotApplied);
		Assert.Equal(3L, LuaValue("#structures"));

		InstallStubs("dotnetClass = 'Game.Player'");
		StructureSummary filled = tools.FillFromDotNet("Managed", "1000", true, cancellationToken: StructureToken);

		Assert.Equal(new StructureSummary("Game.Player", 8, 1), filled);
		Assert.Equal(("0x1000", true), (LuaValue("filled.address"), LuaValue("filled.rename")));
		Assert.Equal(4L, LuaValue("#structures"));
	}

	[Fact]
	public void StructurePdbLayout_KnownAndUnknownTypes_CopyBoundedFields()
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs(StructureHostStubs);
		InstallStubs(
			"pdb['_PEB'] = {{offset = 0, name = 'InheritedAddressSpace', vartype = 0}, {offset = 16, name = 'Ldr', vartype = 12}, {offset = 24, name = 'Odd', vartype = 'x'}}");
		StructureTools tools = NativeStructureHarness().Structures;

		PdbLayout layout = tools.GetPdbLayout("_PEB", 2, StructureToken);
		PdbLayout missing = tools.GetPdbLayout("_NONE", cancellationToken: StructureToken);
		PdbLayout all = tools.GetPdbLayout("_PEB", cancellationToken: StructureToken);

		Assert.True(layout.Found && layout.Truncated);
		Assert.Equal([
			new PdbLayoutElement("0", "InheritedAddressSpace", StructureElementType.UInt8),
			new PdbLayoutElement("10", "Ldr", StructureElementType.Pointer)
		], layout.Elements);
		Assert.Equal((false, 0, false), (missing.Found, missing.Elements.Length, missing.Truncated));
		Assert.Null(all.Elements[2].ValueType);
	}

	[Fact]
	public void StructureReadAndWrite_ManagedAndCheatEngineFormattedElements_ShareOneDispatch()
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs(StructureHostStubs);
		StructureToolHarness harness = NativeStructureHarness();
		harness.Map(0x1000, 0x9C, 0xFF, 0xFF, 0xFF, 0, 0, 0, 0, 0, 0, 0xC0, 0x3F, 0, 0, 0, 0, 5);

		StructureReadResult read = harness.Values.Read("Player", ["1000", "2000"], cancellationToken: StructureToken);

		Assert.Equal(["-100", null], read.Elements[0].Values);
		Assert.Equal(["1.5", null], read.Elements[1].Values);
		Assert.Equal((StructureElementType.Binary, "v1000", (string?) null),
			(read.Elements[2].ValueType, read.Elements[2].Values[0], read.Elements[2].Values[1]));
		Assert.Equal(1, harness.Dispatcher.Calls);

		StructureWriteResult managed = harness.Values.WriteElement("Player", "1000", 0, "7", StructureToken);
		StructureWriteResult formatted = harness.Values.WriteElement("Player", "1000", 2, "1", StructureToken);

		Assert.Equal(new StructureWriteResult("1000", 4, "7"), managed);
		Assert.Equal(new StructureWriteResult("1010", 1, "v1000"), formatted);
		Assert.Equal((4096L, "1"), (LuaValue("written.base"), LuaValue("written.value")));
		AssertDeclared(() => harness.Values.WriteElement("Player", "1000", 9, "1", StructureToken),
			ToolErrorKind.NotFound, ToolHostEffect.NotStarted);
	}

	[Fact]
	public void StructureCompare_AlongAStubbedStructure_ClassifiesItsElements()
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs(StructureHostStubs);
		StructureToolHarness harness = NativeStructureHarness();
		harness.Map(0x1000, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0xC8, 0x42, 0, 0, 0, 0, 7);
		harness.Map(0x2000, 2, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0xC8, 0x42, 0, 0, 0, 0, 7);

		StructureComparison comparison = harness.Comparisons.Compare(["1000"], ["2000"], structureName: "Player",
			mode: StructureCompareMode.All, cancellationToken: StructureToken);

		Assert.Equal(
			[
				StructureFieldClassification.Discriminator, StructureFieldClassification.Constant,
				StructureFieldClassification.Constant
			],
			comparison.Rows.Select(static row => row.Classification));
		Assert.Equal(["1"], comparison.Rows[0].ValuesA);
		Assert.Equal(["2"], comparison.Rows[0].ValuesB);
		Assert.Equal(["100"], comparison.Rows[1].ValuesA);
	}

	private static StructureToolHarness NativeStructureHarness()
	{
		return new StructureToolHarness { NativeLua = ExecuteNativeLua };
	}

	/// <summary>Runs one Client Lua operation on the test's Lua state, as the Client would on Cheat Engine's thread.</summary>
	private static object? ExecuteNativeLua(MethodInfo method, object operation)
	{
		Type resultType = method.GetGenericArguments()[1];
		MethodInfo tryExecute = typeof(ILuaOperation<>).MakeGenericType(resultType)
			.GetMethod(nameof(ILuaOperation<>.TryExecute))!;
		object?[] call = [ActiveContext.Instance, null, null];
		if ((bool) tryExecute.Invoke(operation, call)!)
		{
			return call[1];
		}

		((CheatEngineFailure) call[2]!).Throw();
		return null;
	}

	private static void AssertDeclared(Action call, ToolErrorKind kind, ToolHostEffect effect)
	{
		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(call);
		Assert.Equal((kind, effect), (exception.Error.Kind, exception.Error.HostEffect));
	}

	private static void AssertDeclared<T>(Func<T> call, ToolErrorKind kind, ToolHostEffect effect)
	{
		AssertDeclared(() =>
		{
			_ = call();
		}, kind, effect);
	}

	/// <summary>Evaluates a Lua expression over the stubs' globals.</summary>
	private static object? LuaValue(string expression)
	{
		PluginLuaToolRuntime.LuaToolOperation operation = new("structure_probe", "return " + expression);
		Assert.True(operation.TryExecute(ActiveContext.Instance, out object? result, out CheatEngineFailure failure),
			failure.Message);
		return result;
	}
}
