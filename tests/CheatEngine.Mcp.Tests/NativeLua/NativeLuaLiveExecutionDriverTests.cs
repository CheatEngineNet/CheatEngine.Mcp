using CheatEngine.Mcp.Tests.LiveQualification;

namespace CheatEngine.Mcp.Tests.NativeLua;

public sealed partial class NativeLuaToolRuntimeTests
{
	private const string ExecutionDriverStubs = """
		clock = 0; started = 0; writes = 0; closes = 0; clears = 0
		stopExists = false; targetExited = false
		timer = { Enabled = true }
		getTickCount = function() return clock end
		getAddressList = function() return { clear = function() clears = clears + 1 end } end
		closeCE = function() closes = closes + 1 end
		log = function() end
		io = { open = function(path, mode)
		  if mode == 'w' then
		    assert(path == 'C:/run/expiry')
		    return { write = function(_, value) assert(value == 'expired'); writes = writes + 1 end, close = function() end }
		  end
		  if path == 'C:/run/stop' and stopExists then return { close = function() end } end
		  if path == 'C:/run/target.exited' and targetExited then return { close = function() end } end
		end }
		""";

	[Fact]
	public void ExecutionDriver_ExpiryKeepsPollingAndLaterConfirmedStopClosesOnce()
	{
		using RuntimeScope scope = CreateScope();
		InstallExecutionDriver(true);
		InstallStubs("clock = 300001; tick(); tick()");
		Assert.Equal((1L, 0L, 0L, true), (ReadGlobal("writes"), ReadGlobal("closes"), ReadGlobal("clears"), EvaluateLua("timer.Enabled")));

		InstallStubs("stopExists = true; targetExited = true; tick()");
		Assert.Equal((1L, 1L, 1L, false), (ReadGlobal("writes"), ReadGlobal("closes"), ReadGlobal("clears"), EvaluateLua("timer.Enabled")));
	}

	[Fact]
	public void ExecutionDriver_StopBeforeTargetExitRefusesWithoutDisablingWatcher()
	{
		using RuntimeScope scope = CreateScope();
		InstallExecutionDriver(true);
		InstallStubs("clock = 300001; stopExists = true; ok, message = pcall(tick)");
		Assert.Equal(false, ReadGlobal("ok"));
		Assert.Contains("target exit was not confirmed", (string) ReadGlobal("message")!, StringComparison.Ordinal);
		Assert.Equal((0L, 0L, true), (ReadGlobal("closes"), ReadGlobal("clears"), EvaluateLua("timer.Enabled")));

		InstallStubs("targetExited = true; tick()");
		Assert.Equal((1L, 1L), (ReadGlobal("closes"), ReadGlobal("clears")));
	}

	[Fact]
	public void ExecutionDriver_OrdinaryScenarioRetainsLifetimeShutdown()
	{
		using RuntimeScope scope = CreateScope();
		InstallExecutionDriver(false);
		InstallStubs("clock = 300001; tick()");
		Assert.Equal((0L, 1L, 1L, false), (ReadGlobal("writes"), ReadGlobal("closes"), ReadGlobal("clears"), EvaluateLua("timer.Enabled")));
	}

	private static void InstallExecutionDriver(bool targetFirst)
	{
		InstallStubs(ExecutionDriverStubs + "\n" + LiveExecutionDriverLua.RenderPreamble() + "\nfunction tick()\n" +
			LiveExecutionDriverLua.RenderTimerBody("C:/run/stop", "C:/run/target.exited", "C:/run/expiry", 300000, targetFirst) + "\nend");
	}
}
