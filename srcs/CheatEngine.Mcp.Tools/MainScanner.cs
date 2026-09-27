using CheatEngine.Client;
using CheatEngine.Client.Scanning;

namespace CheatEngine.Mcp.Tools;

/// <summary>Operates CE's currently visible scan tab without owning its scanner or results.</summary>
internal static class MainScanner
{
	// CE 7.7 MainUnit: drive the same controls/actions as the user so CE owns scan callbacks,
	// FoundList initialization, progress, and button state. Never wait or replace OnScanDone.
	private const string Context = """
	                               local f=assert(getMainForm(), 'Cheat Engine main form is unavailable')
	                               local ms=assert(getCurrentMemscan(), 'Cheat Engine main scanner is unavailable')
	                               assert(f.btnNewScan and f.btnNextScan and f.VarType and f.ScanType, 'Unsupported Cheat Engine scan controls')
	                               local function valueType()
	                                 local types={[1]='byte',[2]='int16',[3]='int32',[4]='int64',[5]='float',[6]='double',[7]='string',[8]='bytes'}
	                                 local result=types[f.VarType.ItemIndex] or 'unsupported'
	                                 if result=='string' and f.cbUnicode.Checked then result='wstring' end
	                                 return result
	                               end
	                               local function isBusy()
	                                 local repeating=f.findComponentByName('cbRepeatUntilStopped')
	                                 return getOpenedProcessID()~=0 and (not f.btnNewScan.Enabled or
	                                   (repeating~=nil and repeating.Visible and repeating.Checked and ms.LastScanType~='stNewScan'))
	                               end
	                               local function summary()
	                                 local attached=getOpenedProcessID()~=0
	                                 local busy=isBusy()
	                                 local started=ms.LastScanType~='stNewScan'
	                                 local baseline=started and ms.LastScanWasRegionScan
	                                 local failure=not busy and ms.ErrorString or ''
	                                 local state=not attached and 'NoTarget' or (busy and 'Scanning' or (failure~='' and 'Failed' or (not started and 'Created' or (baseline and 'BaselineReady' or 'ResultsReady'))))
	                                 local count=state=='ResultsReady' and ms.FoundList.Count or nil
	                                 return {success=true,scannerName='main',mode='ui',state=state,isScanning=busy,
	                                   resultsReady=state=='ResultsReady',count=count,valueType=valueType(),
	                                   processId=getOpenedProcessID(),error=failure~='' and failure or nil}
	                               end
	                               local function idle()
	                                 assert(not isBusy(), 'The main UI scanner is busy; poll get_memory_scan_status or cancel it in Cheat Engine')
	                               end

	                               """;

	private const string StatusSource = Context + "return summary()";

	private const string StartSource = Context + """
	                                             idle()
	                                             assert(getOpenedProcessID()~=0, 'Open a target process before scanning')
	                                             local first=a[1]
	                                             if first then
	                                               assert(ms.LastScanType=='stNewScan', 'Reset the main scanner explicitly before starting another first scan')
	                                             else
	                                               assert(ms.LastScanType~='stNewScan' and f.btnNextScan.Enabled, 'The main scanner has no completed scan to narrow')
	                                               assert(ms.ErrorString=='', 'The main scanner failed; reset it before scanning again')
	                                             end
	                                             if first then
	                                               local indices={byte=1,int16=2,int32=3,int64=4,float=5,double=6,string=7,wstring=7,bytes=8}
	                                               f.VarType.ItemIndex=assert(indices[a[2]], 'Unsupported main scan value type')
	                                               f.VarType.OnChange(f.VarType)
	                                               f.cbUnicode.Checked=a[2]=='wstring'
	                                             end
	                                             assert(a[3]<f.ScanType.Items.Count, 'This comparison is unavailable in the main scanner')
	                                             f.ScanType.ItemIndex=a[3]
	                                             f.ScanType.OnChange(f.ScanType)
	                                             -- Explicit comparisons must not inherit a previous Lua formula, percentage, inverse,
	                                             -- repeat, or saved-baseline modifier from the UI. Memory range/region/alignment stay visible.
	                                             for _,name in ipairs({'cbLuaFormula','cbNot','cbRepeatUntilStopped','cbPercentage','cbCompareToSavedScan'}) do
	                                               local control=f.findComponentByName(name)
	                                               if control then control.Checked=false end
	                                             end
	                                             f.cbHexadecimal.Checked=valueType()=='bytes'
	                                             f.Scanvalue.Text=a[4] or ''
	                                             local second=f.findComponentByName('scanvalue2')
	                                             if a[5]~=nil then assert(second, 'The upper-value control is unavailable').Text=a[5] end
	                                             if first then f.btnNewScan.doClick() else f.btnNextScan.doClick() end
	                                             local result=summary()
	                                             result.started=true
	                                             return result
	                                             """;

	private const string ReadSource = Context + """
	                                            idle()
	                                            assert(ms.LastScanType~='stNewScan', 'The main scanner has no results')
	                                            assert(ms.ErrorString=='', 'The main scanner failed; reset it before reading results')
	                                            assert(not ms.LastScanWasRegionScan, 'Unknown-initial baseline: run next_memory_scan before reading results')
	                                            local found=assert(ms.FoundList, 'The UI found list is unavailable')
	                                            local count=found.Count
	                                            local finish=math.min(count,a[1]+a[2])
	                                            local rows={}
	                                            for i=a[1],finish-1 do
	                                              local value=found.Value[i] or ''
	                                              assert(#value<=4096, 'A scan value exceeds the 4096-byte per-result limit; narrow the scan before reading')
	                                              local address=found.Address[i]:gsub('^0[xX]',''):gsub('^0+',''):upper()
	                                              rows[#rows+1]={address='0x'..(address=='' and '0' or address),value=value}
	                                            end
	                                            local result=summary()
	                                            result.results=table.pack(table.unpack(rows))
	                                            result.hasMore=finish<count
	                                            result.nextStartIndex=finish<count and finish or nil
	                                            return result
	                                            """;

	private const string ResetSource = Context + """
	                                             idle()
	                                             if ms.LastScanType~='stNewScan' then
	                                               -- CE's New Scan handler focuses Scanvalue without an exception guard.
	                                               -- A hidden main window cannot accept focus; show it before the explicit reset.
	                                               if not f.Visible then f.show() end
	                                               f.btnNewScan.doClick()
	                                             end
	                                             assert(ms.LastScanType=='stNewScan', 'Cheat Engine did not reset the main scanner')
	                                             return summary()
	                                             """;

	internal static object Status(ICheatEngineClient client)
	{
		return Execute(client, "main_scan_status", StatusSource);
	}

	internal static string ValueType(ICheatEngineClient client)
	{
		return (string) Execute(client, "main_scan_type", Context + "return valueType()");
	}

	internal static object First(ICheatEngineClient client, ValueScanFirstRequest request)
	{
		return Execute(client, "main_scan_first", StartSource, true, TypeName(request.ValueType),
			ComparisonIndex(request.Comparison),
			request.Value?.Text, request.UpperValue?.Text);
	}

	internal static object Next(ICheatEngineClient client, ValueScanNextRequest request)
	{
		return Execute(client, "main_scan_next", StartSource, false, null, ComparisonIndex(request.Comparison),
			request.Value?.Text, request.UpperValue?.Text);
	}

	internal static object Read(ICheatEngineClient client, long startIndex, int maximumResults)
	{
		return Execute(client, "main_scan_read", ReadSource, startIndex, maximumResults);
	}

	internal static object Reset(ICheatEngineClient client)
	{
		return Execute(client, "main_scan_reset", ResetSource);
	}

	internal static object? PrepareForTargetChange(ICheatEngineClient client)
	{
		return Execute(client, "main_scan_guard", Context + "return isBusy()") is true
			? ToolExecution.Error(
				"The main UI scanner is busy. Wait for it to finish or cancel it in Cheat Engine before switching targets.")
			: null;
	}

	private static object Execute(ICheatEngineClient client, string operation, string source,
		params object?[] arguments)
	{
		return LuaToolRuntime.Execute(client, operation, source, arguments) ??
		       throw new InvalidOperationException("The main scanner returned no result.");
	}

	internal static string TypeName(ValueScanValueType type)
	{
		return type switch
		{
			ValueScanValueType.Integer8 => "byte",
			ValueScanValueType.Integer16 => "int16",
			ValueScanValueType.Integer32 => "int32",
			ValueScanValueType.Integer64 => "int64",
			ValueScanValueType.SingleFloat => "float",
			ValueScanValueType.DoubleFloat => "double",
			ValueScanValueType.Utf8String => "string",
			ValueScanValueType.Utf16String => "wstring",
			ValueScanValueType.ByteArray => "bytes",
			_ => throw new ArgumentOutOfRangeException(nameof(type))
		};
	}

	private static int ComparisonIndex(ValueScanComparison comparison)
	{
		return comparison switch
		{
			ValueScanComparison.Exact => 0,
			ValueScanComparison.BiggerThan => 1,
			ValueScanComparison.SmallerThan => 2,
			ValueScanComparison.Between => 3,
			ValueScanComparison.UnknownInitialValue or ValueScanComparison.Increased => 4,
			ValueScanComparison.IncreasedBy => 5,
			ValueScanComparison.Decreased => 6,
			ValueScanComparison.DecreasedBy => 7,
			ValueScanComparison.Changed => 8,
			ValueScanComparison.Unchanged => 9,
			_ => throw new ArgumentOutOfRangeException(nameof(comparison))
		};
	}
}
