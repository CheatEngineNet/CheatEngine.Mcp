using CheatEngine.Mcp.Core.Lua;
using CheatEngine.Mcp.Tests.Support;
using CheatEngine.Mcp.Tools.Mono;

namespace CheatEngine.Mcp.Tests.NativeLua;

/// <summary>Native Lua coverage for fixed, target-code-executing Mono v2 bodies.</summary>
public sealed partial class NativeLuaToolRuntimeTests
{
	[Fact]
	public void MonoV2_InvokeMethod_ResolvesAHexInstanceAddressBeforeCallingTheTarget()
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs("""
		             monopipe = {}
		             monoBase = 4096
		             getAddressSafe = function(value)
		               observedExpression = value
		               if value == '401000' then return 0x401000 end
		               return nil
		             end
		             mono_method_get_parameters = function(method)
		               observedMethod = method
		               return {parameters = {}}
		             end
		             mono_invoke_method = function(_, method, instance, arguments)
		               observedInvokeMethod = method
		               observedInstance = instance
		               observedArgumentType = arguments[1].type
		               observedArgumentValue = arguments[1].value
		               return 17, nil
		             end
		             """);

		LuaJsonResult<MonoMethodInvocation> result = ReadJson(
			LuaToolRuntime.BuildSource(MonoLuaScripts.InvokeMethod, 100,
				new object?[] { "42", "401000", new object?[] { new object?[] { 8L, "7" } } }),
			MonoJsonContext.Default.MonoMethodInvocation);

		Assert.False(result.IsError, result.Error?.Message);
		Assert.Equal(new MonoMethodInvocation(true, "17"), result.Value);
		Assert.Equal("401000", ReadGlobal("observedExpression"));
		Assert.Equal(42L, ReadGlobal("observedMethod"));
		Assert.Equal(42L, ReadGlobal("observedInvokeMethod"));
		Assert.Equal(0x401000L, ReadGlobal("observedInstance"));
		Assert.Equal(8L, ReadGlobal("observedArgumentType"));
		Assert.Equal("7", ReadGlobal("observedArgumentValue"));
		LuaFixedScriptAssert.NeverLoadsCode(MonoLuaScripts.InvokeMethod);
	}

	[Fact]
	public void MonoV2_InvokeMethod_UnresolvableInstanceAddress_RefusesBeforeCallingTheTarget()
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs("""
		             monopipe = {}
		             monoBase = 4096
		             getAddressSafe = function(_) return nil end
		             mono_invoke_method = function()
		               invocationCount = (invocationCount or 0) + 1
		               return 17, nil
		             end
		             """);

		LuaJsonResult<MonoMethodInvocation> result = ReadJson(
			LuaToolRuntime.BuildSource(MonoLuaScripts.InvokeMethod, 100,
				new object?[] { "42", "not-an-address", Array.Empty<object?>() }),
			MonoJsonContext.Default.MonoMethodInvocation);

		Assert.True(result.IsError);
		Assert.Equal(("invalid_argument", "not_started"), (result.Error!.Kind, result.Error.HostEffect));
		Assert.Null(ReadGlobal("invocationCount"));
	}
}
