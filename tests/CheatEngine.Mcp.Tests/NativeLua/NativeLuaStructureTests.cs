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
	// sorted by offset on endUpdate, and element indices through the published index property. Size is the end of the
	// element with the highest offset, as Cheat Engine's getStructureSize reads its last sorted element.
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
	                                                local last = nil
	                                                for _, e in ipairs(t.elements) do
	                                                  if last == nil or e.Offset >= last.Offset then last = e end
	                                                end
	                                                if last == nil then return 0 end
	                                                return last.Offset + last.bytesize
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

	// Player points to Enemy and Enemy back to Player, so the C header closure must end on the cycle.
	private const string StructurePointerCycle = """
	                                             local target = player.addElement()
	                                             target.Offset = 24; target.Name = 'target'; target.Vartype = 12; target.bytesize = 8
	                                             target.ChildStruct = enemy
	                                             local owner = enemy.addElement()
	                                             owner.Offset = 8; owner.Name = 'owner'; owner.Vartype = 12; owner.bytesize = 8
	                                             owner.ChildStruct = player
	                                             """;

	private static readonly int[] StructureRemovedIndices = [2, 0];
	private static readonly int[] StructureFormattedIndices = [2];
	private static readonly ulong[] StructureFormattedBases = [0x1000UL];
	private static readonly string[] StructureHeaderNames = ["Player"];

	public static TheoryData<string, object?[]> StructureScripts => new()
	{
		{ nameof(StructureLuaScripts.Helpers), [] },
		{ nameof(StructureLuaScripts.List), ["play", 0, 100, 65536] },
		{ nameof(StructureLuaScripts.Elements), ["Player", null, 16, 0, 100, true] },
		{ nameof(StructureLuaScripts.DefinitionPage), ["Player", 0, 100, true, 65536] },
		{ nameof(StructureLuaScripts.ElementAt), ["Player", 1] },
		{
			nameof(StructureLuaScripts.CreateFromElements),
			["New", true, new object?[] { new object?[] { 0, "a", 2, "dtSignedInteger", null, "Player", 4 } }]
		},
		{ nameof(StructureLuaScripts.CreateClone), ["Copy", false, "Player"] },
		{ nameof(StructureLuaScripts.CreateFromPdb), ["Peb", false, "_PEB", 4096] },
		{ nameof(StructureLuaScripts.Delete), ["Player"] },
		{ nameof(StructureLuaScripts.Rename), ["Player", "Hero"] },
		{ nameof(StructureLuaScripts.CHeader), [StructureHeaderNames, "auto", 256, 8192, 1048576, 1048576] },
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
		StructureDefinition concise = tools.Get("Player", 1, 2, ResultFormat.Concise, StructureToken);
		StructureDefinition tail = tools.Get("Player", 2, 1, ResultFormat.Detailed, StructureToken);

		Assert.Equal(("Player", 3, false, (int?) null), (definition.Name, definition.Total, definition.Internal,
			definition.NextOffset));
		Assert.Equal((definition.Name, definition.Total, definition.Internal, definition.NextOffset),
			(concise.Name, concise.Total, concise.Internal, concise.NextOffset));
		Assert.Equal((3, 1, (int?) null), (tail.Total, tail.Elements.Length, tail.NextOffset));
		Assert.Equal(new StructureElement(1, "8", "speed", StructureElementType.Float, null, 4, "Enemy", "4"),
			definition.Elements[0]);
		Assert.Equal((StructureElementType.Binary, 1),
			(definition.Elements[1].ValueType, definition.Elements[1].ByteSize));
		AssertDeclared(() => tools.Get("player", cancellationToken: StructureToken), ToolErrorKind.NotFound,
			ToolHostEffect.NotStarted);
	}

	[Fact]
	public void StructureGet_BoundedDefinitionLookupAndPagedElements_AvoidWholeStructureTraversal()
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs(StructureHostStubs);
		InstallStubs("""
					 virtualCalls, elementCalls = {}, {}
					 getStructureCount = function() return 70000 end
					 getStructure = function(index)
					   if index == 0 then virtualCalls[#virtualCalls + 1] = index; return player end
					   error('bounded lookup must stop at the matching definition')
					 end
					 setmetatable(player, {__index = function(t, key)
					   if key == 'Count' then return 100000 end
					   if key == 'Size' then return 400000 end
					 end})
					 player.getElement = function(index)
					   elementCalls[#elementCalls + 1] = index
					   if index < 900 or index > 901 then error('page-only getElement access expected') end
					   return {Offset = index * 4, Name = 'field' .. index, Vartype = 2,
					     DisplayMethod = 'dtSignedInteger', getBytesize = function() return 4 end}
					 end
					 """);
		StructureTools tools = NativeStructureHarness().Structures;

		StructureDefinition definition = tools.Get("Player", 900, 2, cancellationToken: StructureToken);

		Assert.Equal((100000, 2, (int?) 902), (definition.Total, definition.Elements.Length, definition.NextOffset));
		Assert.Equal((1L, 0L), (LuaValue("#virtualCalls"), LuaValue("virtualCalls[1]")));
		Assert.Equal((2L, 900L, 901L), (LuaValue("#elementCalls"), LuaValue("elementCalls[1]"),
			LuaValue("elementCalls[2]")));
	}

	[Fact]
	public void StructureGet_MissingDefinition_ReportsNotFoundWithinBoundAndLimitExceededPastIt()
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs(StructureHostStubs);
		StructureTools tools = NativeStructureHarness().Structures;

		AssertDeclared(() => tools.Get("Missing", cancellationToken: StructureToken), ToolErrorKind.NotFound,
			ToolHostEffect.NotStarted);
		InstallStubs("getStructureCount = function() return 65537 end; getStructure = function() return nil end");
		AssertDeclared(() => tools.Get("Missing", cancellationToken: StructureToken), ToolErrorKind.LimitExceeded,
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
	public void StructureSetName_StubbedStructures_RenamesInPlaceAndRefusesTakenOrMissingNames()
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs(StructureHostStubs);
		// getStructure(name) also finds internal structures, which the global list does not hold.
		InstallStubs("hidden = makeStructure('Hidden'); hidden.Internal = true; local byName = getStructure; " +
					 "getStructure = function(x) if x == 'Hidden' then return hidden end return byName(x) end");
		InstallStubs(StructurePointerCycle);
		StructureTools tools = NativeStructureHarness().Structures;

		StructureSummary renamed = tools.SetName("Player", "Hero", StructureToken);
		StructureSummary unchanged = tools.SetName("Hero", "Hero", StructureToken);

		Assert.Equal(new StructureSummary("Hero", 32, 4), renamed);
		Assert.Equal(renamed, unchanged);
		Assert.Equal(("Hero", "Hero", 3L), (LuaValue("player.Name"), LuaValue("enemy.elements[2].ChildStruct.Name"),
			LuaValue("#structures")));
		AssertDeclared(() => tools.SetName("Hero", "Enemy", StructureToken), ToolErrorKind.InvalidState,
			ToolHostEffect.NotStarted);
		AssertDeclared(() => tools.SetName("Hero", "Hidden", StructureToken), ToolErrorKind.InvalidState,
			ToolHostEffect.NotStarted);
		AssertDeclared(() => tools.SetName("Player", "Other", StructureToken), ToolErrorKind.NotFound,
			ToolHostEffect.NotStarted);
		Assert.Equal(("Hero", "Enemy"), (LuaValue("player.Name"), LuaValue("enemy.Name")));
	}

	[Fact]
	public void StructureSetName_SetterThatFailsOrIgnoresTheName_IsHostRefusedNotApplied()
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs(StructureHostStubs);
		InstallStubs("local index = getmetatable(enemy).__index; enemy.realName = enemy.Name; enemy.Name = nil; " +
					 "setmetatable(enemy, {__index = function(t, k) if k == 'Name' then return t.realName end " +
					 "return index(t, k) end, __newindex = function(t, k, v) if k == 'Name' then " +
					 "if ignoreRename then return end error('read only') end rawset(t, k, v) end})");
		StructureTools tools = NativeStructureHarness().Structures;

		AssertDeclared(() => tools.SetName("Enemy", "Foe", StructureToken), ToolErrorKind.HostRefused,
			ToolHostEffect.NotApplied);
		InstallStubs("ignoreRename = true");
		AssertDeclared(() => tools.SetName("Enemy", "Foe", StructureToken), ToolErrorKind.HostRefused,
			ToolHostEffect.NotApplied);

		Assert.Equal("Enemy", LuaValue("enemy.Name"));
	}

	[Fact]
	public void StructureGenerateCHeader_CheatEngineGenerator_GetsTheRequestedStructuresOnly()
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs(StructureHostStubs);
		InstallStubs(StructurePointerCycle);
		InstallStubs("generate_c_header = function(list) received = list; " +
					 "return 'header of ' .. list[1].Name .. ' and ' .. #list end");
		StructureHeaderTools tools = NativeStructureHarness().Headers;

		StructureCHeader header = tools.GenerateCHeader(["Player"], cancellationToken: StructureToken);
		StructureCHeader both = tools.GenerateCHeader(["Enemy", "PlayerBase"], StructureHeaderGenerator.CheatEngine,
			StructureToken);

		Assert.Equal(("header of Player and 1", StructureHeaderGenerator.CheatEngine), (header.Text, header.Generator));
		Assert.Equal(["Player", "Enemy"], header.Structures);
		Assert.Equal("header of Enemy and 2", both.Text);
		Assert.Equal(["Enemy", "PlayerBase", "Player"], both.Structures);
		Assert.Equal("PlayerBase", LuaValue("received[2].Name"));
	}

	[Fact]
	public void StructureGenerateCHeader_MissingOrFailingGenerator_FallsBackToTheManagedCopy()
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs(StructureHostStubs);
		InstallStubs(StructurePointerCycle);
		StructureHeaderTools tools = NativeStructureHarness().Headers;

		StructureCHeader missing = tools.GenerateCHeader(["Player"], cancellationToken: StructureToken);
		AssertDeclared(() => tools.GenerateCHeader(["Player"], StructureHeaderGenerator.CheatEngine, StructureToken),
			ToolErrorKind.Unsupported, ToolHostEffect.NotStarted);
		InstallStubs("generate_c_header = function() error('broken') end");
		StructureCHeader failed = tools.GenerateCHeader(["Player"], cancellationToken: StructureToken);
		AssertDeclared(() => tools.GenerateCHeader(["Player"], StructureHeaderGenerator.CheatEngine, StructureToken),
			ToolErrorKind.HostRefused, ToolHostEffect.NotApplied);
		InstallStubs("generate_c_header = function() return nil end");
		AssertDeclared(() => tools.GenerateCHeader(["Player"], StructureHeaderGenerator.CheatEngine, StructureToken),
			ToolErrorKind.HostRefused, ToolHostEffect.NotApplied);
		InstallStubs("generate_c_header = function() return string.rep('x', 1048577) end");
		AssertDeclared(() => tools.GenerateCHeader(["Player"], cancellationToken: StructureToken),
			ToolErrorKind.LimitExceeded, ToolHostEffect.NotStarted);
		StructureCHeader managed = tools.GenerateCHeader(["Player"], StructureHeaderGenerator.Managed, StructureToken);

		Assert.Equal((StructureHeaderGenerator.Managed, StructureHeaderGenerator.Managed),
			(missing.Generator, failed.Generator));
		Assert.Equal(missing.Text, failed.Text);
		Assert.Equal(missing.Text, managed.Text);
		Assert.Equal(["Player", "Enemy"], managed.Structures);
		Assert.Contains("struct Player\n{\n\tint32_t health; // 0x0 int32\n\tuint8_t pad_4[0x4]; // 0x4 padding\n" +
						"\tfloat speed; // 0x8 float\n\tuint8_t pad_C[0x4]; // 0xC padding\n" +
						"\tuint8_t flags; // 0x10 binary\n\tuint8_t pad_11[0x7]; // 0x11 padding\n" +
						"\tEnemy *target; // 0x18 pointer to Enemy\n};\n", managed.Text, StringComparison.Ordinal);
		Assert.Contains("\tPlayer *owner; // 0x8 pointer to Player\n", managed.Text, StringComparison.Ordinal);
	}

	[Fact]
	public void StructureGenerateCHeader_NestedBinaryAndCustomElements_AreCopiedForTheManagedGenerator()
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs(StructureHostStubs);
		InstallStubs("""
					 vec = define('Vec', {{0, 'x', 4}, {4, 'y', 4}, {8, 'z', 4}})
					 local pos = player.addElement()
					 pos.Offset = 32; pos.Name = 'pos'; pos.Vartype = 12; pos.bytesize = 12; pos.ChildStruct = vec
					 pos.NestedStructure = true
					 local kind = player.addElement()
					 kind.Offset = 44; kind.Name = 'kind'; kind.Vartype = 13; kind.CustomTypeName = 'BE4'
					 player.elements[3].BitStart = 1; player.elements[3].BitSize = 3
					 """);
		StructureHeaderTools tools = NativeStructureHarness().Headers;

		StructureCHeader header = tools.GenerateCHeader(["Player"], StructureHeaderGenerator.Managed, StructureToken);

		Assert.Equal(["Player", "Vec"], header.Structures);
		Assert.Contains("\tuint8_t flags; // 0x10 binary bits 1-3\n", header.Text, StringComparison.Ordinal);
		Assert.Contains("\tVec pos; // 0x20 nested Vec\n", header.Text, StringComparison.Ordinal);
		Assert.Contains("\tuint32_t kind; // 0x2C custom \"BE4\"\n", header.Text, StringComparison.Ordinal);
		Assert.True(header.Text.IndexOf("struct Vec\n", StringComparison.Ordinal) <
					header.Text.IndexOf("struct Player\n", StringComparison.Ordinal),
			"An embedded structure must be defined before the structure that embeds it.");
	}

	[Fact]
	public void StructureGenerateCHeader_PointerIntoItsChild_IsCopiedWithItsStartOnlyWhenNotNested()
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs(StructureHostStubs);
		InstallStubs(StructurePointerCycle);
		InstallStubs("""
					 player.elements[4].childStart = 4
					 enemy.elements[2].getChildStructStart = function() error('no start') end
					 vec = define('Vec', {{0, 'x', 4}, {4, 'y', 4}, {8, 'z', 4}})
					 local pos = player.addElement()
					 pos.Offset = 32; pos.Name = 'pos'; pos.Vartype = 12; pos.bytesize = 12; pos.ChildStruct = vec
					 pos.NestedStructure = true; pos.childStart = 4
					 """);
		StructureHeaderTools tools = NativeStructureHarness().Headers;

		LuaJsonResult<StructureLuaHeader> copied =
			StructureHeaderScript(StructureHeaderNames, "managed", 256, 8192, 1048576, 1048576);
		StructureCHeader header = tools.GenerateCHeader(["Player"], StructureHeaderGenerator.Managed, StructureToken);

		StructureLuaHeaderElement[] elements = copied.Value!.Structures![0].Elements;
		Assert.Equal(("target", 4L, "pos", (long?) null),
			(elements[3].Name, elements[3].ChildStart, elements[4].Name, elements[4].ChildStart));
		Assert.Equal(("owner", (long?) null), (copied.Value.Structures[1].Elements[1].Name,
			copied.Value.Structures[1].Elements[1].ChildStart));
		Assert.Contains("\tuint8_t *target; // 0x18 pointer to Enemy+0x4\n", header.Text, StringComparison.Ordinal);
		Assert.Contains("\tPlayer *owner; // 0x8 pointer to Player\n", header.Text, StringComparison.Ordinal);
		Assert.Contains("\tVec pos; // 0x20 nested Vec\n", header.Text, StringComparison.Ordinal);
	}

	[Fact]
	public void StructureGenerateCHeader_NestedChildWithAnElementPastItsSize_IsEmbeddedAsBytes()
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs(StructureHostStubs);
		// Cheat Engine's Size of Pair ends at its highest-offset element, b (2 + 2 = 4), yet a reaches 8.
		InstallStubs("""
					 pair = define('Pair', {{0, 'a', 3, nil, 8}, {2, 'b', 1, nil, 2}})
					 holder = define('Holder', {{4, 'x', 2}})
					 local inner = holder.addElement()
					 inner.Offset = 0; inner.Name = 'inner'; inner.Vartype = 12; inner.bytesize = 4
					 inner.ChildStruct = pair; inner.NestedStructure = true
					 """);
		StructureHeaderTools tools = NativeStructureHarness().Headers;

		StructureCHeader header = tools.GenerateCHeader(["Holder"], StructureHeaderGenerator.Managed, StructureToken);

		Assert.Equal(["Holder", "Pair"], header.Structures);
		Assert.Contains("// \"Pair\": 0x4 bytes, 2 elements\nstruct Pair\n{\n\tuint64_t a; // 0x0 uint64\n" +
						"\t// 0x2 uint16 \"b\": overlaps the member before it, not declared\n};\n" +
						"// An element ends at 0x8, past Cheat Engine's size 0x4, which ends at the element with the " +
						"highest offset.\n// static_assert(sizeof(Pair) == 0x8, \"Pair\");\n", header.Text,
			StringComparison.Ordinal);
		Assert.Contains("\tuint8_t inner[0x4]; // 0x0 nested Pair as bytes\n\tuint32_t x; // 0x4 uint32\n",
			header.Text, StringComparison.Ordinal);
	}

	[Fact]
	public void StructureGenerateCHeader_Limits_AreDeclaredBeforeAnyGeneration()
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs(StructureHostStubs);
		InstallStubs(StructurePointerCycle);
		InstallStubs("generate_c_header = function(list) called = true; return string.rep('x', 100) end");
		string[] missing = ["Player", "Nope"];

		LuaJsonResult<StructureLuaHeader> structures =
			StructureHeaderScript(StructureHeaderNames, "auto", 1, 8192, 64, 1048576);
		LuaJsonResult<StructureLuaHeader> elements =
			StructureHeaderScript(StructureHeaderNames, "auto", 256, 4, 64, 1048576);
		LuaJsonResult<StructureLuaHeader> unknown = StructureHeaderScript(missing, "auto", 256, 8192, 64, 1048576);
		Assert.Null(LuaValue("called"));
		LuaJsonResult<StructureLuaHeader> text = StructureHeaderScript(StructureHeaderNames, "auto", 256, 8192, 64,
			1048576);

		Assert.Equal(("limit_exceeded", "not_started"), (structures.Error!.Kind, structures.Error.HostEffect));
		Assert.Equal(("limit_exceeded", "not_started"), (elements.Error!.Kind, elements.Error.HostEffect));
		Assert.Equal(("not_found", "not_started"), (unknown.Error!.Kind, unknown.Error.HostEffect));
		Assert.Contains("names[1]", unknown.Error.Message, StringComparison.Ordinal);
		Assert.Equal(("limit_exceeded", "not_started"), (text.Error!.Kind, text.Error.HostEffect));
		Assert.Equal(true, LuaValue("called"));
	}

	[Fact]
	public void StructureGenerateCHeader_ElementsSpanningTooManyBytes_SkipCheatEngineGenerator()
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs(StructureHostStubs);
		InstallStubs("generate_c_header = function(list) called = true; return 'native' end");

		// Player's elements span 4 + 4 + 1 bytes.
		LuaJsonResult<StructureLuaHeader> skipped =
			StructureHeaderScript(StructureHeaderNames, "auto", 256, 8192, 1048576, 8);
		LuaJsonResult<StructureLuaHeader> refused =
			StructureHeaderScript(StructureHeaderNames, "cheat_engine", 256, 8192, 1048576, 8);
		Assert.Null(LuaValue("called"));
		LuaJsonResult<StructureLuaHeader> native =
			StructureHeaderScript(StructureHeaderNames, "cheat_engine", 256, 8192, 1048576, 9);

		Assert.False(skipped.IsError);
		Assert.Null(skipped.Value!.Text);
		Assert.Equal(3, Assert.Single(skipped.Value.Structures!).Elements.Length);
		Assert.Equal(("limit_exceeded", "not_started"), (refused.Error!.Kind, refused.Error.HostEffect));
		Assert.Equal("native", native.Value!.Text);
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

		// Cheat Engine reads from the base argument, so the body receives the object address plus the offset.
		Assert.Equal(new StructureSummary("Guess", 12, 1), guessed);
		Assert.Equal(("0x1008", 8L, 64L), (LuaValue("guessed.base"), LuaValue("guessed.offset"),
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

	/// <summary>Runs the C header script with explicit caps and copies its result.</summary>
	private static LuaJsonResult<StructureLuaHeader> StructureHeaderScript(params object?[] arguments)
	{
		return ReadJson(LuaToolRuntime.BuildSource(StructureLuaScripts.CHeader, 100, arguments),
			StructureLuaJsonContext.Default.StructureLuaHeader);
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
