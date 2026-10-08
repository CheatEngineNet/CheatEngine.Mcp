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
		local function pluginControl()
		  local request=io.open(__REQUEST__,'r')
		  if not request then return end
		  local command=request:read('*a'); request:close(); assert(os.remove(__REQUEST__),'plugin request dequeue failed')
		  local nonce,action=command:match('^(%d+)\n(%a+)\n$')
		  assert(nonce and (action=='enable' or action=='disable'), 'invalid fixed plugin request')
		  assert((action=='enable') ~= pluginEnabled, 'plugin requested state is already active')
		  reloadSettingsFromRegistry()
		  local f=assert(getSettingsForm(), 'settings form unavailable')
		  local pc=assert(f.findComponentByName('pcSetting'), 'settings page control unavailable')
		  local tab=assert(f.findComponentByName('Plugins'), 'plugins tab unavailable')
		  local clb=assert(f.findComponentByName('clbPlugins'), 'plugins checklist unavailable')
		  local ok=assert(f.findComponentByName('btnOK'), 'settings OK unavailable')
		  pc.ActivePage=tab
		  assert(clb.Items.Count==1, 'expected exactly one owned plugin row')
		  local row=-1
		  for i=0,clb.Items.Count-1 do
		    local text=tostring(clb.Items[i])
		    if text:find(__BASENAME__,1,true) and text:find(__DISPLAY__,1,true) then
		      assert(row==-1, 'duplicate MCP plugin rows'); row=i
		    end
		  end
		  assert(row>=0, 'owned MCP plugin row not found')
		  clb.ItemIndex=row
		  sendMessage(clb.Handle,0x0100,0x20,0); sendMessage(clb.Handle,0x0101,0x20,0)
		  ok.doClick(); pluginEnabled=(action=='enable')
		  pluginResponse('ok\n'..nonce..'\n'..action)
		end
		""";

	internal static string Render(string request, string response, Func<string, string> quote) => Template
		.Replace("__REQUEST__", quote(request), StringComparison.Ordinal)
		.Replace("__RESPONSE__", quote(response), StringComparison.Ordinal)
		.Replace("__BASENAME__", quote("CheatEngine.Mcp.dll"), StringComparison.Ordinal)
		.Replace("__DISPLAY__", quote("CheatEngine.Mcp"), StringComparison.Ordinal);
}
