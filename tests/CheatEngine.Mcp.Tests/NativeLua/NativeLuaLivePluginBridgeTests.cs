using CheatEngine.Mcp.Tests.LiveQualification;

namespace CheatEngine.Mcp.Tests.NativeLua;

public sealed partial class NativeLuaToolRuntimeTests
{
	[Fact]
	public void LivePluginBridge_Disable_UsesOnlySpaceKeyMessagesAndSettingsOk()
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs(Stubs("7\ndisable\n", "CheatEngine.Mcp.dll:CheatEngine.Mcp"));
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
	[InlineData("8\nother\n", "CheatEngine.Mcp.dll:CheatEngine.Mcp", 1, false)]
	[InlineData("8\ndisable\n", "Other.dll:Other", 1, true)]
	[InlineData("8\ndisable\n", "CheatEngine.Mcp.dll:CheatEngine.Mcp", 0, true)]
	[InlineData("8\ndisable\n", "CheatEngine.Mcp.dll:CheatEngine.Mcp", 2, true)]
	[InlineData("8\nenable\n", "CheatEngine.Mcp.dll:CheatEngine.Mcp", 1, true)]
	public void LivePluginBridge_InvalidRequestsOrRows_RefuseBeforeSettingsApply(string command, string row, int count,
		bool expectsBridgeError)
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs(Stubs(command, row, count));
		RunPluginBridge("pluginEnabled=true; success=pcall(pluginControl)");
		Assert.Equal(expectsBridgeError, (bool) EvaluateLua("success")!);
		Assert.Equal(0L, EvaluateLua("#messages"));
		Assert.Equal(0L, EvaluateLua("okClicks"));
		if (expectsBridgeError)
		{
			string action = command.Split('\n')[1];
			Assert.StartsWith($"error\n8\n{action}\n", (string) EvaluateLua("response")!);
			if (count != 1)
			{
				Assert.Contains($"expected exactly one owned plugin row (count={count})", (string) EvaluateLua("response")!);
			}
		}
		else
		{
			Assert.Equal(string.Empty, EvaluateLua("response"));
		}
	}

	[Fact]
	public void LivePluginBridge_Enable_UsesThePreviouslyDisabledState()
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs(Stubs("9\nenable\n", "CheatEngine.Mcp.dll:CheatEngine.Mcp"));
		RunPluginBridge("pluginEnabled=false; pluginControl()");
		Assert.True((bool) EvaluateLua("pluginEnabled")!);
		Assert.Equal("ok\n9\nenable", EvaluateLua("response"));
		Assert.Equal(1L, EvaluateLua("okClicks"));
	}

	[Theory]
	[InlineData(0, 2, "TformSettings")]
	[InlineData(1, 0, "TUnexpectedSettings")]
	public void LivePluginBridge_ReloadChangesRows_ReportsObservedClassAndBeforeAfterCounts(int beforeReload,
		int afterReload, string settingsClass)
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs(Stubs("8\ndisable\n", "CheatEngine.Mcp.dll:CheatEngine.Mcp", beforeReload, afterReload, settingsClass));
		RunPluginBridge("pluginEnabled=true; pluginControl()");
		string response = (string) EvaluateLua("response")!;
		Assert.StartsWith("error\n8\ndisable\n", response);
		Assert.Contains($"expected exactly one owned plugin row (count={afterReload})", response);
		Assert.Contains($"; settingsClass={settingsClass}; beforeReload={beforeReload}", response);
		Assert.Equal(0L, EvaluateLua("#messages"));
		Assert.Equal(0L, EvaluateLua("okClicks"));
		Assert.True((bool) EvaluateLua("pluginEnabled")!);
	}

	[Fact]
	public void LivePluginBridge_LongMultibyteError_RemainsBoundedAscii()
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs(Stubs("8\ndisable\n", "CheatEngine.Mcp.dll:CheatEngine.Mcp"));
		RunPluginBridge("pluginEnabled=true; getSettingsForm=function() error(string.rep('a',1023)..utf8.char(0xE9)..'tail',0) end; pluginControl()");
		Assert.Equal("error\n8\ndisable\n" + new string('a', 1023) + "?", EvaluateLua("response"));
		Assert.Equal(0L, EvaluateLua("#messages"));
		Assert.Equal(0L, EvaluateLua("okClicks"));
	}

	private static string Stubs(string command, string row, int count = 1, int? countAfterReload = null,
		string settingsClass = "TformSettings") => $$"""
		response=''; messages={}; okClicks=0; settingsFormRequested=false
		io={open=function(_,mode) if mode=='r' then return {read=function() return {{Quote(command)}} end,close=function() end} end return {write=function(_,v) response=response..v end,close=function() end} end}
		os={remove=function() return true end,rename=function() return true end}
		local items={[0]={{Quote(row)}},Count={{count}}}
		reloadSettingsFromRegistry=function()
		  assert(settingsFormRequested, 'settings form must be requested before reload')
		  items.Count={{countAfterReload ?? count}}
		end
		local clb={Items=items,Handle=99}
		local pc,tab,ok={},{},{doClick=function() okClicks=okClicks+1 end}
		local f={ClassName={{Quote(settingsClass)}},findComponentByName=function(n) return ({pcSetting=pc,Plugins=tab,clbPlugins=clb,btnOK=ok})[n] end}
		getSettingsForm=function() settingsFormRequested=true; return f end
		sendMessage=function(...) table.insert(messages,{...}) end
		""";

	private static void RunPluginBridge(string tail) => InstallStubs(LivePluginBridge.Render("request", "response", Quote) + "\n" + tail);

	private static string Quote(string value) => '"' + value.Replace("\\", "\\\\", StringComparison.Ordinal)
		.Replace("\"", "\\\"", StringComparison.Ordinal).Replace("\n", "\\n", StringComparison.Ordinal) + '"';
}
