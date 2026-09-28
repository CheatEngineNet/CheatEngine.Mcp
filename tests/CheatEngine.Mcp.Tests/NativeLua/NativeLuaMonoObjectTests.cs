using CheatEngine.Client.Results;
using CheatEngine.Mcp.Tests.Support;
using CheatEngine.Mcp.Tools.Mono;

namespace CheatEngine.Mcp.Tests.NativeLua;

/// <summary>
///     Native Lua coverage for <see cref="MonoLuaScripts.Object" /> (<c>mono_get_object</c>): the object header is
///     found by reading target memory only, a class is trusted only once its own image's class list names it, and the
///     collector is never asked about the address itself. A collector call that fails stops the body, and Cheat
///     Engine's IL2CPP memory-scan guess of a class list is never used.
/// </summary>
public sealed partial class NativeLuaToolRuntimeTests
{
	/// <summary>
	///     A 64-bit Mono target. The <c>Game.Player</c> object at 0x5000 starts with its vtable 0x9000, whose first
	///     pointer is the class 0x7000; the class names its image 0x3000 eight pointers in, as a MonoClass does. Fields
	///     below the probed address hold decoys: an unaligned native pointer, a reference to an <c>Enemy</c> object, a
	///     System.Type handle pointing into the Player class, the raw class pointer and a string reference. Every
	///     collector call records its argument, and the collector commands that dereference a guessed object are traps.
	/// </summary>
	private const string MonoObjectStubs = """
	                                       targetIs64Bit = function() return true end
	                                       pointers = {
	                                         [0x5000] = 0x9000, [0x5010] = 0x1234, [0x5018] = 0x6000, [0x5020] = 0x70A0,
	                                         [0x5028] = 0x7000, [0x5030] = 0x6100, [0x9900] = 0x6000,
	                                         [0x6000] = 0xA000, [0xA000] = 0x7100, [0x6100] = 0xB000, [0xB000] = 0x7200,
	                                         [0x9000] = 0x7000, [0x7000] = 0x7000, [0x7040] = 0x3000, [0x70A0] = 0x7000,
	                                         [0x7100] = 0x7100, [0x7140] = 0x3000, [0x7200] = 0x7200, [0x7240] = 0x3100}
	                                       qwords = {[0x5010] = 0x1234}
	                                       dwords = {[0x5058] = 100, [0x505C] = 25}
	                                       readPointer = function(at) return pointers[at] end
	                                       readQword = function(at) return qwords[at] end
	                                       readInteger = function(at) return dwords[at] end
	                                       readFloat = function(at) if at == 0x5038 then return 1.5 end end
	                                       getAddressSafe = function(expression)
	                                         if expression == 'player' then return 0x505C end
	                                         if expression == 'far' then return 0x5100 end
	                                         if expression == 'start' then return 0x5000 end
	                                         return nil
	                                       end
	                                       collector = {}
	                                       local function record(name, value) collector[#collector + 1] = name .. ':' .. string.format('%X', value) end
	                                       mono_enumAssemblies = function() collector[#collector + 1] = 'assemblies' return {0x100, 0x200} end
	                                       mono_getImageFromAssembly = function(assembly) record('image', assembly) return assembly == 0x100 and 0x3000 or 0x3100 end
	                                       mono_image_enumClasses = function(image)
	                                         record('classes', image)
	                                         if image == 0x3000 then
	                                           return {{class = 0x7000, classname = 'Player', namespace = 'Game'}, {class = 0x7100, classname = 'Enemy', namespace = 'Game'}}
	                                         end
	                                         return {{class = 0x7200, classname = 'String', namespace = 'System'}}
	                                       end
	                                       mono_class_enumFields = function(class, includeParents)
	                                         record('fields', class)
	                                         observedParents = includeParents
	                                         return {
	                                           {field = 1, name = 'm_CachedPtr', typename = 'System.IntPtr', monotype = 0x18, offset = 0x10, isStatic = false, isConst = false},
	                                           {field = 2, name = 'target', typename = 'Game.Enemy', monotype = 0x12, offset = 0x18, isStatic = false, isConst = false},
	                                           {field = 3, name = 'name', typename = 'System.String', monotype = 0x0E, offset = 0x30, isStatic = false, isConst = false},
	                                           {field = 4, name = 'speed', typename = 'System.Single', monotype = 0x0C, offset = 0x38, isStatic = false, isConst = false},
	                                           {field = 5, name = 'position', typename = 'UnityEngine.Vector3', monotype = 0x11, offset = 0x40, isStatic = false, isConst = false},
	                                           {field = 6, name = 'health', typename = 'System.Int32', monotype = 0x08, offset = 0x58, isStatic = false, isConst = false},
	                                           {field = 7, name = 'armor', typename = 'System.Int32', monotype = 0x08, offset = 0x5C, isStatic = false, isConst = false},
	                                           {field = 8, name = 'Instance', typename = 'Game.Player', monotype = 0x12, offset = 0, isStatic = true, isConst = false, staticAddress = 0x9900},
	                                           {field = 9, name = 'MaxHealth', typename = 'System.Int32', monotype = 0x08, offset = 0, isStatic = true, isConst = true}}
	                                       end
	                                       mono_object_getClass = function() getClassCalls = (getClassCalls or 0) + 1 return 0x7000, 'Player' end
	                                       mono_object_findRealStartOfObject = function() findStartCalls = (findStartCalls or 0) + 1 return 0x5000, 0x7000, 'Player' end
	                                       """;

	/// <summary>
	///     <c>fail(name, value)</c> records a collector call, then fails it the way Cheat Engine's pipe calls do: they
	///     swallow a timeout or a pipe error, set <c>libmono.fail</c> and return <c>nil</c> or a partial list.
	/// </summary>
	private const string MonoPipeFailureStubs = """
	                                            fail = function(name, value)
	                                              collector[#collector + 1] = value and string.format('%s:%X', name, value) or name
	                                              libmono.fail = true
	                                            end
	                                            """;

	/// <summary>
	///     An IL2CPP collector that reports no classes for an image, where Cheat Engine's <c>mono_image_enumClasses</c>
	///     falls back to <c>mono_image_enumClasses_il2cppfallback</c>, a memory scan whose guesses the collector never
	///     confirmed. The guess would name the Player class.
	/// </summary>
	private const string Il2CppUnlistedImageStubs = """
	                                                libmono.IL2CPP = true
	                                                pointers[0x5000] = 0x7000
	                                                pointers[0x7000] = 0x3000
	                                                guess = function(image)
	                                                  guesses = (guesses or 0) + 1
	                                                  return {{class = 0x7000, classname = 'Player', namespace = 'Game'}}
	                                                end
	                                                mono_image_enumClasses_il2cppfallback = guess
	                                                mono_image_enumClasses = function(image)
	                                                  collector[#collector + 1] = string.format('classes:%X', image)
	                                                  if libmono.IL2CPP then return mono_image_enumClasses_il2cppfallback(image) end
	                                                end
	                                                """;

	[Fact]
	public void MonoGetObject_Mono_FindsTheHeaderBelowTheAddressThroughAListedClassAndReadsTheFields()
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs(MonoAttachedStubs);
		InstallStubs(MonoObjectStubs);

		LuaJsonResult<MonoObject> result = ReadJson(
			LuaToolRuntime.BuildSource(MonoLuaScripts.Object, 100, new object?[] { "player", 4096, 512 }),
			MonoJsonContext.Default.MonoObject);
		InstallStubs("calls = table.concat(collector, ' ')");

		Assert.False(result.IsError, result.Error?.Message);
		MonoObject found = result.Value!;
		Assert.Equal(("5000", 0x5CL, "28672", "Player", "Game", "12288", 9),
			(found.Address, found.OffsetInObject, found.ClassHandle, found.ClassName, found.Namespace,
				found.ImageHandle, found.TotalFields));
		Assert.Equal(
		[
			new MonoObjectField("m_CachedPtr", 0x10, false, false, "System.IntPtr", 0x18, "5010", "4660"),
			new MonoObjectField("target", 0x18, false, false, "Game.Enemy", 0x12, "5018", "6000"),
			new MonoObjectField("name", 0x30, false, false, "System.String", 0x0E, "5030", "6100"),
			new MonoObjectField("speed", 0x38, false, false, "System.Single", 0x0C, "5038", "1.5"),
			new MonoObjectField("position", 0x40, false, false, "UnityEngine.Vector3", 0x11, "5040"),
			new MonoObjectField("health", 0x58, false, false, "System.Int32", 0x08, "5058", "100"),
			new MonoObjectField("armor", 0x5C, false, false, "System.Int32", 0x08, "505C", "25"),
			new MonoObjectField("Instance", 0, true, false, "Game.Player", 0x12, "9900", "6000"),
			new MonoObjectField("MaxHealth", 0, true, true, "System.Int32", 0x08)
		], found.Fields);
		// Only the image the Player class names is enumerated, and the collector only ever sees the listed class.
		Assert.Equal("assemblies image:100 image:200 classes:3000 fields:7000", ReadGlobal("calls"));
		Assert.Equal(true, ReadGlobal("observedParents"));
		Assert.Null(ReadGlobal("getClassCalls"));
		Assert.Null(ReadGlobal("findStartCalls"));
	}

	[Fact]
	public void MonoGetObject_Il2Cpp_AcceptsAHeaderThatIsTheListedClassItselfAndCopiesAtMostMaximumFields()
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs(MonoAttachedStubs);
		InstallStubs(MonoObjectStubs);
		// IL2CPP: the header is the Il2CppClass, which starts with its image; statics may have no address.
		InstallStubs("""
		             libmono.IL2CPP = true
		             pointers[0x5000] = 0x7000
		             pointers[0x7000] = 0x3000
		             mono_class_enumFields = function(class)
		               return {
		                 {name = 'Instance', typename = 'Game.Player', monotype = 0x12, offset = 0, isStatic = true, isConst = false},
		                 {name = 'health', typename = 'System.Int32', monotype = 0x08, offset = 0x58, isStatic = false, isConst = false},
		                 {name = 'armor', typename = 'System.Int32', monotype = 0x08, offset = 0x5C, isStatic = false, isConst = false}}
		             end
		             """);

		LuaJsonResult<MonoObject> result = ReadJson(
			LuaToolRuntime.BuildSource(MonoLuaScripts.Object, 100, new object?[] { "start", 0, 2 }),
			MonoJsonContext.Default.MonoObject);

		Assert.False(result.IsError, result.Error?.Message);
		Assert.Equal(("5000", 0L, "28672", "Player", 3),
			(result.Value!.Address, result.Value.OffsetInObject, result.Value.ClassHandle, result.Value.ClassName,
				result.Value.TotalFields));
		Assert.Equal(
		[
			new MonoObjectField("Instance", 0, true, false, "Game.Player", 0x12),
			new MonoObjectField("health", 0x58, false, false, "System.Int32", 0x08, "5058", "100")
		], result.Value.Fields);
		Assert.Null(ReadGlobal("getClassCalls"));
	}

	[Theory]
	// The object starts 0x100 bytes below the address, past the searched range.
	[InlineData("far", 0xF8, "", "not_found", "not_started", null)]
	// Zero backtrack only accepts an address that is itself an aligned object start.
	[InlineData("player", 0, "", "not_found", "not_started", null)]
	// A class structure that names no collector image.
	[InlineData("player", 4096, "pointers[0x7040] = 0x3200", "not_found", "not_started", null)]
	// A generic instance class: the image it names does not list it, so nothing is trusted.
	[InlineData("player", 4096, "pointers[0x9000] = 0x7300; pointers[0x7340] = 0x3000", "unsupported", "not_started",
		"at 5000,")]
	// An array object between the Player header and the address holds the address: Player is never reported.
	[InlineData("player", 4096, "pointers[0x5040] = 0xC000; pointers[0xC000] = 0x7300; pointers[0x7308] = 0x3000",
		"unsupported", "not_started", "at 5040,")]
	// A Mono image the collector reports no classes for (no pipe failure) lists none of them.
	[InlineData("player", 4096, "mono_image_enumClasses = function() return nil end", "unsupported", "not_started",
		"at 5000,")]
	[InlineData("unknown", 4096, "", "invalid_argument", "not_started", null)]
	[InlineData("player", 4096, "libmono.ProcessID = 78", "not_attached", "not_started", null)]
	[InlineData("player", 4096, "mono_enumAssemblies = function() return nil end", "host_refused", "unknown", null)]
	[InlineData("player", 4096, "mono_class_enumFields = function() return nil end", "host_refused", "not_started",
		null)]
	public void MonoGetObject_WithoutAListedClass_RefusesAndNeverAsksTheCollectorAboutTheAddress(string address,
		int maxBacktrack, string stubs, string kind, string effect, string? header)
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs(MonoAttachedStubs);
		InstallStubs(MonoObjectStubs);
		InstallStubs(stubs);

		LuaJsonResult<MonoObject> result = ReadJson(
			LuaToolRuntime.BuildSource(MonoLuaScripts.Object, 100, new object?[] { address, maxBacktrack, 512 }),
			MonoJsonContext.Default.MonoObject);

		Assert.True(result.IsError);
		Assert.Equal((kind, effect), (result.Error!.Kind, result.Error.HostEffect));
		Assert.Null(ReadGlobal("getClassCalls"));
		Assert.Null(ReadGlobal("findStartCalls"));
		if (kind is "not_found" or "unsupported")
		{
			Assert.StartsWith("The collector was not asked about the address.", result.Error.Hint,
				StringComparison.Ordinal);
			InstallStubs("calls = table.concat(collector, ' ')");
			Assert.DoesNotContain("fields:", (string) ReadGlobal("calls")!, StringComparison.Ordinal);
		}

		if (header is not null)
		{
			Assert.Contains(header, result.Error.Message, StringComparison.Ordinal);
		}
	}

	[Fact]
	public void MonoGetObject_TheNearestListedHeaderWinsAndEachImageIsEnumeratedOnce()
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs(MonoAttachedStubs);
		InstallStubs(MonoObjectStubs);
		// An Enemy object at 0x5040 lies between the Player header and the address, so it contains the address.
		InstallStubs("""
		             pointers[0x5040] = 0xA000
		             mono_class_enumFields = function(class) collector[#collector + 1] = string.format('fields:%X', class) return {} end
		             """);

		LuaJsonResult<MonoObject> result = ReadJson(
			LuaToolRuntime.BuildSource(MonoLuaScripts.Object, 100, new object?[] { "player", 4096, 512 }),
			MonoJsonContext.Default.MonoObject);
		InstallStubs("calls = table.concat(collector, ' ')");

		Assert.False(result.IsError, result.Error?.Message);
		Assert.Equal(("5040", 0x1CL, "28928", "Enemy", 0),
			(result.Value!.Address, result.Value.OffsetInObject, result.Value.ClassHandle, result.Value.ClassName,
				result.Value.TotalFields));
		Assert.Empty(result.Value.Fields);
		Assert.Equal("assemblies image:100 image:200 classes:3000 fields:7100", ReadGlobal("calls"));
	}

	[Theory]
	[InlineData("mono_enumAssemblies = function() fail('assemblies') end", "invalid_state",
		"while Cheat Engine enumerated Mono assemblies,", "assemblies")]
	[InlineData("mono_getImageFromAssembly = function(assembly) fail('image', assembly) end", "invalid_state",
		"while Cheat Engine read the images of Mono assemblies,", "assemblies image:100")]
	// Without the check, a missing class list would be reported as a generic instance or an array class.
	[InlineData("mono_image_enumClasses = function(image) fail('classes', image) end", "invalid_state",
		"while Cheat Engine enumerated the classes of a Mono image,", "assemblies image:100 image:200 classes:3000")]
	// A partial list that stops before the Player class.
	[InlineData(
		"mono_image_enumClasses = function(image) fail('classes', image) "
		+ "return {{class = 0x7100, classname = 'Enemy'}} end",
		"invalid_state", "while Cheat Engine enumerated the classes of a Mono image,",
		"assemblies image:100 image:200 classes:3000")]
	[InlineData(
		"mono_class_enumFields = function(class) fail('fields', class) "
		+ "return {{name = 'hp', monotype = 8, offset = 88}} end",
		"invalid_state", "while Cheat Engine listed the fields of the object's class,",
		"assemblies image:100 image:200 classes:3000 fields:7000")]
	// Abort in Cheat Engine's timeout dialog sets abort and terminates the collector connection.
	[InlineData("mono_image_enumClasses = function(image) libmono.abort = true fail('classes', image) end",
		"not_attached", "Cheat Engine aborted its Mono collector connection while it enumerated the classes",
		"assemblies image:100 image:200 classes:3000")]
	public void MonoGetObject_CollectorPipeFailure_StopsAtTheFailedCallInsteadOfReportingAnUnlistedClass(string stubs,
		string kind, string message, string calls)
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs(MonoAttachedStubs);
		InstallStubs(MonoObjectStubs);
		InstallStubs(MonoPipeFailureStubs);
		InstallStubs(stubs);

		LuaJsonResult<MonoObject> result = ReadJson(
			LuaToolRuntime.BuildSource(MonoLuaScripts.Object, 100, new object?[] { "player", 4096, 512 }),
			MonoJsonContext.Default.MonoObject);
		InstallStubs("calls = table.concat(collector, ' ')");

		Assert.True(result.IsError);
		Assert.Equal((kind, "not_started"), (result.Error!.Kind, result.Error.HostEffect));
		Assert.Contains(message, result.Error.Message, StringComparison.Ordinal);
		Assert.EndsWith("so the object was not read.", result.Error.Message, StringComparison.Ordinal);
		if (kind == "not_attached")
		{
			Assert.Equal(MonoRecoveryHint, result.Error.Hint);
		}
		else
		{
			Assert.Equal("Retry the call once: the next Mono call closes the failed pipe and reconnects to the "
				+ "collector, and says how to recover when it cannot.", result.Error.Hint);
		}

		// No collector call follows the failed one: a timed-out pipe answers out of step.
		Assert.Equal(calls, ReadGlobal("calls"));
		Assert.Null(ReadGlobal("getClassCalls"));
		Assert.Null(ReadGlobal("findStartCalls"));
	}

	[Fact]
	public void MonoGetObject_Il2CppImageWithoutReportedClasses_IsRefusedWithoutCheatEngineMemoryScanGuess()
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs(MonoAttachedStubs);
		InstallStubs(MonoObjectStubs);
		InstallStubs(Il2CppUnlistedImageStubs);

		LuaJsonResult<MonoObject> result = ReadJson(
			LuaToolRuntime.BuildSource(MonoLuaScripts.Object, 100, new object?[] { "start", 0, 512 }),
			MonoJsonContext.Default.MonoObject);
		InstallStubs("""
		             calls = table.concat(collector, ' ')
		             restored = rawequal(mono_image_enumClasses_il2cppfallback, guess)
		             """);

		Assert.True(result.IsError);
		Assert.Equal(("unsupported", "not_started"), (result.Error!.Kind, result.Error.HostEffect));
		Assert.Contains("reported no classes for the image 3000 that the nearest object header, at 5000,",
			result.Error.Message, StringComparison.Ordinal);
		Assert.StartsWith("The collector was not asked about the address.", result.Error.Hint,
			StringComparison.Ordinal);
		// The memory-scan guess never ran, the collector never saw the class, and the global is Cheat Engine's again.
		Assert.Null(ReadGlobal("guesses"));
		Assert.Equal("assemblies image:100 image:200 classes:3000", ReadGlobal("calls"));
		Assert.Equal(true, ReadGlobal("restored"));
	}

	[Fact]
	public void MonoGetObject_ClassEnumerationThatRaises_RestoresCheatEngineFallbackAndFails()
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs(MonoAttachedStubs);
		InstallStubs(MonoObjectStubs);
		InstallStubs(Il2CppUnlistedImageStubs);
		InstallStubs("mono_image_enumClasses = function() error('enumeration raised') end");

		AssertFailure(LuaToolRuntime.BuildSource(MonoLuaScripts.Object, 100, new object?[] { "start", 0, 512 }),
			CheatEngineHostEffect.Started);
		InstallStubs("restored = rawequal(mono_image_enumClasses_il2cppfallback, guess)");

		Assert.Equal(true, ReadGlobal("restored"));
		Assert.Null(ReadGlobal("guesses"));
	}
}
