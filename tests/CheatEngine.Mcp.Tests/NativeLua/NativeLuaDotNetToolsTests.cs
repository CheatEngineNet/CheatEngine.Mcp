using System.Reflection;

using CheatEngine.Mcp.Core.Features;
using CheatEngine.Mcp.Tests.Support;
using CheatEngine.Mcp.Tools.DotNet;

namespace CheatEngine.Mcp.Tests.NativeLua;

/// <summary>
///     Native Lua coverage for the fixed .NET bodies against a collector stub shaped like Cheat Engine 7.7's
///     <c>getDotNetDataCollector()</c> as <c>dotnetinfo.lua</c> reads it.
/// </summary>
public sealed partial class NativeLuaToolRuntimeTests
{
	private const string DotNetCollectorStubs = """
	                                            collector = {Attached = true}
	                                            getDotNetDataCollector = function() return collector end
	                                            collector.getTypeDefData = function(module, token)
	                                              if module == 140 and token == 0x2000005 then
	                                                return {ObjectType = 1, ElementType = 0, CountOffset = 0, ElementSize = 0, FirstElementOffset = 0, ClassName = 'Game.Player',
	                                                  Fields = {
	                                                    {Token = 0x4000001, Name = 'health', FieldType = 8, FieldTypeClassName = 'System.Int32', Offset = 16, IsStatic = false, Attribs = 1},
	                                                    {Token = 0x4000002, Name = 'Instance', FieldType = 18, FieldTypeClassName = 'Game.Player', Offset = 0, IsStatic = true, Attribs = 0x16, Address = 0x7FF0A000},
	                                                    {Token = 0x4000003, Name = 'Max', FieldType = 8, Offset = 0, IsStatic = true, Attribs = 0x56, Address = 0}}}
	                                              end
	                                              if module == 150 and token == 0x2000001 then return {ClassName = 'System.Object', Fields = {}} end
	                                              return nil
	                                            end
	                                            collector.getTypeDefParent = function(module, token)
	                                              if module == 140 then return {ModuleHandle = 150, TypedefToken = 0x2000001} end
	                                              return {ModuleHandle = 0, TypedefToken = 0}
	                                            end
	                                            """;

	[Fact]
	public void DotNetV2_FixedBodies_NeverLoadCodeOrNeedAGate()
	{
		FieldInfo[] bodies = typeof(DotNetLuaScripts).GetFields(BindingFlags.NonPublic | BindingFlags.Static)
			.Where(static field => field.IsLiteral && field.FieldType == typeof(string)).ToArray();

		Assert.NotEmpty(bodies);
		foreach (FieldInfo field in bodies)
		{
			string body = (string) field.GetRawConstantValue()!;
			LuaFixedScriptAssert.NeverLoadsCode(body);
			Assert.True(LuaFeatureScan.Scan(body).Length == 0, field.Name);
		}
	}

	[Fact]
	public void DotNetV2_GetType_FillsTheBaseTypeStaticsAndElementTypes()
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs(DotNetCollectorStubs);

		LuaJsonResult<DotNetTypeDetails> result = ReadJson(
			LuaToolRuntime.BuildSource(DotNetLuaScripts.Type, 100, new object?[] { "140", "33554437", 512 }),
			DotNetJsonContext.Default.DotNetTypeDetails);

		Assert.False(result.IsError, result.Error?.Message);
		DotNetTypeDetails type = result.Value!;
		Assert.Equal(("140", "33554437", "Game.Player"), (type.ModuleHandle, type.TypeToken, type.Name));
		Assert.Equal(("System.Object", "150", "33554433"),
			(type.BaseType, type.BaseTypeModuleHandle, type.BaseTypeToken));
		Assert.Equal((1L, 0L), (type.ObjectType, type.ElementType));
		Assert.Equal(
		[
			new DotNetField("health", 16, false, "System.Int32", 8, 1),
			new DotNetField("Instance", 0, true, "Game.Player", 18, 0x16, "7FF0A000"),
			new DotNetField("Max", 0, true, null, 8, 0x56)
		], type.Fields);
	}

	[Fact]
	public void DotNetV2_GetType_PositionalShapeWithoutAParentLookup_KeepsNumbersOutOfTypeNames()
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs("""
		             collector = {}
		             getDotNetDataCollector = function() return collector end
		             collector.getTypeDefData = function() return {1, 0, 0, 0, 0, 'Legacy', {{24, 8, 'count'}, {32, 'System.String', 'label'}}} end
		             """);

		LuaJsonResult<DotNetTypeDetails> result = ReadJson(
			LuaToolRuntime.BuildSource(DotNetLuaScripts.Type, 100, new object?[] { "1", "2", 1 }),
			DotNetJsonContext.Default.DotNetTypeDetails);

		Assert.False(result.IsError, result.Error?.Message);
		Assert.Equal(("Legacy", null, null), (result.Value!.Name, result.Value.BaseType, result.Value.BaseTypeToken));
		Assert.Equal([new DotNetField("count", 24, false, null, 8)], result.Value.Fields);
	}

	[Fact]
	public void DotNetV2_GetType_ARootTypeHasNoBaseType()
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs(DotNetCollectorStubs);

		LuaJsonResult<DotNetTypeDetails> result = ReadJson(
			LuaToolRuntime.BuildSource(DotNetLuaScripts.Type, 100, new object?[] { "150", "33554433", 512 }),
			DotNetJsonContext.Default.DotNetTypeDetails);

		Assert.False(result.IsError, result.Error?.Message);
		Assert.Equal(("System.Object", null, null, null),
			(result.Value!.Name, result.Value.BaseType, result.Value.BaseTypeModuleHandle,
				result.Value.BaseTypeToken));
		Assert.Empty(result.Value.Fields);
	}

	[Theory]
	[InlineData(null, 0, 100, "Game.Player,Game.Enemy,PlayerStats,System.Object", 4, null)]
	[InlineData("PLAYER", 1, 1, "PlayerStats", 2, null)]
	[InlineData("player", 0, 1, "Game.Player", 2, 1)]
	// Matched literally, never as a Lua pattern.
	[InlineData(".", 0, 100, "Game.Player,Game.Enemy,System.Object", 3, null)]
	public void DotNetV2_ListTypes_NameContainsFiltersFullNamesBeforeThePage(string? filter, int offset, int limit,
		string names, int total, int? nextOffset)
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs("""
		             collector = {}
		             getDotNetDataCollector = function() return collector end
		             collector.enumTypeDefs = function()
		               return {{TypeDefToken = 1, Name = 'Game.Player'}, {TypeDefToken = 2, Name = 'Game.Enemy'},
		                 {3, 'PlayerStats', 0}, {TypeDefToken = 4, Name = 'System.Object'}}
		             end
		             """);

		LuaJsonResult<DotNetTypePage> result = ReadJson(
			LuaToolRuntime.BuildSource(DotNetLuaScripts.Types, 100, new object?[] { "140", offset, limit, filter }),
			DotNetJsonContext.Default.DotNetTypePage);

		Assert.False(result.IsError, result.Error?.Message);
		Assert.Equal(names, string.Join(',', result.Value!.Types.Select(static type => type.Name)));
		Assert.Equal((total, nextOffset), (result.Value.Total, result.Value.NextOffset));
	}

	[Fact]
	public void DotNetV2_ListMethods_NameContainsFiltersMethodNamesBeforeThePage()
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs("""
		             collector = {}
		             getDotNetDataCollector = function() return collector end
		             collector.getTypeDefMethods = function()
		               return {{MethodToken = 1, Name = 'Heal'}, {MethodToken = 2, Name = 'TakeDamage'},
		                 {3, 'GetHealth', 0, 0, 0, 0x7FF0C000}, {MethodToken = 4, Name = 'Update'}}
		             end
		             """);

		LuaJsonResult<DotNetMethodPage> result = ReadJson(
			LuaToolRuntime.BuildSource(DotNetLuaScripts.Methods, 100,
				new object?[] { "140", "33554437", 0, 100, "HEAL" }), DotNetJsonContext.Default.DotNetMethodPage);

		Assert.False(result.IsError, result.Error?.Message);
		Assert.Equal(["Heal", "GetHealth"], result.Value!.Methods.Select(static method => method.Name));
		Assert.Equal(("7FF0C000", 2), (result.Value.Methods[1].NativeCode, result.Value.Total));
	}

	[Fact]
	public void DotNetV2_GetObject_ReadsFieldValuesByElementType()
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs("""
		             collector = {}
		             getDotNetDataCollector = function() return collector end
		             getAddressSafe = function(expression) if expression == 'player' then return 0x5008 end return nil end
		             collector.getAddressData = function(address)
		               observedAddress = address
		               return {StartAddress = 0x5000, ClassName = 'Game.Player', Fields = {
		                 {Name = 'alive', FieldType = 2, Offset = 0x10}, {Name = 'letter', FieldType = 3, Offset = 0x12},
		                 {Name = 'delta', FieldType = 4, Offset = 0x14}, {Name = 'level', FieldType = 5, Offset = 0x15},
		                 {Name = 'tilt', FieldType = 6, Offset = 0x16}, {Name = 'health', FieldType = 8, FieldTypeClassName = 'System.Int32', Offset = 0x18},
		                 {Name = 'flags', FieldType = 9, Offset = 0x1C}, {Name = 'score', FieldType = 10, Offset = 0x20},
		                 {Name = 'seed', FieldType = 11, Offset = 0x28}, {Name = 'speed', FieldType = 12, Offset = 0x30},
		                 {Name = 'mass', FieldType = 13, Offset = 0x38}, {Name = 'name', FieldType = 14, Offset = 0x40},
		                 {Name = 'position', FieldType = 17, Offset = 0x48}, {Name = 'handle', FieldType = 24, Offset = 0x50},
		                 {Name = 'gone', FieldType = 8, Offset = 0x60}, {Name = 'Instance', FieldType = 18, Offset = 0, IsStatic = true, Address = 0x9000}}}
		             end
		             targetIs64Bit = function() return true end
		             bytes = {[0x5010] = 1, [0x5014] = 0xFE, [0x5015] = 200}
		             words = {[0x5012] = 65, [0x5016] = 0xFFFF}
		             dwords = {[0x5018] = 0xFFFFFF9C, [0x501C] = 0x80000000}
		             qwords = {[0x5020] = -5, [0x5028] = -1, [0x5050] = 0x1234}
		             pointers = {[0x5040] = 0x7FF0B000, [0x9000] = 0xABC}
		             readBytes = function(at, count) assert(count == 1) return bytes[at] end
		             readSmallInteger = function(at) return words[at] end
		             readInteger = function(at) return dwords[at] end
		             readQword = function(at) return qwords[at] end
		             readFloat = function(at) if at == 0x5030 then return 1.5 end end
		             readDouble = function(at) if at == 0x5038 then return 0 / 0 end end
		             readPointer = function(at) return pointers[at] end
		             """);

		LuaJsonResult<DotNetObject> result = ReadJson(
			LuaToolRuntime.BuildSource(DotNetLuaScripts.Object, 100, new object?[] { "player", 512 }),
			DotNetJsonContext.Default.DotNetObject);

		Assert.False(result.IsError, result.Error?.Message);
		Assert.Equal(0x5008L, ReadGlobal("observedAddress"));
		Assert.Equal(("5000", "Game.Player"), (result.Value!.Address, result.Value.TypeName));
		Assert.Equal(
		[
			new DotNetObjectField("alive", "true", null, 0x10, 2),
			new DotNetObjectField("letter", "65", null, 0x12, 3),
			new DotNetObjectField("delta", "-2", null, 0x14, 4),
			new DotNetObjectField("level", "200", null, 0x15, 5),
			new DotNetObjectField("tilt", "-1", null, 0x16, 6),
			new DotNetObjectField("health", "-100", "System.Int32", 0x18, 8),
			new DotNetObjectField("flags", "2147483648", null, 0x1C, 9),
			new DotNetObjectField("score", "-5", null, 0x20, 10),
			new DotNetObjectField("seed", "18446744073709551615", null, 0x28, 11),
			new DotNetObjectField("speed", "1.5", null, 0x30, 12),
			new DotNetObjectField("mass", "NaN", null, 0x38, 13),
			new DotNetObjectField("name", "7FF0B000", null, 0x40, 14),
			new DotNetObjectField("position", null, null, 0x48, 17),
			new DotNetObjectField("handle", "4660", null, 0x50, 24),
			new DotNetObjectField("gone", null, null, 0x60, 8),
			new DotNetObjectField("Instance", "ABC", null, 0, 18)
		], result.Value.Fields);
	}

	[Theory]
	[InlineData("{{Name = 'speed', CType = 12}, {Name = 'target', CType = 18}}, 'System.Void (System.Single, Game.Player)'",
		"System.Void (System.Single, Game.Player)")]
	[InlineData("{{'speed', 12}, {'target', 18}}", null)]
	public void DotNetV2_GetMethodParameters_ReadsNamedAndPositionalShapes(string returned, string? signature)
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs($$"""
		               collector = {}
		               getDotNetDataCollector = function() return collector end
		               collector.getMethodParameters = function(module, token)
		                 observedModule, observedToken = module, token
		                 return {{returned}}
		               end
		               """);

		LuaJsonResult<DotNetMethodParameters> result = ReadJson(
			LuaToolRuntime.BuildSource(DotNetLuaScripts.MethodParameters, 100,
				new object?[] { "140", "100663297" }), DotNetJsonContext.Default.DotNetMethodParameters);

		Assert.False(result.IsError, result.Error?.Message);
		Assert.Equal(("140", "100663297", signature),
			(result.Value!.ModuleHandle, result.Value.MethodToken, result.Value.Signature));
		Assert.Equal([new DotNetParameter(0, "speed", 12), new DotNetParameter(1, "target", 18)],
			result.Value.Parameters);
		Assert.Equal((140L, 0x6000001L), (ReadGlobal("observedModule"), ReadGlobal("observedToken")));
	}

	[Theory]
	[InlineData("collector.getMethodParameters = function() return nil end", "140", "not_found")]
	[InlineData("getDotNetDataCollector = function() return nil end", "140", "not_attached")]
	[InlineData("", "module", "invalid_argument")]
	public void DotNetV2_GetMethodParameters_RefusesWithoutAnAnswer(string stubs, string module, string kind)
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs("""
		             collector = {getMethodParameters = function() return {} end}
		             getDotNetDataCollector = function() return collector end
		             """);
		InstallStubs(stubs);

		LuaJsonResult<DotNetMethodParameters> result = ReadJson(
			LuaToolRuntime.BuildSource(DotNetLuaScripts.MethodParameters, 100, new object?[] { module, "1" }),
			DotNetJsonContext.Default.DotNetMethodParameters);

		Assert.True(result.IsError);
		Assert.Equal((kind, "not_started"), (result.Error!.Kind, result.Error.HostEffect));
	}
}
