namespace CheatEngine.Mcp.Tools.Scan;

/// <summary>
///     The fixed Lua bodies that drive Cheat Engine's visible main scanner; caller data reaches them only through the
///     <c>a</c> table. A precondition that a caller can fix is declared with <c>mcp.err</c> and <c>not_started</c> before
///     the first effect: <c>busy</c> while the scan runs, <c>not_attached</c> without a process and
///     <c>invalid_state</c> when the scanner holds no usable scan. A missing Cheat Engine control stays an assertion.
/// </summary>
internal static class ScanScripts
{
	/// <summary>
	///     The shared prologue: the main form and scanner, the UI value type, the busy test, the status summary and the
	///     precondition refusals.
	/// </summary>
	private const string MainContext = """
	                                   local f = assert(getMainForm(), 'Cheat Engine main form is unavailable')
	                                   local ms = assert(getCurrentMemscan(), 'Cheat Engine main scanner is unavailable')
	                                   assert(f.btnNewScan and f.btnNextScan and f.VarType and f.ScanType, 'Unsupported Cheat Engine scan controls')
	                                   local function valueType()
	                                     local types = {[1]='byte',[2]='int16',[3]='int32',[4]='int64',[5]='float',[6]='double',[7]='string',[8]='bytes'}
	                                     local result = types[f.VarType.ItemIndex] or 'unsupported'
	                                     if result == 'string' and f.cbUnicode.Checked then result = 'wstring' end
	                                     return result
	                                   end
	                                   local function openedProcessId()
	                                     local processId = getOpenedProcessID()
	                                     return math.type(processId) == 'integer' and processId > 0 and processId or nil
	                                   end
	                                   local function isBusy()
	                                     local repeating = f.findComponentByName('cbRepeatUntilStopped')
	                                     return openedProcessId() ~= nil and (not f.btnNewScan.Enabled or
	                                       (repeating ~= nil and repeating.Visible and repeating.Checked and ms.LastScanType ~= 'stNewScan'))
	                                   end
	                                   local function summary()
	                                     local processId = openedProcessId()
	                                     local attached = processId ~= nil
	                                     local busy = isBusy()
	                                     local started = ms.LastScanType ~= 'stNewScan'
	                                     local baseline = started and ms.LastScanWasRegionScan
	                                     local failure = not busy and ms.ErrorString or ''
	                                     local state
	                                     if not attached then
	                                       state = 'NoTarget'
	                                     elseif busy then
	                                       state = 'Scanning'
	                                     elseif failure ~= '' then
	                                       state = 'Failed'
	                                     elseif not started then
	                                       state = 'Created'
	                                     elseif baseline then
	                                       state = 'BaselineReady'
	                                     else
	                                       state = 'ResultsReady'
	                                     end
	                                     local count = state == 'ResultsReady' and ms.FoundList.Count or nil
	                                     return {scannerName='main',mode='ui',state=state,isScanning=busy,resultsReady=state=='ResultsReady',
	                                       count=count,valueType=valueType(),processId=processId,error=failure ~= '' and failure or nil}
	                                   end
	                                   local function refuse(kind, message, hint)
	                                     return mcp.err(kind, message, 'not_started', hint)
	                                   end
	                                   local function busy()
	                                     if isBusy() then
	                                       return refuse('busy', 'The main UI scanner is busy.',
	                                         'Poll scan_get_status until its state is not Scanning, or cancel the scan with scan_stop.')
	                                     end
	                                   end
	                                   local function detached()
	                                     if openedProcessId() == nil then
	                                       return refuse('not_attached', 'No process is open in Cheat Engine.', 'Attach a process with process_attach.')
	                                     end
	                                   end
	                                   local function failed()
	                                     local failure = ms.ErrorString or ''
	                                     if failure ~= '' then
	                                       return refuse('invalid_state', 'The main scanner failed: ' .. string.sub(failure, 1, 1024),
	                                         'Call scan_reset, then start again with scan_first.')
	                                     end
	                                   end
	                                   local function narrowable()
	                                     local refused = busy() or detached()
	                                     if refused then return refused end
	                                     if ms.LastScanType == 'stNewScan' or not f.btnNextScan.Enabled then
	                                       return refuse('invalid_state', 'The main scanner has no completed scan to narrow.',
	                                         'Start one with scan_first, then poll scan_get_status until it reports ResultsReady or BaselineReady.')
	                                     end
	                                     return failed()
	                                   end
	                                   """ + "\n";

	/// <summary>
	///     The read-only settings function: Cheat Engine's scan-panel options that a main first or next scan uses, and
	///     the region kinds of its Scan Settings page. A missing control leaves its field out; the whole table is
	///     <c>nil</c> when no control exists, because an empty table would copy as an array.
	/// </summary>
	/// <remarks>
	///     The control names are those of Cheat Engine 7.7's main form and settings form. Fast Scan's text is
	///     hexadecimal, as Cheat Engine reads it (<c>StrToInt('$' + text)</c>); rounding follows the rt1, rt2 and rt3
	///     radio buttons (Rounded (default), Rounded (extreme), Truncated). The region kinds come from the Scan
	///     Settings check boxes, so an override a Lua script set with <c>setSpecialScanOptionsOverride</c> is not seen.
	/// </remarks>
	private const string MainSettings = """
	                                    local function settings()
	                                      local function ticked(name)
	                                        local box = f.findComponentByName(name)
	                                        if box == nil then return nil end
	                                        return box.Checked == true
	                                      end
	                                      local function requirement(name)
	                                        local box = f.findComponentByName(name)
	                                        local state = box ~= nil and box.State or nil
	                                        if state == 1 or state == 'cbChecked' then return 'required' end
	                                        if state == 0 or state == 'cbUnchecked' then return 'excluded' end
	                                        if state == 2 or state == 'cbGrayed' then return 'any' end
	                                        return nil
	                                      end
	                                      local function text(name)
	                                        local edit = f.findComponentByName(name)
	                                        local value = edit ~= nil and edit.Text or nil
	                                        if type(value) ~= 'string' or #value > 256 then return nil end
	                                        return value
	                                      end
	                                      local fast = ticked('cbFastScan')
	                                      local aligned = ticked('rbFsmAligned')
	                                      local parameter = fast and text('edtAlignment') or nil
	                                      local alignment, digits
	                                      if parameter ~= nil and aligned == true then
	                                        alignment = tonumber(parameter, 16)
	                                        if math.type(alignment) ~= 'integer' or alignment < 1 or
	                                          alignment > 2147483647 then alignment = nil end
	                                      elseif parameter ~= nil and aligned == false then
	                                        digits = string.upper(parameter)
	                                      end
	                                      local rounding = ticked('rt1') and 'rounded' or
	                                        ticked('rt2') and 'rounded_extreme' or ticked('rt3') and 'truncated' or nil
	                                      local form = type(getSettingsForm) == 'function' and getSettingsForm() or nil
	                                      local function setting(name)
	                                        local box = form ~= nil and form.findComponentByName(name) or nil
	                                        if box == nil then return nil end
	                                        return box.Checked == true
	                                      end
	                                      local result = {startAddress=text('FromAddress'),
	                                        stopAddress=text('ToAddress'),writable=requirement('cbWritable'),
	                                        executable=requirement('cbExecutable'),
	                                        copyOnWrite=requirement('cbCopyOnWrite'),fastScan=fast,alignment=alignment,
	                                        lastDigits=digits,activeMemoryOnly=ticked('cbPresentMemoryOnly'),
	                                        pauseWhileScanning=ticked('cbPauseWhileScanning'),
	                                        hexadecimal=ticked('cbHexadecimal'),rounding=rounding,
	                                        simpleValuesOnly=ticked('cbFloatSimple'),
	                                        caseSensitive=ticked('cbCaseSensitive'),
	                                        codePage=ticked('cbCodePage'),memPrivate=setting('cbMemPrivate'),
	                                        memImage=setting('cbMemImage'),memMapped=setting('cbMemMapped')}
	                                      if next(result) == nil then return nil end
	                                      return result
	                                    end
	                                    """ + "\n";

	/// <summary>Returns the main scanner's status.</summary>
	internal const string MainStatusScript = MainContext + "return summary()";

	/// <summary>Returns whether the main scanner is running or repeating.</summary>
	internal const string MainBusyScript = MainContext + "return isBusy()";

	/// <summary>Returns the main scanner's status with the scan settings that its scans use.</summary>
	internal const string MainStatusSettingsScript =
		MainContext + MainSettings + "local status = summary()\nstatus.settings = settings()\nreturn status";

	/// <summary>
	///     Returns the main scanner's status, including the UI value type a next scan compares, or refuses when the scanner
	///     cannot be narrowed.
	/// </summary>
	internal const string MainNextCheckScript = MainContext + "return narrowable() or summary()";

	/// <summary>
	///     Starts a first scan through the visible controls: <c>a[1]</c> is the value type, <c>a[2]</c> the first-scan
	///     comparison index, <c>a[3]</c> the value text and <c>a[4]</c> the upper value text.
	/// </summary>
	internal const string MainFirstScript = MainContext + """
	                                                      local refused = busy() or detached()
	                                                      if refused then return refused end
	                                                      if ms.LastScanType ~= 'stNewScan' then
	                                                        return refuse('invalid_state', 'The main scanner already holds a scan.',
	                                                          "Call scan_reset, which runs Cheat Engine's New Scan, then repeat scan_first.")
	                                                      end
	                                                      local indices = {byte=1,int16=2,int32=3,int64=4,float=5,double=6,string=7,wstring=7,bytes=8}
	                                                      f.VarType.ItemIndex = assert(indices[a[1]], 'Unsupported main scan value type')
	                                                      f.VarType.OnChange(f.VarType)
	                                                      f.cbUnicode.Checked = a[1] == 'wstring'
	                                                      assert(a[2] < f.ScanType.Items.Count, 'This comparison is unavailable in the main scanner')
	                                                      f.ScanType.ItemIndex = a[2]
	                                                      f.ScanType.OnChange(f.ScanType)
	                                                      for _, name in ipairs({'cbLuaFormula','cbNot','cbRepeatUntilStopped','cbPercentage','cbCompareToSavedScan'}) do
	                                                        local control = f.findComponentByName(name)
	                                                        if control then control.Checked = false end
	                                                      end
	                                                      f.cbHexadecimal.Checked = valueType() == 'bytes'
	                                                      f.Scanvalue.Text = a[3] or ''
	                                                      local second = f.findComponentByName('scanvalue2')
	                                                      if a[4] ~= nil then assert(second, 'The upper-value control is unavailable').Text = a[4] end
	                                                      f.btnNewScan.doClick()
	                                                      return summary()
	                                                      """;

	/// <summary>
	///     Narrows the completed scan through the visible controls: <c>a[1]</c> is the next-scan comparison index,
	///     <c>a[2]</c> the value text and <c>a[3]</c> the upper value text.
	/// </summary>
	/// <remarks>
	///     Cheat Engine reads the Hex box again on every next scan, so it is set as <see cref="MainFirstScript" /> sets
	///     it (ticked only for <c>bytes</c>) and the decimal text is never read as hex. It is set before the value is
	///     written, because its click handler rewrites the value text.
	/// </remarks>
	internal const string MainNextScript = MainContext + """
	                                                     local refused = narrowable()
	                                                     if refused then return refused end
	                                                     assert(a[1] < f.ScanType.Items.Count, 'This comparison is unavailable in the main scanner')
	                                                     f.ScanType.ItemIndex = a[1]
	                                                     f.ScanType.OnChange(f.ScanType)
	                                                     for _, name in ipairs({'cbLuaFormula','cbNot','cbRepeatUntilStopped','cbPercentage','cbCompareToSavedScan'}) do
	                                                       local control = f.findComponentByName(name)
	                                                       if control then control.Checked = false end
	                                                     end
	                                                     f.cbHexadecimal.Checked = valueType() == 'bytes'
	                                                     f.Scanvalue.Text = a[2] or ''
	                                                     local second = f.findComponentByName('scanvalue2')
	                                                     if a[3] ~= nil then assert(second, 'The upper-value control is unavailable').Text = a[3] end
	                                                     f.btnNextScan.doClick()
	                                                     return summary()
	                                                     """;

	/// <summary>
	///     Copies one page of the visible found list: <c>a[1]</c> is the zero-based start index and <c>a[2]</c> the most
	///     rows. Addresses are uppercase hexadecimal without <c>0x</c> or leading zeros.
	/// </summary>
	internal const string MainReadScript = MainContext + """
	                                                     local refused = busy()
	                                                     if refused then return refused end
	                                                     if ms.LastScanType == 'stNewScan' then
	                                                       return refuse('invalid_state', 'The main scanner has no scan to read.',
	                                                         'Start one with scan_first, then poll scan_get_status until it reports ResultsReady.')
	                                                     end
	                                                     refused = failed()
	                                                     if refused then return refused end
	                                                     if ms.LastScanWasRegionScan then
	                                                       return refuse('invalid_state', 'The main scanner holds an unknown-initial baseline, which has no rows yet.',
	                                                         'Narrow it with scan_next, for example with comparison changed or unchanged, then read the results.')
	                                                     end
	                                                     local found = assert(ms.FoundList, 'The UI found list is unavailable')
	                                                     local count = found.Count
	                                                     local finish = math.min(count, a[1] + a[2])
	                                                     local rows = {}
	                                                     for i = a[1], finish - 1 do
	                                                       local value = found.Value[i] or ''
	                                                       if #value > 4096 then
	                                                         return refuse('limit_exceeded', 'A scan value exceeds the 4096-byte per-result limit.',
	                                                           'Narrow the scan so that long values drop out, or read the address with memory_read.')
	                                                       end
	                                                       local address = found.Address[i]:gsub('^0[xX]', ''):gsub('^0+', ''):upper()
	                                                       rows[#rows + 1] = {address=address == '' and '0' or address, value=value}
	                                                     end
	                                                     return {scannerName='main',mode='ui',count=count,results=rows,
	                                                       hasMore=finish<count,nextStartIndex=finish<count and finish or nil}
	                                                     """;

	/// <summary>Runs Cheat Engine's New Scan when a scan exists, showing the main window first when it is hidden.</summary>
	internal const string MainResetScript = MainContext + """
	                                                      local refused = busy()
	                                                      if refused then return refused end
	                                                      if ms.LastScanType ~= 'stNewScan' then
	                                                        if not f.Visible then f.show() end
	                                                        f.btnNewScan.doClick()
	                                                      end
	                                                      assert(ms.LastScanType == 'stNewScan', 'Cheat Engine did not reset the main scanner')
	                                                      return summary()
	                                                      """;

	/// <summary>
	///     Asks a running or repeating main scan to stop without waiting, and reports whether a stop was requested.
	/// </summary>
	/// <remarks>
	///     It unticks the Repeat box first, so that Cheat Engine does not start the next repetition, then asks a
	///     running scan to stop with the MemScan method <c>terminateScan(false)</c>, which is what Cheat Engine's own
	///     Cancel button does on its first click: the scan controller is told to terminate and the call returns at
	///     once, while the forced form would wait up to 5 seconds on the main thread. The scan ends early and keeps
	///     what it found before the stop; Cheat Engine re-enables its controls only when its main thread processes that
	///     end, so the status this script returns for a running scan still reads Scanning.
	/// </remarks>
	internal const string MainStopScript = MainContext + """
	                                                     local requested = false
	                                                     if isBusy() then
	                                                       local running = not f.btnNewScan.Enabled
	                                                       if running and type(ms.terminateScan) ~= 'function' then
	                                                         return refuse('unsupported', 'This Cheat Engine scanner cannot be cancelled through Lua.',
	                                                           'Cancel the scan in Cheat Engine.')
	                                                       end
	                                                       local repeating = f.findComponentByName('cbRepeatUntilStopped')
	                                                       if repeating ~= nil and repeating.Checked then
	                                                         repeating.Checked = false
	                                                       end
	                                                       if running then ms.terminateScan(false) end
	                                                       requested = true
	                                                     end
	                                                     return {stopRequested=requested,status=summary()}
	                                                     """;
}
