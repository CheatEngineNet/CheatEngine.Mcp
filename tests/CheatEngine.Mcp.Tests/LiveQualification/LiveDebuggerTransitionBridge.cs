using System.Globalization;

namespace CheatEngine.Mcp.Tests.LiveQualification;

/// <summary>Renders a fixed, private autorun bridge used only to diagnose an owned INT3 transition.</summary>
internal static class LiveDebuggerTransitionBridge
{
	private const int MaximumRequestBytes = 128;
	private const int MaximumCallbacks = 65535;

	internal static readonly string Template = """
		local mcp_transition_request = __REQUEST__
		local mcp_transition_response = __RESPONSE__
		local mcp_transition_probe = nil
		local mcp_transition_max_callbacks = __MAXIMUM_CALLBACKS__

		local function mcp_transition_text(value)
		  local text = tostring(value or ''):gsub('[\r\n]+', ' '):gsub('[^ -~]', '?')
		  return text:sub(1, 256)
		end
		local function mcp_transition_publish(lines)
		  local file = assert(io.open(mcp_transition_response .. '.tmp', 'w'))
		  file:write(table.concat(lines, '\n')); file:write('\n'); file:close()
		  assert(os.rename(mcp_transition_response .. '.tmp', mcp_transition_response), 'transition receipt publish failed')
		end
		local function mcp_transition_raw(name)
		  local candidate = rawget(_G, name)
		  if type(candidate) ~= 'function' then return 'false', type(candidate), type(candidate) end
		  local called, value = pcall(candidate)
		  local primitive = type(value)
		  return tostring(called), primitive, primitive == 'boolean' and tostring(value) or primitive
		end
		local function mcp_transition_value(value)
		  local primitive = type(value)
		  local numeric = primitive == 'number' and math.type(value) or 'not_number'
		  if primitive == 'nil' then return primitive, numeric, 'nil' end
		  if primitive == 'boolean' then return primitive, numeric, value and 'true' or 'false' end
		  if primitive == 'number' then return primitive, numeric, tostring(value):sub(1, 64) end
		  if primitive == 'string' then return primitive, numeric, mcp_transition_text(value):sub(1, 64) end
		  return primitive, numeric, primitive
		end
		local function mcp_transition_sample(nonce)
		  local event = mcp_transition_probe and mcp_transition_probe.event or nil
		  local raw = mcp_transition_probe and mcp_transition_probe.first or nil
		  local debuggingCalled, debuggingType, debugging = mcp_transition_raw('debug_isDebugging')
		  local canBreakCalled, canBreakType, canBreak = mcp_transition_raw('debug_canBreak')
		  local brokenCalled, brokenType, broken = mcp_transition_raw('debug_isBroken')
		  local steppingCalled, steppingType, stepping = mcp_transition_raw('debug_isStepping')
		  mcp_transition_publish({'ok', nonce, 'sample', debuggingCalled, debuggingType, debugging,
		    canBreakCalled, canBreakType, canBreak, brokenCalled, brokenType, broken, steppingCalled, steppingType, stepping,
		    event and '1' or '0', event and tostring(event.threadId) or '', event and string.format('%X', event.rip) or '',
		    tostring(mcp_transition_probe and mcp_transition_probe.callbackCount or 0),
		    raw and raw.threadType or 'none', raw and raw.threadMathType or 'not_number', raw and raw.threadValue or '',
		    raw and raw.ripType or 'none', raw and raw.ripMathType or 'not_number', raw and raw.ripValue or '',
		    raw and raw.eipType or 'none', raw and raw.eipMathType or 'not_number', raw and raw.eipValue or ''})
		end
		local function mcp_transition_install(nonce, threadId, rip)
		  assert(mcp_transition_probe == nil, 'transition probe is already installed')
		  local original = rawget(_G, 'debugger_onBreakpoint')
		  local originalType = type(original)
		  local probe = {expectedThread = threadId, expectedRip = rip, original = original, callbackCount = 0}
		  local function wrapper()
		    -- CE has set register globals. This callback does no CE, UI, or file operation.
		    local thread = rawget(_G, 'THREADID')
		    local instruction = rawget(_G, 'RIP')
		    local eip = rawget(_G, 'EIP')
		    if probe.callbackCount < mcp_transition_max_callbacks then probe.callbackCount = probe.callbackCount + 1 end
		    if probe.first == nil then
		      local threadType, threadMathType, threadValue = mcp_transition_value(thread)
		      local ripType, ripMathType, ripValue = mcp_transition_value(instruction)
		      local eipType, eipMathType, eipValue = mcp_transition_value(eip)
		      probe.first = {threadType = threadType, threadMathType = threadMathType, threadValue = threadValue,
		        ripType = ripType, ripMathType = ripMathType, ripValue = ripValue,
		        eipType = eipType, eipMathType = eipMathType, eipValue = eipValue}
		    end
		    if probe.event == nil and math.type(thread) == 'integer' and math.type(instruction) == 'integer'
		       and thread == probe.expectedThread and instruction == probe.expectedRip then
		      probe.event = {threadId = thread, rip = instruction}
		    end
		    if originalType == 'function' then return original() end
		    return 0
		  end
		  probe.wrapper = wrapper
		  mcp_transition_probe = probe
		  _G.debugger_onBreakpoint = wrapper
		  mcp_transition_publish({'ok', nonce, 'install'})
		end
		local function mcp_transition_restore(nonce)
		  local probe = assert(mcp_transition_probe, 'transition probe is not installed')
		  assert(rawget(_G, 'debugger_onBreakpoint') == probe.wrapper,
		    'debugger_onBreakpoint changed after transition probe installation')
		  _G.debugger_onBreakpoint = probe.original
		  mcp_transition_probe = nil
		  mcp_transition_publish({'ok', nonce, 'restore'})
		end
		local function mcp_transition_timer()
		  local requestFile = io.open(mcp_transition_request, 'r')
		  if not requestFile then return end
		  local request = ''
		  local nonce, command = '0', ''
		  local ok, reason = pcall(function()
		    request = requestFile:read(__MAXIMUM_REQUEST_BYTES__ + 1) or ''
		    requestFile:close()
		    nonce = request:match('^(%d+)') or '0'
		    command = request:match('^%d+\n([^\n]*)') or ''
		    assert(os.remove(mcp_transition_request), 'transition request dequeue failed')
		    assert(#request <= __MAXIMUM_REQUEST_BYTES__, 'transition request exceeds fixed bound')
		    local parsedNonce, parsedCommand = request:match('^(%d+)\n(%a+)')
		    assert(parsedNonce and (parsedCommand == 'install' or parsedCommand == 'sample' or parsedCommand == 'restore'),
		      'invalid fixed transition request')
		    if parsedCommand == 'install' then
		      local installNonce, installCommand, tid, rip = request:match('^(%d+)\n(install)\n(%d+)\n([0-9A-Fa-f]+)\n?$')
		      assert(installNonce == parsedNonce and installCommand == parsedCommand, 'install requires owned thread and RIP')
		      assert(#tid <= 10 and tonumber(tid) and tonumber(tid) >= 1 and tonumber(tid) <= 4294967295,
		        'install thread ID must be a positive DWORD')
		      assert(#rip <= 16 and not rip:match('^0+$') and tonumber(rip, 16),
		        'install RIP must be a nonzero supported-width hexadecimal address')
		      mcp_transition_install(parsedNonce, tonumber(tid), tonumber(rip, 16))
		    elseif parsedCommand == 'sample' then
		      assert(request:match('^%d+\nsample\n?$'), 'sample accepts no arguments')
		      mcp_transition_sample(parsedNonce)
		    else
		      assert(request:match('^%d+\nrestore\n?$'), 'restore accepts no arguments')
		      mcp_transition_restore(parsedNonce)
		    end
		  end)
		  if not ok then pcall(mcp_transition_publish, {'error', mcp_transition_text(nonce), mcp_transition_text(command), mcp_transition_text(reason)}) end
		end
		local mcp_transition_timer_handle = createTimer(nil, false)
		mcp_transition_timer_handle.Interval = 25
		mcp_transition_timer_handle.OnTimer = mcp_transition_timer
		mcp_transition_timer_handle.Enabled = true
		""".ReplaceLineEndings("\n").TrimEnd('\n');

	internal static string Render(string requestPath, string responsePath, Func<string, string> quote)
	{
		ArgumentNullException.ThrowIfNull(quote);
		if (!Path.IsPathFullyQualified(requestPath) || !Path.IsPathFullyQualified(responsePath))
		{
			throw new ArgumentException("The diagnostic request and response paths must be absolute.");
		}

		return Template.Replace("__REQUEST__", quote(requestPath), StringComparison.Ordinal)
			.Replace("__RESPONSE__", quote(responsePath), StringComparison.Ordinal)
			.Replace("__MAXIMUM_REQUEST_BYTES__", MaximumRequestBytes.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)
			.Replace("__MAXIMUM_CALLBACKS__", MaximumCallbacks.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal);
	}
}
