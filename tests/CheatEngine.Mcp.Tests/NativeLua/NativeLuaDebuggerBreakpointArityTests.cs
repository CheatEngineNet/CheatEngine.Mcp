using CheatEngine.Mcp.Tools.Debugger;

namespace CheatEngine.Mcp.Tests.NativeLua;

public sealed partial class NativeLuaToolRuntimeTests
{
	[Theory]
	[InlineData(false, DebuggerBreakpointMethod.Default, false)]
	[InlineData(false, DebuggerBreakpointMethod.Default, true)]
	[InlineData(false, DebuggerBreakpointMethod.Int3, false)]
	[InlineData(false, DebuggerBreakpointMethod.Int3, true)]
	[InlineData(true, DebuggerBreakpointMethod.Default, false)]
	[InlineData(true, DebuggerBreakpointMethod.Default, true)]
	[InlineData(true, DebuggerBreakpointMethod.Int3, false)]
	[InlineData(true, DebuggerBreakpointMethod.Int3, true)]
	public void DebuggerAdapter_SetBreakpoint_OmitsAbsentOptionalSlots(bool threadSpecific,
		DebuggerBreakpointMethod method, bool oneShot)
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs(JobHostStubs + "\n" + """
		             bptExecute, bptAccess, bptWrite, bpmDebugRegister, bpmInt3, bpmException = 1, 2, 3, 4, 5, 6
		             debug_isDebugging = function() return true end
		             getOpenedProcessID = function() return 77 end
		             getAddressSafe = function(_) return 0x401000 end
		             breakpointAddresses = {}
		             debug_getBreakpointList = function() return breakpointAddresses end
		             local function observe(threadSpecific, ...)
		                local arguments = table.pack(...)
		                observedArgumentCount = arguments.n
		                local first = threadSpecific and 2 or 1
		                observedThread = threadSpecific and arguments[1] or 0
		                table.insert(breakpointAddresses, arguments[first])
		                assert(arguments[first] == 0x401000 and arguments[first + 1] == 1
		                   and arguments[first + 2] == bptExecute, 'Wrong breakpoint address, size or trigger')
		                observedMethod = type(arguments[first + 3]) == 'number' and arguments[first + 3] or nil
		                observedCallback = type(arguments[arguments.n]) == 'function' and arguments[arguments.n] or nil
		                return true, {ID = 88, PID = 77, DTID = 9}
		             end
		             debug_setBreakpoint = function(...) return observe(false, ...) end
		             debug_setBreakpointForThread = function(...) return observe(true, ...) end
		             debug_removeBreakpointByID = function(id)
		                removedBreakpoint = type(id) == 'table' and id.ID or id
		                table.remove(breakpointAddresses)
		                return true
		             end
		             targetIs64Bit = function() return true end
		             """);
		using DebuggerToolHarness harness = CreateDebuggerTools();
		DebuggerBreakpointSet result = harness.Tools.SetBreakpoint("401000", DebuggerBreakpointTrigger.Execute, 1,
			method, threadSpecific ? 19 : null, oneShot, Token);

		int expectedCount = 3 + (threadSpecific ? 1 : 0) + (method == DebuggerBreakpointMethod.Default ? 0 : 1) + (oneShot ? 1 : 0);
		Assert.Equal((long) expectedCount, ReadGlobal("observedArgumentCount"));
		Assert.Equal(threadSpecific ? 19L : 0L, ReadGlobal("observedThread"));
		Assert.Equal(method == DebuggerBreakpointMethod.Default ? null : (object) 5L, ReadGlobal("observedMethod"));
		Assert.Equal((method, oneShot, threadSpecific ? (int?) 19 : null), (result.Method, result.OneShot, result.ThreadId));
		InstallStubs(oneShot ? "assert(type(observedCallback) == 'function'); observedCallback()" : "assert(observedCallback == nil)");
		if (!oneShot)
		{
			Assert.True(harness.Tools.DeleteBreakpoint("401000", Token).Released);
		}
		Assert.Equal(88L, ReadGlobal("removedBreakpoint"));
	}
}
