namespace CheatEngine.Mcp.Tests.LiveQualification;

/// <summary>Fixed lifecycle bridge with bounded breakpoint-cleanup probing.</summary>
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
		local function pluginSafe(value)
		  return tostring(value):gsub('[^%w:%.%-%_ ]','?'):sub(1,32)
		end
		local function pluginBreakpointSnapshot()
		  local success,list=pcall(debug_getBreakpointList)
		  if not success or type(list)~='table' then return -1, success and 'not-table' or pluginSafe(list) end
		  local count=0
		  local samples={}
		  for key,value in pairs(list) do
		    count=count+1
		    if #samples<8 then samples[#samples+1]=type(key)..':'..pluginSafe(key)..'='..type(value)..':'..pluginSafe(value) end
		  end
		  table.sort(samples)
		  return count,table.concat(samples,';')
		end
		local function pluginBreakpointIdFields(value)
		  if type(value)~='table' then return '' end
		  local function fields(tableValue)
		    local samples={}
		    local key=nil
		    for _=1,8 do
		      local item
		      key,item=next(tableValue,key)
		      if key==nil then break end
		      samples[#samples+1]=type(key)..':'..pluginSafe(key)..'='..type(item)..':'..pluginSafe(item)
		    end
		    table.sort(samples)
		    return table.concat(samples,';')
		  end
		  local metatable=getmetatable(value)
		  return 'fields='..fields(value)..';metatableType='..type(metatable)..
		    ';metatableFields='..(type(metatable)=='table' and fields(metatable) or '')
		end
		local function pluginInstallRemovalProbe()
		  local key='__cheatengine_mcp_lifecycle_removal_probe'
		  assert(rawget(_G,key)==nil, 'breakpoint removal probe is already installed')
		  assert(type(debug_removeBreakpointByID)=='function', 'breakpoint remover unavailable')
		  local state={original=debug_removeBreakpointByID,receipt=nil}
		  state.wrapper=function(id,...)
		    local beforeCount,beforeSamples=pluginBreakpointSnapshot()
		    local results=table.pack(pcall(state.original,id,...))
		    local afterCount,afterSamples=pluginBreakpointSnapshot()
		    state.receipt={idType=type(id),id=pluginSafe(id),succeeded=results[1],
		      returnType=type(results[2]),returnValue=pluginSafe(results[2]),
		      failure=not results[1] and pluginSafe(results[2]) or results[3]~=nil and pluginSafe(results[3]) or '',
		      beforeCount=beforeCount,beforeSamples=beforeSamples,afterCount=afterCount,afterSamples=afterSamples,
		      idFields=pluginBreakpointIdFields(id)}
		    if not results[1] then error(results[2],0) end
		    return table.unpack(results,2,results.n)
		  end
		  rawset(_G,key,state)
		  debug_removeBreakpointByID=state.wrapper
		  assert(debug_removeBreakpointByID==state.wrapper, 'breakpoint remover probe could not be installed')
		end
		local function pluginRemoveRemovalProbe()
		  local key='__cheatengine_mcp_lifecycle_removal_probe'
		  local state=rawget(_G,key)
		  if state==nil then return end
		  assert(debug_removeBreakpointByID==state.wrapper, 'breakpoint remover changed while its probe was installed')
		  debug_removeBreakpointByID=state.original
		  rawset(_G,key,nil)
		end
		local function pluginObserve(nonce,address)
		  local success,enabled,status,breakpoint,openedProcessId,breakpointCount,breakpointSamples=pcall(function()
		    local f=assert(getSettingsForm(), 'settings form unavailable')
		    local clb=assert(f.findComponentByName('clbPlugins'), 'plugins checklist unavailable')
		    assert(clb.Items.Count==1, 'expected exactly one owned plugin row')
		    assert(tostring(clb.Items[0])==__BASENAME__..':'..__DISPLAY__, 'owned MCP plugin row not found')
		    local requested=assert(tonumber(address), 'invalid exact breakpoint address')
		    local list=assert(debug_getBreakpointList(), 'breakpoint list unavailable')
		    local found=false
		    local count=0
		    local samples={}
		    for key,value in pairs(list) do
		      count=count+1
		      if tonumber(value)==requested then found=true end
		      if #samples<16 then samples[#samples+1]=type(key)..':'..pluginSafe(key)..'='..type(value)..':'..pluginSafe(value) end
		    end
		    table.sort(samples)
		    local status=getMainForm().Menu.findComponentByName('CheatEngineMcpStatus')
		    local statusState='absent'
		    if status~=nil then statusState=status.Caption=='MCP: Disabled' and 'disabled' or 'other' end
		    return clb.Checked[0], statusState, found, getOpenedProcessID(), count, table.concat(samples,';')
		  end)
		  if not success then pluginError(nonce,'observe',enabled); return end
		  local state=rawget(_G,'__cheatengine_mcp_lifecycle_removal_probe')
		  local receipt=state~=nil and state.receipt or nil
		  local probeStatus=state==nil and 'uninstalled' or receipt==nil and 'installed-no-call' or 'called'
		  local rawCount,rawSamples=pluginBreakpointSnapshot()
		  pluginResponse('ok\n'..nonce..'\nobserve\n'..address..'\n'..(enabled and 'enabled' or 'disabled')..'\n'..status..'\n'..(breakpoint and 'present' or 'absent')..'\n'..tostring(openedProcessId)..'\n'..tostring(breakpointCount)..'\n'..breakpointSamples..
		    '\n'..probeStatus..'\n'..(receipt and receipt.idType or '')..'\n'..(receipt and receipt.id or '')..
		    '\n'..(receipt and tostring(receipt.succeeded) or '')..'\n'..(receipt and receipt.returnType or '')..
		    '\n'..(receipt and receipt.returnValue or '')..'\n'..(receipt and receipt.failure or '')..
		    '\n'..(receipt and tostring(receipt.beforeCount) or '')..'\n'..(receipt and receipt.beforeSamples or '')..
		    '\n'..(receipt and tostring(receipt.afterCount) or '')..'\n'..(receipt and receipt.afterSamples or '')..
		    '\n'..(receipt and receipt.idFields or '')..
		    '\nraw:'..tostring(rawCount)..':'..rawSamples)
		end
		local function pluginControl()
		  local request=io.open(__REQUEST__,'r')
		  if not request then return end
		  local command=request:read('*a'); request:close(); assert(os.remove(__REQUEST__),'plugin request dequeue failed')
		  local nonce,action,address=command:match('^(%d+)\n(%a+)\n(0x[0-9A-F]+)\n$')
		  if not nonce then nonce,action=command:match('^(%d+)\n([%a_]+)\n$') end
		  assert(nonce and (action=='enable' or action=='disable' or action=='observe'
		    or action=='install_probe' or action=='remove_probe'), 'invalid fixed plugin request')
		  if action=='observe' then assert(address, 'missing exact breakpoint address'); pluginObserve(nonce,address); return end
		  if action=='install_probe' or action=='remove_probe' then
		    local success,reason=pcall(action=='install_probe' and pluginInstallRemovalProbe or pluginRemoveRemovalProbe)
		    if not success then pluginError(nonce,action,reason); return end
		    pluginResponse('ok\n'..nonce..'\n'..action); return
		  end
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
