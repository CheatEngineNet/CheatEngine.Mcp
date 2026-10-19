using System.Text.Json;

using CheatEngine.Client.Processes;
using CheatEngine.Mcp.Core.Files;
using CheatEngine.Mcp.Tests.Core;
using CheatEngine.Mcp.Tests.Tools.Structures;
using CheatEngine.Mcp.Tools.Structures;
using CheatEngine.SDK.Engine.Inspection;
using CheatEngine.SDK.Engine.Runtime;

namespace CheatEngine.Mcp.Tests.NativeLua;

public sealed partial class NativeLuaToolRuntimeTests
{
	private const string InterchangeHostStubs = StructureHostStubs + """
        local identities, nextIdentity = {}, 0
        function userDataToInteger(s)
          if identities[s] == nil then nextIdentity = nextIdentity + 1; identities[s] = nextIdentity end
          return identities[s]
        end
        """;

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public void StructureInterchangeExport_PrivateChild_UsesReferencedHandleEvenWithGlobalNamesake(bool globalNamesake)
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs(InterchangeHostStubs);
		InstallStubs("""
local root = define('Root', {})
local child = createStructure('Child')
local value = child.addElement(); value.Offset = 16; value.Name = 'actual'; value.Vartype = 2
local pointer = root.addElement(); pointer.Name = 'child'; pointer.Vartype = 12; pointer.ChildStruct = child
""");
		if (globalNamesake)
		{
			InstallStubs("define('Child', {{8, 'unrelated', 4}})");
		}

		LuaJsonResult<StructureInterchangeLuaExport> result = ReadJson(
			LuaToolRuntime.BuildSource(StructureInterchangeLua.Export, 100, [new[] { "Root" }, 128, 4096]),
			StructureInterchangeJsonContext.Default.StructureInterchangeLuaExport);

		Assert.False(result.IsError);
		Assert.Equal(["Root", "Child"], result.Value.Layouts.Select(layout => layout.Name));
		StructureInterchangeLuaField actual = Assert.Single(result.Value.Layouts[1].Fields);
		Assert.Equal((16L, "actual", 2), (actual.Offset, actual.Name, actual.Vartype));
	}

	[Fact]
	public void StructureInterchangeExport_DistinctReferencedChildrenWithSameName_RefusesAmbiguousFile()
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs(InterchangeHostStubs);
		InstallStubs("""
local root = define('Root', {})
for i = 1, 2 do
  local child = createStructure('Repeated')
  local pointer = root.addElement(); pointer.Offset = i * 8; pointer.Vartype = 12; pointer.ChildStruct = child
end
""");

		LuaJsonResult<StructureInterchangeLuaExport> result = ReadJson(
			LuaToolRuntime.BuildSource(StructureInterchangeLua.Export, 100, [new[] { "Root" }, 128, 4096]),
			StructureInterchangeJsonContext.Default.StructureInterchangeLuaExport);

		Assert.True(result.IsError);
		Assert.Equal(("invalid_state", "not_started"), (result.Error.Kind, result.Error.HostEffect));
		Assert.Contains("Repeated", result.Error.Message, StringComparison.Ordinal);
	}

	[Fact]
	public void StructureInterchange_ProductionAdapters_RoundTripCyclicLayoutsLosslessly()
	{
		using RuntimeScope scope = CreateScope();
		using McpFilePathsTests.Scratch scratch = new();
		InstallStubs(InterchangeHostStubs);
		InstallStubs("""
local a = define('RoundA', {{4, 'health', 2, 'dtSignedInteger'}, {16, 'bytes', 8, nil, 12}})
local b = define('RoundB', {{0, 'value', 5}})
b.Internal = true
local p = a.addElement(); p.Offset = 32; p.Name = 'next'; p.Vartype = 12; p.ChildStruct = b; p.childStart = 8
local q = b.addElement(); q.Offset = 16; q.Name = 'back'; q.Vartype = 12; q.ChildStruct = a
""");
		ProcessSnapshot process = new(new TargetProcessId(77), "owned.exe", null, TargetBackend.LocalProcess,
			CheatEngineArchitecture.X64, PointerSize.Bit64, 8, null, 2);
		StructureToolHarness harness = new(process)
		{
			NativeLua = ExecuteNativeLua
		};
		string directory = scratch.CreateFolder("files");
		McpFilePaths files = new(new McpFileOptions { AllowedRoots = [directory] }, scratch.CreateFolder("registry"), scratch.CreateFolder("data"));
		StructureInterchangeTools tools = new(harness.Dispatch, files);
		string before = Path.Combine(directory, "before.json");
		string after = Path.Combine(directory, "after.json");

		StructureInterchangeResult exported = tools.Export(["RoundA"], before, cancellationToken: StructureToken);
		using JsonDocument expected = JsonDocument.Parse(File.ReadAllBytes(before));
		Assert.Equal(2, exported.LayoutCount);
		JsonElement root = expected.RootElement;
		Assert.Equal(8, root.GetProperty("pointerWidth").GetInt32());
		JsonElement fields = root.GetProperty("layouts")[0].GetProperty("fields");
		Assert.Equal("Int32", fields[0].GetProperty("valueType").GetString());
		Assert.Equal("4", fields[0].GetProperty("offset").GetString());
		Assert.Equal(12, fields[1].GetProperty("byteSize").GetInt32());
		Assert.Equal("RoundB", fields[2].GetProperty("childStructure").GetString());
		Assert.Equal("8", fields[2].GetProperty("childStructureStart").GetString());
		InstallStubs("getStructure('RoundA').destroy(); getStructure('RoundB').destroy()");
		Assert.Equal(3L, LuaValue("#structures"));

		StructureInterchangeResult imported = tools.Import(before, StructureToken);
		tools.Export(["RoundA"], after, cancellationToken: StructureToken);

		Assert.Equal(["RoundA"], imported.Names);
		Assert.Equal(2, imported.LayoutCount);
		Assert.Equal(File.ReadAllBytes(before), File.ReadAllBytes(after));
		Assert.Equal((5L, "RoundA", true), (LuaValue("#structures"),
			LuaValue("getStructure('RoundB').elements[2].ChildStruct.Name"), LuaValue("getStructure('RoundB').Internal")));
	}

	[Fact]
	public void StructureInterchangeImport_PrivateLayoutsWireCyclesBeforePublishing()
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs(InterchangeHostStubs);
		object?[][] layouts =
		[
			["ImportedA", false, new object?[] { new object?[] { 0, "toB", 12, null, null, 2, 4 } }],
			["ImportedB", true, new object?[] { new object?[] { 0, "toA", 12, null, null, 1, null } }]
		];

		LuaJsonResult<StructureInterchangeLuaImport> result = ReadJson(
			LuaToolRuntime.BuildSource(StructureInterchangeLua.Import, 100, [layouts]),
			StructureInterchangeJsonContext.Default.StructureInterchangeLuaImport);

		Assert.False(result.IsError);
		Assert.Equal(2, result.Value.LayoutCount);
		Assert.Equal(["ImportedA", "ImportedB"], result.Value.Names);
		Assert.Equal((5L, "ImportedB", 4L, "ImportedA", true),
			(LuaValue("#structures"), LuaValue("getStructure('ImportedA').elements[1].ChildStruct.Name"),
			 LuaValue("getStructure('ImportedA').elements[1].childStart"), LuaValue("getStructure('ImportedB').elements[1].ChildStruct.Name"),
			 LuaValue("getStructure('ImportedB').Internal")));
	}

	[Fact]
	public void StructureInterchangeImport_FailureBeforePublish_DestroysPrivateHandles()
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs(InterchangeHostStubs);
		InstallStubs("createdObjects = {}; createStructure = function(name) local s = makeStructure(name); createdObjects[#createdObjects + 1] = s; if name == 'B' then s.addElement = function() error('refused') end end; return s end");
		object?[][] layouts = [["A", false, new object?[] { new object?[] { 0, "x", 2, null, null, null, null } }], ["B", false, new object?[] { new object?[] { 0, "y", 2, null, null, null, null } }]];

		LuaJsonResult<StructureInterchangeLuaImport> result = ReadJson(
			LuaToolRuntime.BuildSource(StructureInterchangeLua.Import, 100, [layouts]),
			StructureInterchangeJsonContext.Default.StructureInterchangeLuaImport);

		Assert.True(result.IsError);
		Assert.Equal(("host_refused", "not_applied"), (result.Error.Kind, result.Error.HostEffect));
		Assert.Equal((3L, true, true), (LuaValue("#structures"), LuaValue("createdObjects[1].destroyed"), LuaValue("createdObjects[2].destroyed")));
	}

	[Fact]
	public void StructureInterchangeImport_UnconfirmedCleanup_ReportsPartialEffect()
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs(InterchangeHostStubs);
		InstallStubs("createStructure = function(name) local s = makeStructure(name); s.destroy = function() error('cleanup refused') end; s.addElement = function() error('refused') end; return s end");
		object?[][] layouts = [["A", false, new object?[] { new object?[] { 0, "x", 2, null, null, null, null } }]];

		LuaJsonResult<StructureInterchangeLuaImport> result = ReadJson(
			LuaToolRuntime.BuildSource(StructureInterchangeLua.Import, 100, [layouts]),
			StructureInterchangeJsonContext.Default.StructureInterchangeLuaImport);

		Assert.True(result.IsError);
		Assert.Equal(("partial_effect", "unknown"), (result.Error.Kind, result.Error.HostEffect));
		Assert.Equal(3L, LuaValue("#structures"));
	}
}
