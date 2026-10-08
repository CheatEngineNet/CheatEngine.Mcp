using CheatEngine.Mcp.Tests.LiveQualification;

namespace CheatEngine.Mcp.Tests.NativeLua;

public sealed partial class NativeLuaToolRuntimeTests
{
	[Fact]
	public void LivePluginBridge_Disable_UsesOnlySpaceKeyMessagesAndSettingsOk()
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs(Stubs("7\ndisable\n", "CheatEngine.Mcp.dll CheatEngine.Mcp"));
		RunPluginBridge("pluginEnabled=true; pluginControl()");
		Assert.Equal("ok\n7\ndisable", EvaluateLua("response"));
		Assert.Equal(2L, EvaluateLua("#messages"));
		Assert.Equal(0x0100L, EvaluateLua("messages[1][2]"));
		Assert.Equal(0x0101L, EvaluateLua("messages[2][2]"));
		Assert.Equal(99L, EvaluateLua("messages[1][1]"));
		Assert.Equal(0x20L, EvaluateLua("messages[1][3]"));
		Assert.False((bool) EvaluateLua("pluginEnabled")!);
		Assert.Equal(1L, EvaluateLua("okClicks"));
	}

	[Theory]
	[InlineData("8\nother\n", "CheatEngine.Mcp.dll CheatEngine.Mcp", 1)]
	[InlineData("8\ndisable\n", "Other.dll Other", 1)]
	[InlineData("8\ndisable\n", "CheatEngine.Mcp.dll CheatEngine.Mcp", 2)]
	[InlineData("8\nenable\n", "CheatEngine.Mcp.dll CheatEngine.Mcp", 1)]
	public void LivePluginBridge_InvalidRequestsOrRows_RefuseBeforeSettingsApply(string command, string row, int count)
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs(Stubs(command, row, count));
		RunPluginBridge("pluginEnabled=true; success=pcall(pluginControl)");
		Assert.False((bool) EvaluateLua("success")!);
		Assert.Equal(0L, EvaluateLua("#messages"));
		Assert.Equal(0L, EvaluateLua("okClicks"));
		Assert.Equal(string.Empty, EvaluateLua("response"));
	}

	[Fact]
	public void LivePluginBridge_Enable_UsesThePreviouslyDisabledState()
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs(Stubs("9\nenable\n", "CheatEngine.Mcp.dll CheatEngine.Mcp"));
		RunPluginBridge("pluginEnabled=false; pluginControl()");
		Assert.True((bool) EvaluateLua("pluginEnabled")!);
		Assert.Equal("ok\n9\nenable", EvaluateLua("response"));
		Assert.Equal(1L, EvaluateLua("okClicks"));
	}

	private static string Stubs(string command, string row, int count = 1) => $$"""
		response=''; messages={}; okClicks=0
		io={open=function(_,mode) if mode=='r' then return {read=function() return {{Quote(command)}} end,close=function() end} end return {write=function(_,v) response=response..v end,close=function() end} end}
		os={remove=function() return true end,rename=function() return true end}
		reloadSettingsFromRegistry=function() end
		local items={[0]={{Quote(row)}},Count={{count}}}
		local clb={Items=items,Handle=99}
		local pc,tab,ok={},{},{doClick=function() okClicks=okClicks+1 end}
		local f={findComponentByName=function(n) return ({pcSetting=pc,Plugins=tab,clbPlugins=clb,btnOK=ok})[n] end}
		getSettingsForm=function() return f end
		sendMessage=function(...) table.insert(messages,{...}) end
		""";

	private static void RunPluginBridge(string tail) => InstallStubs(LivePluginBridge.Render("request", "response", Quote) + "\n" + tail);

	private static string Quote(string value) => '"' + value.Replace("\\", "\\\\", StringComparison.Ordinal)
		.Replace("\"", "\\\"", StringComparison.Ordinal).Replace("\n", "\\n", StringComparison.Ordinal) + '"';
}
