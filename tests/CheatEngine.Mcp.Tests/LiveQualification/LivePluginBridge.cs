namespace CheatEngine.Mcp.Tests.LiveQualification;

/// <summary>Fixed CE Settings/Plugins control bridge; only enable and disable records are accepted.</summary>
internal static class LivePluginBridge
{
	internal const string Template = """
		local function pluginResponse(text)
		  local response=assert(io.open(__RESPONSE__..'.tmp','w'))
		  response:write(text); response:close()
		  assert(os.rename(__RESPONSE__..'.tmp',__RESPONSE__),'plugin response publish failed')
		end
		local function pluginError(nonce,action,reason)
		  local message=tostring(reason):gsub('[\r\n]+',' '):gsub('[^ -~]','?'):sub(1,1024)
		  pluginResponse('error\n'..nonce..'\n'..action..'\n'..message)
		end
		local function pluginControl()
		  local request=io.open(__REQUEST__,'r')
		  if not request then return end
		  local command=request:read('*a'); request:close(); assert(os.remove(__REQUEST__),'plugin request dequeue failed')
		  local nonce,action=command:match('^(%d+)\n(%a+)\n$')
		  assert(nonce and (action=='enable' or action=='disable'), 'invalid fixed plugin request')
		  local success,reason=pcall(function()
		    assert((action=='enable') ~= pluginEnabled, 'plugin requested state is already active')
		    local f=assert(getSettingsForm(), 'settings form unavailable')
		    local settingsClass=tostring(f.ClassName):sub(1,128)
		    local beforeReload=assert(f.findComponentByName('clbPlugins'), 'plugins checklist unavailable before reload').Items.Count
		    reloadSettingsFromRegistry()
		    local pc=assert(f.findComponentByName('pcSetting'), 'settings page control unavailable')
		    local tab=assert(f.findComponentByName('Plugins'), 'plugins tab unavailable')
		    local clb=assert(f.findComponentByName('clbPlugins'), 'plugins checklist unavailable')
		    local ok=assert(f.findComponentByName('btnOK'), 'settings OK unavailable')
		    pc.ActivePage=tab
		    local rowCount=clb.Items.Count
		    assert(rowCount==1, 'expected exactly one owned plugin row (count='..tostring(rowCount)..')'..
		      '; settingsClass='..settingsClass..'; beforeReload='..tostring(beforeReload))
		    assert(tostring(clb.Items[0])==__BASENAME__..':'..__DISPLAY__, 'owned MCP plugin row not found')
		    clb.ItemIndex=0
		    sendMessage(clb.Handle,0x0100,0x20,0); sendMessage(clb.Handle,0x0101,0x20,0)
		    ok.doClick(); pluginEnabled=(action=='enable')
		  end)
		  if not success then pluginError(nonce,action,reason); return end
		  pluginResponse('ok\n'..nonce..'\n'..action)
		end
		""";

	internal static string Render(string request, string response, Func<string, string> quote) => Template
		.Replace("__REQUEST__", quote(request), StringComparison.Ordinal)
		.Replace("__RESPONSE__", quote(response), StringComparison.Ordinal)
		.Replace("__BASENAME__", quote("CheatEngine.Mcp.dll"), StringComparison.Ordinal)
		.Replace("__DISPLAY__", quote("CheatEngine.Mcp"), StringComparison.Ordinal);
}
