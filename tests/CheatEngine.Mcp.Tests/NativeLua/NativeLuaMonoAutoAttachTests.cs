using CheatEngine.Mcp.Core.Features;
using CheatEngine.Mcp.Tests.Support;

namespace CheatEngine.Mcp.Tests.NativeLua;

public sealed partial class NativeLuaToolRuntimeTests
{
	[Theory]
	[InlineData(
		"getTableOption = function(n) assert(n == 'UsesMono') return true end; getSettingsOption = function(n) assert(n == 'IgnoreUsesMono') return false end; process = 'game.exe'",
		true, false, true, true)]
	[InlineData(
		"getTableOption = function() return false end; getSettingsOption = function() return false end",
		false, false, false, true)]
	[InlineData(
		"getTableOption = function() return nil end; getSettingsOption = function() return true end; process = 'x'",
		false, true, true, true)]
	// Lua truth: an option read as '0' is set; the process-open hook attaches only when the setting reads exactly false.
	[InlineData("getTableOption = function() return '0' end; getSettingsOption = function() return nil end",
		true, true, false, true)]
	[InlineData("process = false", true, false, false, false)]
	[InlineData(
		"getTableOption = function() error('no option') end; getSettingsOption = function() error('no setting') end",
		true, false, false, false)]
	public void MonoAutoAttachProbe_StubbedCheatEngine_ReportsWhatTheMonoHooksWouldDo(string stubs, bool usesMono,
		bool ignore, bool processOpen, bool determined)
	{
		LuaFixedScriptAssert.NeverLoadsCode(MonoAutoAttachProbe.Script);
		using RuntimeScope scope = CreateScope();
		InstallStubs(stubs);

		LuaJsonResult<MonoAutoAttachState> result = ReadJson(
			LuaToolRuntime.BuildSource(MonoAutoAttachProbe.Script, 100, []),
			FeaturesJsonContext.Default.MonoAutoAttachState);

		Assert.False(result.IsError, result.Error?.Message);
		Assert.Equal(new MonoAutoAttachState(usesMono, ignore, processOpen, determined), result.Value);
	}
}
