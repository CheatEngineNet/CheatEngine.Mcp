using CheatEngine.Mcp.Core.Execution;
using CheatEngine.Mcp.Core.Features;
using CheatEngine.Mcp.Core.Jobs;
using CheatEngine.Mcp.Tools.Code;

using Microsoft.Extensions.Options;

namespace CheatEngine.Mcp.Tests.NativeLua;

/// <summary>Executable CE-API-double coverage for the fixed code-view Lua bodies.</summary>
public sealed partial class NativeLuaToolRuntimeTests
{
	[Fact]
	public void CodeGetFunction_StubbedApi_ReturnsBoundariesAndJumpDestination()
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs("""
		             getAddressSafe = function(value) receivedAddress = value; return 0x401020 end
		             getFunctionRange = function(address) assert(address == 0x401020); return 0x401000, 0x401080 end
		             getInstructionSize = function() error('Raw host boundaries must not be normalized') end
		             isJumpDestination = function(address, range) receivedRange = range; return address == 0x401020 end
		             """);
		ToolDispatch dispatch = CreateNativeDispatch(new McpFeatureOptions());

		CodeLuaFunction result = dispatch.RunLua("code_get_function", CodeScripts.GetFunction,
			CodeLuaJsonContext.Default.CodeLuaFunction, CancellationToken.None, "game.exe+20", 512);

		Assert.Equal(new CodeLuaFunction(true, "401020", "401000", "401080", 128, true), result);
		Assert.Equal(("game.exe+20", 512L), (ReadGlobal("receivedAddress"), ReadGlobal("receivedRange")));
	}

	[Fact]
	public void CodeComments_StubbedApi_KeepsReadOrderAndWritesTheRequestedComment()
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs("""
		             getAddressSafe = function(value) return ({ first=0x401000, second=0x402000, target=0x403000 })[value] end
		             getComment = function(address) return address == 0x401000 and 'entry' or nil end
		             setComment = function(address, comment) writtenAddress=address; writtenComment=comment end
		             """);
		ToolDispatch dispatch = CreateNativeDispatch(new McpFeatureOptions());

		using JobRegistry jobs = new(dispatch, new TargetResources(), Options.Create(new McpExecutionOptions()),
			TimeProvider.System);
		CodeTools tools = new(dispatch, jobs);
		CodeComments read = tools.GetComments(["first", "second"], CancellationToken.None);
		CodeComment written = tools.SetComment("target", "updated", CancellationToken.None);

		Assert.Equal([new CodeComment("401000", "entry"), new CodeComment("402000", null)], read.Comments);
		Assert.Equal(new CodeComment("403000", "updated"), written);
		Assert.Equal((0x403000L, "updated"), (ReadGlobal("writtenAddress"), ReadGlobal("writtenComment")));
	}
}
