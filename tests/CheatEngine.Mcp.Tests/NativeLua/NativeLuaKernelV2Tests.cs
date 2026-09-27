using CheatEngine.Mcp.Core.Contract;
using CheatEngine.Mcp.Core.Features;
using CheatEngine.Mcp.Tests.Support;
using CheatEngine.Mcp.Tools.Kernel;

namespace CheatEngine.Mcp.Tests.NativeLua;

/// <summary>Native Lua coverage for DBVM preconditions in the v2 kernel bodies.</summary>
public sealed partial class NativeLuaToolRuntimeTests
{
	[Fact]
	public void KernelV2_PhysicalOperations_RefuseAnUninitializedDbvmBeforeTheDeviceCall()
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs("""
		             readCalls = 0
		             writeCalls = 0
		             watchCalls = 0
		             dbvm_initialized = function() return false end
		             dbvm_readPhysicalMemory = function() readCalls = readCalls + 1; return {0} end
		             dbvm_writePhysicalMemory = function() writeCalls = writeCalls + 1; return true end
		             dbvm_watch_reads = function() watchCalls = watchCalls + 1; return 1 end
		             dbvm_watch_writes = function() watchCalls = watchCalls + 1; return 1 end
		             dbvm_watch_executes = function() watchCalls = watchCalls + 1; return 1 end
		             dbvm_watch_disable = function() return true end
		             """);
		ToolDispatch dispatch = CreateNativeDispatch(new McpFeatureOptions());

		CheatEngineToolException read = Assert.Throws<CheatEngineToolException>(() => dispatch.RunLua(
			"kernel_read_physical", KernelScripts.ReadPhysical, KernelJsonContext.Default.KernelPhysicalRead, Token,
			0x1000UL, 1));
		CheatEngineToolException write = Assert.Throws<CheatEngineToolException>(() => dispatch.RunLua(
			"kernel_write_physical", KernelScripts.WritePhysical, KernelJsonContext.Default.KernelPhysicalWrite, Token,
			0x1000UL, new byte[] { 0xAA }));
		CheatEngineToolException watch = Assert.Throws<CheatEngineToolException>(() => dispatch.RunLua(
			"kernel_start_watch", KernelScripts.StartWatch, KernelJsonContext.Default.LuaKernelWatchArmed, Token,
			"bbbb22", "kernelwatch-bbbb22-1", 2, 3_000, 0, 0x1000UL, 1, 0, 1));

		Assert.All([read, write, watch], static exception =>
			Assert.Equal((ToolErrorKind.InvalidState, ToolHostEffect.NotStarted),
				(exception.Error.Kind, exception.Error.HostEffect)));
		Assert.Equal((0L, 0L, 0L), (ReadGlobal("readCalls"), ReadGlobal("writeCalls"), ReadGlobal("watchCalls")));
	}
}
