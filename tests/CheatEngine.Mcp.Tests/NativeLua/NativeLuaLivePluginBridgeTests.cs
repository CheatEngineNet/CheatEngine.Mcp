using CheatEngine.Mcp.Tests.LiveQualification;

namespace CheatEngine.Mcp.Tests.NativeLua;

public sealed partial class NativeLuaToolRuntimeTests
{
	[Fact]
	public void LivePluginBridge_ObserveProtocolCarriesOneExactBreakpointAddress()
	{
		string source = LivePluginBridge.Render("request", "response", Quote);
		Assert.Contains("debug_getBreakpointList()", source, StringComparison.Ordinal);
		Assert.Contains("(0x[0-9A-F]+)", source, StringComparison.Ordinal);
		Assert.Contains("missing exact breakpoint address", source, StringComparison.Ordinal);
	}

	[Fact]
	public void LivePluginBridge_Disable_UsesOnlySpaceKeyMessagesAndSettingsOk()
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs(Stubs("7\ndisable\n", "CheatEngine.Mcp.dll:CheatEngine.Mcp"));
		RunPluginBridge("pluginEnabled=true; pluginControl()");
		Assert.Equal("ok\n7\ndisable\nchecked\nunchecked\nunchecked", EvaluateLua("response"));
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
		Assert.Equal("ok\n9\nenable\nunchecked\nchecked\nchecked", EvaluateLua("response"));
		Assert.Equal(1L, EvaluateLua("okClicks"));
	}

	[Fact]
	public void LivePluginBridge_CheckboxDoesNotToggle_RefusesAfterApplyingSettingsWithSanitizedStates()
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs(Stubs("9\nenable\n", "CheatEngine.Mcp.dll:CheatEngine.Mcp", toggleOnSpace: false));
		RunPluginBridge("pluginEnabled=false; pluginControl()");
		string response = (string) EvaluateLua("response")!;
		Assert.StartsWith("error\n9\nenable\n", response, StringComparison.Ordinal);
		Assert.Contains("plugin checkbox transition mismatch; afterReload=unchecked; afterToggle=unchecked; afterApply=unchecked; expected=checked", response,
			StringComparison.Ordinal);
		Assert.False((bool) EvaluateLua("pluginEnabled")!);
		Assert.Equal(1L, EvaluateLua("okClicks"));
	}

	[Fact]
	public void LivePluginBridge_SettingsApplyRevertsCheckbox_RefusesAfterCapturingAllTransitionStates()
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs(Stubs("7\ndisable\n", "CheatEngine.Mcp.dll:CheatEngine.Mcp", checkedAfterApply: true));
		RunPluginBridge("pluginEnabled=true; pluginControl()");
		string response = (string) EvaluateLua("response")!;
		Assert.StartsWith("error\n7\ndisable\n", response, StringComparison.Ordinal);
		Assert.Contains("plugin checkbox transition mismatch; afterReload=checked; afterToggle=unchecked; afterApply=checked; expected=unchecked", response,
			StringComparison.Ordinal);
		Assert.True((bool) EvaluateLua("pluginEnabled")!);
		Assert.Equal(1L, EvaluateLua("okClicks"));
	}

	[Fact]
	public void LivePluginBridge_NonBooleanCheckboxState_RefusesBeforeSendingTheToggle()
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs(Stubs("7\ndisable\n", "CheatEngine.Mcp.dll:CheatEngine.Mcp"));
		RunPluginBridge("pluginEnabled=true; pluginCheckbox.Checked[0]='invalid'; pluginControl()");
		string response = (string) EvaluateLua("response")!;
		Assert.StartsWith("error\n7\ndisable\n", response, StringComparison.Ordinal);
		Assert.Contains("plugin checkbox state is not boolean", response, StringComparison.Ordinal);
		Assert.Equal(0L, EvaluateLua("#messages"));
		Assert.Equal(0L, EvaluateLua("okClicks"));
	}

	[Fact]
	public void LivePluginBridge_ObserveReportsDisabledPluginWithoutStatusHook()
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs(Stubs("10\nobserve\n0x7FF600001012\n", "CheatEngine.Mcp.dll:CheatEngine.Mcp", pluginChecked: false,
			statusHookPresent: false));
		RunPluginBridge("pluginEnabled=false; pluginControl()");
		string[] fields = ((string) EvaluateLua("response")!).Split('\n');
		Assert.Equal(23, fields.Length);
		Assert.Equal("ok", fields[0]);
		Assert.Equal("uninstalled", fields[10]);
		Assert.Equal("", fields[21]);
		Assert.Equal("raw:0:", fields[22]);
		Assert.Equal(0L, EvaluateLua("#messages"));
		Assert.Equal(0L, EvaluateLua("okClicks"));
	}

	[Fact]
	public void LivePluginBridge_RemovalProbe_RecordsIdAndReturnErrorThenRestoresOriginal()
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs(Stubs("11\ninstall_probe\n", "CheatEngine.Mcp.dll:CheatEngine.Mcp") + "\n" + $$"""
			debug_removeBreakpointByID=function(id) return nil,'CE refused '..tostring(type(id)=='table' and id.ID or id) end
			debug_getBreakpointList=function() return {[1]=0x7FF600001012} end
			""");
		RunPluginBridge("pluginEnabled=false; originalRemover=debug_removeBreakpointByID; pluginControl(); first,second=debug_removeBreakpointByID({ID=88,id=88}); probeState=rawget(_G,'__cheatengine_mcp_lifecycle_removal_probe'); probeReceipt=probeState.receipt; response=''; pluginCommand='12\\nremove_probe\\n'; pluginControl()");
		Assert.Equal("ok\n12\nremove_probe", EvaluateLua("response"));
		Assert.StartsWith("table:", (string) EvaluateLua("probeReceipt.id")!, StringComparison.Ordinal);
		Assert.Equal("table", EvaluateLua("probeReceipt.idType"));
		Assert.True((bool) EvaluateLua("probeReceipt.succeeded")!);
		Assert.Equal("CE refused 88", EvaluateLua("probeReceipt.failure"));
		Assert.Contains("string:ID=number:88", (string) EvaluateLua("probeReceipt.idFields")!, StringComparison.Ordinal);
		Assert.Equal("nil", EvaluateLua("probeReceipt.returnValue"));
		Assert.Equal(1L, EvaluateLua("probeReceipt.beforeCount"));
		Assert.Equal(1L, EvaluateLua("probeReceipt.afterCount"));
		Assert.True((bool) EvaluateLua("debug_removeBreakpointByID==originalRemover")!);
		Assert.True((bool) EvaluateLua("rawget(_G,'__cheatengine_mcp_lifecycle_removal_probe')==nil")!);
		Assert.Null(EvaluateLua("first"));
		Assert.Equal("CE refused 88", EvaluateLua("second"));
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
		string settingsClass = "TformSettings", bool? pluginChecked = null, bool statusHookPresent = false,
		bool toggleOnSpace = true, bool? checkedAfterApply = null) => $$"""
		response=''; messages={}; okClicks=0; settingsFormRequested=false; pluginCommand={{Quote(command)}}
		io={open=function(_,mode) if mode=='r' then return {read=function() return pluginCommand end,close=function() end} end return {write=function(_,v) response=response..v end,close=function() end} end}
		os={remove=function() return true end,rename=function() return true end}
		local items={[0]={{Quote(row)}},Count={{count}}}
		reloadSettingsFromRegistry=function()
		  assert(settingsFormRequested, 'settings form must be requested before reload')
		  items.Count={{countAfterReload ?? count}}
		end
		local clb={Items=items,Handle=99}
		clb.Checked={}
		clb.Checked[0]={{(pluginChecked ?? command.Contains("\ndisable\n", StringComparison.Ordinal)).ToString().ToLowerInvariant()}}
		pluginCheckbox=clb
		local pc,tab={},{}
		local ok={doClick=function()
		  okClicks=okClicks+1
		  {{(checkedAfterApply.HasValue ? "clb.Checked[0]=" + checkedAfterApply.Value.ToString().ToLowerInvariant() : string.Empty)}}
		end}
		local f={ClassName={{Quote(settingsClass)}},findComponentByName=function(n) return ({pcSetting=pc,Plugins=tab,clbPlugins=clb,btnOK=ok})[n] end}
		getSettingsForm=function() settingsFormRequested=true; return f end
		getOpenedProcessID=function() return 77 end
		debug_getBreakpointList=function() return {} end
		getMainForm=function()
		  local menu={}
		  menu.findComponentByName=function(_) return {{(statusHookPresent ? "true" : "nil")}} end
		  return {Menu=menu}
		end
		sendMessage=function(...)
		  local message=select(2,...)
		  table.insert(messages,{...})
		  if message==0x0101 and {{toggleOnSpace.ToString().ToLowerInvariant()}} then clb.Checked[0]=not clb.Checked[0] end
		end
		""";

	private static void RunPluginBridge(string tail) => InstallStubs(LivePluginBridge.Render("request", "response", Quote) + "\n" + tail);

	private static string Quote(string value) => '"' + value.Replace("\\", "\\\\", StringComparison.Ordinal)
		.Replace("\"", "\\\"", StringComparison.Ordinal).Replace("\n", "\\n", StringComparison.Ordinal) + '"';
}
