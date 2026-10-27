using System.Diagnostics;
using System.Globalization;
using System.Reflection.PortableExecutable;
using System.Runtime.Versioning;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

using CheatEngine.Mcp.Tests.LiveQualification.Infrastructure;

namespace CheatEngine.Mcp.Tests.LiveQualification;

internal sealed partial class LiveSandboxSession
{
	private string BuildDriver(string stopPath, string targetExitPath, string pluginPath, string name)
	{
		int maximumLifetimeMilliseconds = _options.UseSoakLifetime ? 8_100_000 : 300_000;
		string pluginBridge = LivePluginBridge.Render(Path.Combine(_layout.RunDirectory, $"plugin-request-{name}.txt"),
			Path.Combine(_layout.RunDirectory, $"plugin-response-{name}.txt"), LuaString);
		string compilerProbe = _options.CompilerQualification
			? LiveCompilerBridge.Render(Path.Combine(_layout.RunDirectory, $"compiler-request-{name}.txt"),
				Path.Combine(_layout.RunDirectory, $"compiler-response-{name}.txt"), CompilerHoldReadyPath(name),
				CompilerHoldReleasePath(name), LiveCompilerPayload.ValidSource, LuaString)
			: "local function compilerProbe() end";
		string transitionProbe = _options.DebuggerTransitionDiagnostic
			? LiveDebuggerTransitionBridge.Render(Path.Combine(_layout.RunDirectory, $"debugger-transition-request-{name}.txt"),
				Path.Combine(_layout.RunDirectory, $"debugger-transition-response-{name}.txt"), LuaString)
			: string.Empty;
		string sourceProbe = _options.SymbolSourcesQualification
			? LiveSymbolSourceNetworkProbe.RenderDriverLua(
				Path.Combine(HostInstallationDirectory(name), LiveSymbolSourceNetworkProbe.ModuleFileName),
				Path.Combine(_layout.RunDirectory, $"symbol-source-probe-request-{name}.txt"),
				Path.Combine(_layout.RunDirectory, $"symbol-source-probe-response-{name}.txt"), LuaString)
			: string.Empty;
		string loadedTimerBody = LiveSymbolSourceNetworkProbe.LoadedTimerBody(_options.SymbolSourcesQualification);
		return $$"""
		         local output = {{LuaString(Path.Combine(_layout.RunDirectory, $"driver-{name}.log"))}}
		         local function log(value)
		           local f = assert(io.open(output, 'a')); f:write(tostring(value)..'\n'); f:close()
		         end
		         log('driver-loaded')
		         local started = getTickCount()
		         local loaded = false
		         local pluginEnabled = false
		         {{pluginBridge}}
		         local function scanProbe()
		           local requestPath={{LuaString(Path.Combine(_layout.RunDirectory, $"scan-request-{name}.txt"))}}
		           local request=io.open(requestPath,'r')
		           if not request then return end
		           local action=request:read('*a'); request:close(); os.remove(requestPath)
		           local ok,answer=pcall(function()
		             local f=getMainForm()
		             if action=='prepare' then
		               f.show()
		               local manifest=assert(io.open({{LuaString(Path.Combine(_layout.RunDirectory, $"target-{name}.json"))}},'r'))
		               local contents=manifest:read('*a'); manifest:close()
		               local address=assert(contents:match('"address"%s*:%s*"([^"]+)"')):gsub('^0x','')
		               f.FromAddress.Text=address
		               f.ToAddress.Text=string.format('%X',tonumber(address,16)+63)
		               f.cbFastScan.Checked=false
		             elseif action=='manual' then
		               assert(f.btnNewScan.Enabled, 'scan is busy')
		               if getCurrentMemscan().LastScanType~='stNewScan' then f.btnNewScan.doClick() end
		               f.VarType.ItemIndex=3; f.VarType.OnChange(f.VarType)
		               f.ScanType.ItemIndex=0; f.ScanType.OnChange(f.ScanType)
		               f.cbHexadecimal.Checked=false; f.Scanvalue.Text='20260927'
		               f.btnNewScan.doClick()
		             elseif action=='hide' then f.hide()
		             else assert(action=='snapshot', 'unknown scan probe') end
		             local ms=getCurrentMemscan()
		             return 'ok\n'..tostring(f.Foundlist3.Items.Count)..'\n'..f.FoundCountLabel.Caption..'\n'..
		               f.Scanvalue.Text..'\n'..tostring(f.VarType.ItemIndex)..'\n'..tostring(ms.FoundList.Count)..'\n'..
		               f.FromAddress.Text..'|'..f.ToAddress.Text..'|hex='..tostring(f.cbHexadecimal.Checked)..'|type='..tostring(ms.VarType)..'|value='..tostring(ms.LastScanValue)..'|error='..tostring(ms.ErrorString)..'\n'..tostring(f.Visible)
		           end)
		           local responsePath={{LuaString(Path.Combine(_layout.RunDirectory, $"scan-response-{name}.txt"))}}
		           local response=assert(io.open(responsePath..'.tmp','w'))
		           response:write(ok and answer or ('error: '..tostring(answer))); response:close()
		           os.rename(responsePath..'.tmp',responsePath)
		         end
		         {{compilerProbe}}
		         {{transitionProbe}}{{(string.IsNullOrEmpty(sourceProbe) ? string.Empty : "\n" + sourceProbe)}}
		         local timer = createTimer(nil, false)
		         timer.Interval = 100
		         {{LiveExecutionDriverLua.RenderPreamble()}}
		         timer.OnTimer = function()
		          local tickOk, tickError = pcall(function()
		           {{LiveExecutionDriverLua.RenderTimerBody(stopPath, targetExitPath,
				Path.Combine(_layout.RunDirectory, $"expiry-{name}.signal"), maximumLifetimeMilliseconds,
				_options.UsesTargetFirstExecutionShutdown)}}
		           if loaded then {{loadedTimerBody}} end
		           if not getMainForm() then return end
		           loaded = true
		           hideAllCEWindows()
		           log('loading-plugin')
		           local ok, value = pcall(loadPlugin, {{LuaString(pluginPath)}})
		           log('loadPlugin ok='..tostring(ok)..' result='..tostring(value))
		           assert(ok and value, 'owned plugin did not load')
		           pluginEnabled=true
		           local menu = getMainForm().Menu
		           local status = assert(menu.findComponentByName('CheatEngineMcpStatus'), 'MCP status menu is missing')
		           local count = 0
		           for i=0,menu.Items.Count-1 do
		             if menu.Items[i].Name == 'CheatEngineMcpStatus' then count=count+1 end
		           end
		           log('mcp-status '..status.Caption..' count='..count)
		          end)
		          if not tickOk then log('timer-error '..tostring(tickError)) end
		         end
		         timer.Enabled = true
		         """;
	}

	private static string LuaString(string value)
	{
		return '"' + string.Concat(Encoding.UTF8.GetBytes(value).Select(valueByte =>
			"\\" + valueByte.ToString("D3", CultureInfo.InvariantCulture))) + '"';
	}
}
