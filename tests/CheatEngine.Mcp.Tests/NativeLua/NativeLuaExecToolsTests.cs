using CheatEngine.Mcp.Core.Contract;
using CheatEngine.Mcp.Core.Features;
using CheatEngine.Mcp.Tools.Exec;

namespace CheatEngine.Mcp.Tests.NativeLua;

/// <summary>
///     Native Lua coverage for the fixed <c>exec_*</c> bodies against stubs shaped like Cheat Engine 7.7's
///     <c>injectDotNetDLL</c> (<c>DotNetInject.lua</c>), <c>executeCodeLocal</c>, <c>executeCodeEx</c> and
///     <c>executeMethod</c>.
/// </summary>
public sealed partial class NativeLuaToolRuntimeTests
{
	private const string InjectDotNetStubs = """
	                                         injectCalls = 0
	                                         injectResults = {42}
	                                         injectDotNetDLL = function(...)
	                                           injectCalls = injectCalls + 1
	                                           injectCount = select('#', ...)
	                                           injectPath, injectClass, injectMethod, injectParameter = ...
	                                           return table.unpack(injectResults, 1, injectResults.n or 1)
	                                         end
	                                         """;

	private const string CallLocalStubs = """
	                                      getAddressSafe = function(expression, localOnly)
	                                        if expression == 'localcall' and localOnly == true then return 0x1000 end
	                                        return nil
	                                      end
	                                      executeCodeLocal = function(address, parameter)
	                                        localParameter = parameter
	                                        return math.tointeger(tonumber(tostring(parameter), 16))
	                                      end
	                                      """;

	/// <summary>
	///     Models the stub Cheat Engine 7.7's <c>executeMethod</c> assembles (<c>LuaHandler.pas</c>), which
	///     <c>executeCodeEx</c> runs without an instance: it sets the instance register first, then places every
	///     argument by slot. On x64, slots 0 to 3 load RCX, RDX, R8 and R9 (XMM0 to XMM3 for float and double) and
	///     later slots go through RAX to the stack; on x86 every argument goes to the stack, and a double writes its
	///     low half twice. <c>lastCall</c> lists what the called code would find in each register and stack slot.
	/// </summary>
	private const string CallStubs = """
	                                 methodCalls = 0
	                                 remoteCalls = 0
	                                 wideTarget = false
	                                 targetIs64Bit = function() return wideTarget end
	                                 local registers = {[0] = 'ax', 'cx', 'dx', 'bx', 'sp', 'bp', 'si', 'di'}
	                                 local arguments = {[0] = 'rcx', 'rdx', 'r8', 'r9'}
	                                 local function run(instance, ...)
	                                   local state, slot = {}, 0
	                                   if instance ~= nil then
	                                     local name = instance.regnr >= 8 and ('r' .. instance.regnr) or
	                                       ((wideTarget and 'r' or 'e') .. registers[instance.regnr])
	                                     state[name] = instance.classinstance
	                                   end
	                                   local parameters = table.pack(...)
	                                   for i = 1, parameters.n do
	                                     local parameter = parameters[i]
	                                     if wideTarget and slot < 4 then
	                                       local name = parameter.type == 0 and arguments[slot] or ('xmm' .. slot)
	                                       state[name] = parameter.value
	                                     elseif wideTarget then
	                                       state.rax = parameter.value
	                                       state['[' .. slot .. ']'] = parameter.value
	                                     elseif parameter.type == 2 then
	                                       local low = string.unpack('<I4', string.pack('<d', parameter.value))
	                                       state['[' .. slot .. ']'] = low
	                                       slot = slot + 1
	                                       state['[' .. slot .. ']'] = low
	                                     else
	                                       state['[' .. slot .. ']'] = parameter.value
	                                     end
	                                     slot = slot + 1
	                                   end
	                                   local names = {}
	                                   for name in pairs(state) do names[#names + 1] = name end
	                                   table.sort(names)
	                                   for index = 1, #names do
	                                     names[index] = names[index] .. '=' .. tostring(state[names[index]])
	                                   end
	                                   lastCall = table.concat(names, ' ')
	                                 end
	                                 executeMethod = function(callmethod, timeout, address, instance, ...)
	                                   methodCalls = methodCalls + 1
	                                   methodRegister = instance.regnr
	                                   methodInstance = instance.classinstance
	                                   run(instance, ...)
	                                   return 5
	                                 end
	                                 executeCodeEx = function(callmethod, timeout, address, ...)
	                                   remoteCalls = remoteCalls + 1
	                                   run(nil, ...)
	                                   return 5
	                                 end
	                                 """;

	[Fact]
	public void ExecV2_InjectDotNet_PassesTheFourStringsVerbatimAndNoTimeout()
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs(InjectDotNetStubs);
		ToolDispatch dispatch = CreateNativeDispatch(new McpFeatureOptions());
		const string path = "C:\\Mods\\Mod Loader (x64) \u00E9\\My.Mod-1.0.dll";
		const string parameter = @"key=value, 100% // not a comment [ENABLE] \ %s";

		ExecDotNetInjection result = dispatch.RunLua(CheatEngineToolNames.ExecInjectDotNet, ExecScripts.InjectDotNet,
			ExecJsonContext.Default.ExecDotNetInjection, Token, path, "MyMod.Loader+Entry", "Init", parameter);

		Assert.Equal(new ExecDotNetInjection(path, "MyMod.Loader+Entry", "Init", "42"), result);
		Assert.Equal((1L, 4L), (ReadGlobal("injectCalls"), ReadGlobal("injectCount")));
		Assert.Equal((path, "MyMod.Loader+Entry", "Init", parameter),
			(ReadGlobal("injectPath"), ReadGlobal("injectClass"), ReadGlobal("injectMethod"),
				ReadGlobal("injectParameter")));
	}

	[Theory]
	[InlineData("")]
	[InlineData("argument")]
	public void ExecV2_InjectDotNet_RefusesATargetWithAMonoCollectorBeforeInjecting(string parameter)
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs(InjectDotNetStubs + "\nmonopipe = {}");
		ToolDispatch dispatch = CreateNativeDispatch(new McpFeatureOptions());

		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() => dispatch.RunLua(
			CheatEngineToolNames.ExecInjectDotNet, ExecScripts.InjectDotNet,
			ExecJsonContext.Default.ExecDotNetInjection, Token, @"C:\Mods\Loader.dll", "Loader", "Init", parameter));

		Assert.Equal((ToolErrorKind.Unsupported, ToolHostEffect.NotStarted),
			(exception.Error.Kind, exception.Error.HostEffect));
		Assert.Contains("Mono data collector", exception.Error.Message, StringComparison.Ordinal);
		Assert.Equal(0L, ReadGlobal("injectCalls"));
	}

	[Theory]
	[InlineData("nil, -4", ToolErrorKind.Unsupported, ToolHostEffect.NotStarted, "no .NET Framework or .NET Core")]
	[InlineData("nil, -1", ToolErrorKind.HostRefused, ToolHostEffect.Unknown, "(Cheat Engine code -1)")]
	[InlineData("nil, 0x80131513", ToolErrorKind.HostRefused, ToolHostEffect.Unknown,
		"(Cheat Engine code 0x80131513)")]
	[InlineData("false", ToolErrorKind.HostRefused, ToolHostEffect.Unknown,
		"did not confirm managed assembly injection;")]
	public void ExecV2_InjectDotNet_MapsCheatEngineFailureCodes(string results, ToolErrorKind kind,
		ToolHostEffect effect, string message)
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs(InjectDotNetStubs + $"\ninjectResults = table.pack({results})");
		ToolDispatch dispatch = CreateNativeDispatch(new McpFeatureOptions());

		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() => dispatch.RunLua(
			CheatEngineToolNames.ExecInjectDotNet, ExecScripts.InjectDotNet,
			ExecJsonContext.Default.ExecDotNetInjection, Token, @"C:\Mods\Loader.dll", "Loader", "Init", ""));

		Assert.Equal((kind, effect), (exception.Error.Kind, exception.Error.HostEffect));
		Assert.Contains(message, exception.Error.Message, StringComparison.Ordinal);
		Assert.Equal(1L, ReadGlobal("injectCalls"));
	}

	[Theory]
	[InlineData("4294967295", "-1")]
	[InlineData("2147483648", "-2147483648")]
	[InlineData("2147483647", "2147483647")]
	[InlineData("0", "0")]
	public void ExecV2_InjectDotNet_RestoresTheSignOfTheUnsignedReturnValueCheatEngineReads(string read,
		string expected)
	{
		using RuntimeScope scope = CreateScope();
		// injectDotNetDLL returns readInteger of the method's int, which is its unsigned 32-bit view.
		InstallStubs(InjectDotNetStubs + $"\ninjectResults = {{{read}}}");
		ToolDispatch dispatch = CreateNativeDispatch(new McpFeatureOptions());

		ExecDotNetInjection result = dispatch.RunLua(CheatEngineToolNames.ExecInjectDotNet, ExecScripts.InjectDotNet,
			ExecJsonContext.Default.ExecDotNetInjection, Token, @"C:\Mods\Loader.dll", "Loader", "Init", "");

		Assert.Equal(expected, result.Result);
	}

	[Fact]
	public void ExecV2_InjectDotNet_ARaisingInjectionIsHostRefusedWithUnknownEffect()
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs("injectDotNetDLL = function() error('Invalid instance') end");
		ToolDispatch dispatch = CreateNativeDispatch(new McpFeatureOptions());

		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() => dispatch.RunLua(
			CheatEngineToolNames.ExecInjectDotNet, ExecScripts.InjectDotNet,
			ExecJsonContext.Default.ExecDotNetInjection, Token, @"C:\Mods\Loader.dll", "Loader", "Init", ""));

		Assert.Equal((ToolErrorKind.HostRefused, ToolHostEffect.Unknown),
			(exception.Error.Kind, exception.Error.HostEffect));
		Assert.DoesNotContain("Cheat Engine code", exception.Error.Message, StringComparison.Ordinal);
	}

	[Theory]
	[InlineData(10L, "A", "10")]
	[InlineData(255L, "FF", "255")]
	[InlineData(0L, "0", "0")]
	[InlineData(-1L, "FFFFFFFFFFFFFFFF", "-1")]
	[InlineData(long.MaxValue, "7FFFFFFFFFFFFFFF", "9223372036854775807")]
	[InlineData(long.MinValue, "8000000000000000", "-9223372036854775808")]
	public void ExecV2_CallLocal_PassesHexadecimalTextThatCheatEngineReadsBackAsTheSameValue(long parameter,
		string passed, string returned)
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs(CallLocalStubs);
		ToolDispatch dispatch = CreateNativeDispatch(new McpFeatureOptions());

		ExecCallResult result = dispatch.RunLua(CheatEngineToolNames.ExecCallLocal, ExecScripts.CallLocal,
			ExecJsonContext.Default.ExecCallResult, Token, "localcall", parameter);

		// The stub resolves a parameter as executeCodeLocal does: lua_isstring, then getAddressFromName's hexadecimal.
		Assert.Equal(passed, ReadGlobal("localParameter"));
		Assert.Equal(new ExecCallResult("1000", returned), result);
	}

	[Theory]
	[InlineData(8)]
	[InlineData(15)]
	public void ExecV2_CallMethod_RefusesAnUpperRegisterOnA32BitTargetBeforeCalling(int register)
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs(CallStubs);
		ToolDispatch dispatch = CreateNativeDispatch(new McpFeatureOptions());

		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() => CallMethod(dispatch,
			register));

		Assert.Equal((ToolErrorKind.InvalidArgument, ToolHostEffect.NotStarted),
			(exception.Error.Kind, exception.Error.HostEffect));
		Assert.StartsWith("classRegister: 8 to 15", exception.Error.Message, StringComparison.Ordinal);
		Assert.Equal((0L, 0L), (ReadGlobal("methodCalls"), ReadGlobal("remoteCalls")));
	}

	[Theory]
	[InlineData(false, 7)]
	[InlineData(false, 1)]
	[InlineData(true, 8)]
	[InlineData(true, 15)]
	public void ExecV2_CallMethod_PassesAnAcceptedRegisterToExecuteMethod(bool wide, int register)
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs(CallStubs + $"\nwideTarget = {(wide ? "true" : "false")}");
		ToolDispatch dispatch = CreateNativeDispatch(new McpFeatureOptions());

		ExecCallResult result = CallMethod(dispatch, register);

		Assert.Equal(new ExecCallResult("1000", "5"), result);
		Assert.Equal((1L, (long) register, 0x2000L),
			(ReadGlobal("methodCalls"), ReadGlobal("methodRegister"), ReadGlobal("methodInstance")));
	}

	/// <summary>
	///     The Microsoft x64 member call: this in RCX, then argument 1 in RDX or XMM1, 2 in R8 or XMM2, 3 in R9 or XMM3
	///     and the rest on the stack from [RSP+20], which is stack slot 4.
	/// </summary>
	public static TheoryData<string, string> X64MemberCalls()
	{
		return new TheoryData<string, string>
		{
			{ "", "rcx=8192" },
			{ "integer:10", "rcx=8192 rdx=10" },
			{ "float:1.5", "rcx=8192 xmm1=1.5" },
			{ "integer:10,integer:20,integer:30", "r8=20 r9=30 rcx=8192 rdx=10" },
			{
				"float:1.5,integer:20,double:2.5,integer:30,integer:40",
				"[4]=30 [5]=40 r8=20 rax=40 rcx=8192 xmm1=1.5 xmm3=2.5"
			}
		};
	}

	[Theory]
	[MemberData(nameof(X64MemberCalls))]
	public void ExecV2_CallMethod_X64Rcx_PutsThisInRcxAndTheArgumentsAfterIt(string arguments, string expected)
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs(CallStubs + "\nwideTarget = true");
		ToolDispatch dispatch = CreateNativeDispatch(new McpFeatureOptions());

		ExecCallResult result = CallMethod(dispatch, 1, Arguments(arguments));

		Assert.Equal(new ExecCallResult("1000", "5"), result);
		Assert.Equal((0L, 1L, expected),
			(ReadGlobal("methodCalls"), ReadGlobal("remoteCalls"), ReadGlobal("lastCall")));
	}

	public static TheoryData<int, string, string> X64RegisterCalls()
	{
		return new TheoryData<int, string, string>
		{
			{ 3, "integer:10", "rbx=8192 rcx=10" },
			{ 2, "integer:10,float:1.5", "rcx=10 rdx=8192 xmm1=1.5" },
			{ 8, "integer:10,integer:20,double:2.5", "r8=8192 rcx=10 rdx=20 xmm2=2.5" },
			{ 9, "integer:10,integer:20,integer:30", "r8=30 r9=8192 rcx=10 rdx=20" },
			{ 0, "integer:1,integer:2,integer:3,integer:4", "r8=3 r9=4 rax=8192 rcx=1 rdx=2" }
		};
	}

	[Theory]
	[MemberData(nameof(X64RegisterCalls))]
	public void ExecV2_CallMethod_X64OtherRegister_KeepsThisWhenNoArgumentOverwritesIt(int register,
		string arguments, string expected)
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs(CallStubs + "\nwideTarget = true");
		ToolDispatch dispatch = CreateNativeDispatch(new McpFeatureOptions());

		ExecCallResult result = CallMethod(dispatch, register, Arguments(arguments));

		Assert.Equal(new ExecCallResult("1000", "5"), result);
		Assert.Equal((1L, 0L, expected),
			(ReadGlobal("methodCalls"), ReadGlobal("remoteCalls"), ReadGlobal("lastCall")));
	}

	[Theory]
	[InlineData(2, "float:1.5,integer:20", "integer argument 2 into RDX",
		"as argument 2, which Cheat Engine loads into RDX.")]
	[InlineData(8, "integer:10,float:1.5,integer:30", "integer argument 3 into R8",
		"as argument 3, which Cheat Engine loads into R8.")]
	[InlineData(9, "integer:10,integer:20,integer:30,integer:40", "integer argument 4 into R9",
		"as argument 4, which Cheat Engine loads into R9.")]
	[InlineData(0, "float:1.5,float:2.5,float:3.5,float:4.5,float:5.5", "the fifth and later arguments through RAX",
		"pass at most 4 arguments.")]
	public void ExecV2_CallMethod_X64RegisterThatAnArgumentOverwrites_IsRefusedBeforeCalling(int register,
		string arguments, string message, string hint)
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs(CallStubs + "\nwideTarget = true");
		ToolDispatch dispatch = CreateNativeDispatch(new McpFeatureOptions());

		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() => CallMethod(dispatch,
			register, Arguments(arguments)));

		Assert.Equal((ToolErrorKind.InvalidArgument, ToolHostEffect.NotStarted),
			(exception.Error.Kind, exception.Error.HostEffect));
		Assert.StartsWith("classRegister: on a 64-bit target Cheat Engine loads " + message, exception.Error.Message,
			StringComparison.Ordinal);
		Assert.EndsWith(hint, exception.Error.Hint, StringComparison.Ordinal);
		Assert.Equal((0L, 0L), (ReadGlobal("methodCalls"), ReadGlobal("remoteCalls")));
	}

	[Fact]
	public void ExecV2_CallMethod_X86Double_ReachesTheStackAsItsTwoHalvesWithThisInEcx()
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs(CallStubs);
		ToolDispatch dispatch = CreateNativeDispatch(new McpFeatureOptions());

		ExecCallResult result = CallMethod(dispatch, 1, Arguments("double:1.5,integer:7"));

		// 1.5 is 3FF8000000000000: low half 0, high half 3FF80000.
		Assert.Equal(new ExecCallResult("1000", "5"), result);
		Assert.Equal((1L, "[0]=0 [1]=1073217536 [2]=7 ecx=8192"),
			(ReadGlobal("methodCalls"), ReadGlobal("lastCall")));
	}

	[Theory]
	[InlineData(false, "double:1.5,float:2.5", "[0]=0 [1]=1073217536 [2]=2.5")]
	[InlineData(false, "double:-2", "[0]=0 [1]=3221225472")]
	[InlineData(true, "double:1.5,integer:7", "rdx=7 xmm0=1.5")]
	public void ExecV2_CallRemote_PassesADoubleThatReachesTheCalledCodeWhole(bool wide, string arguments,
		string expected)
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs(CallStubs + $"\nwideTarget = {(wide ? "true" : "false")}");
		ToolDispatch dispatch = CreateNativeDispatch(new McpFeatureOptions());
		(int[] types, object?[] values) = Arguments(arguments);

		ExecCallResult result = dispatch.RunLua(CheatEngineToolNames.ExecCallRemote, ExecScripts.CallRemote,
			ExecJsonContext.Default.ExecCallResult, Token, 0x1000UL, 0, 1_000, types, values);

		Assert.Equal(new ExecCallResult("1000", "5"), result);
		Assert.Equal((1L, 0L, expected),
			(ReadGlobal("remoteCalls"), ReadGlobal("methodCalls"), ReadGlobal("lastCall")));
	}

	private static ExecCallResult CallMethod(ToolDispatch dispatch, int register)
	{
		return CallMethod(dispatch, register, ([], []));
	}

	private static ExecCallResult CallMethod(ToolDispatch dispatch, int register,
		(int[] Types, object?[] Values) arguments)
	{
		return dispatch.RunLua(CheatEngineToolNames.ExecCallMethod, ExecScripts.CallMethod,
			ExecJsonContext.Default.ExecCallResult, Token, 0x1000UL, 0, 1_000, 0x2000UL, register, arguments.Types,
			arguments.Values);
	}

	/// <summary>Parses <c>type:value</c> items separated by commas through the tools' own argument checks.</summary>
	private static (int[] Types, object?[] Values) Arguments(string text)
	{
		ExecCallArgument[] arguments = text.Length == 0
			? []
			: text.Split(',').Select(static item =>
			{
				string[] parts = item.Split(':');
				ExecArgumentType type = parts[0] switch
				{
					"integer" => ExecArgumentType.Integral,
					"float" => ExecArgumentType.SinglePrecision,
					_ => ExecArgumentType.DoublePrecision
				};
				return new ExecCallArgument(type, parts[1]);
			}).ToArray();
		return ExecSupport.Arguments(arguments);
	}
}
