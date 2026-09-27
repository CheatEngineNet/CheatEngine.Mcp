using CheatEngine.Client;
using CheatEngine.Mcp.Tests.Support;
using CheatEngine.Mcp.Tools;

namespace CheatEngine.Mcp.Tests.NativeLua;

public sealed partial class NativeLuaToolRuntimeTests
{
	[Fact]
	public void MainScanner_DefaultFirstNextReset_UpdatesVisibleResultsThroughCeActions()
	{
		using RuntimeScope scope = CreateScope();
		InstallMainScanner();
		ScanTool tool = new(CreateDirectLuaClient());

		Dictionary<string, object?> started = UiScanResult(tool.MemoryScan(value: "25"));
		Assert.Equal("main", started["scannerName"]);
		Assert.Equal("ui", started["mode"]);
		Assert.Equal("Scanning", started["state"]);
		Assert.Equal(1L, ReadGlobal("firstCalls"));
		Assert.Equal("25", ReadGlobal("requestedValue"));
		Assert.Equal(false, ReadGlobal("modifier"));
		Assert.False(ToolResultAssert.GetProperty<bool>(tool.MemoryScan(value: "26"), "success"));

		InstallStubs("complete(false)");
		Dictionary<string, object?> page = UiScanResult(tool.GetMemoryScanResults(maximumResults: 2));
		Assert.Equal(3L, page["count"]);
		Assert.Equal(2L, page["nextStartIndex"]);
		Assert.Equal(true, page["hasMore"]);
		object?[] matches = Assert.IsType<object?[]>(page["results"]);
		Assert.Equal(2, matches.Length);
		Assert.Equal("0xABCD", Assert.IsType<Dictionary<string, object?>>(matches[0])["address"]);
		Assert.Equal("25", Assert.IsType<Dictionary<string, object?>>(matches[0])["value"]);
		Assert.Equal("ResultsReady", UiScanResult(tool.GetMemoryScanStatus())["state"]);
		Assert.False(ToolResultAssert.GetProperty<bool>(tool.MemoryScan(value: "26"), "success"));

		Assert.Equal("Scanning", UiScanResult(tool.NextMemoryScan(value: "26"))["state"]);
		Assert.Equal(1L, ReadGlobal("nextCalls"));
		Assert.Equal("26", ReadGlobal("requestedValue"));
		InstallStubs("complete(false); ms.FoundList.Count=1; ms.FoundList.Value[0]='26'");
		Assert.Equal(1L, UiScanResult(tool.GetMemoryScanResults())["count"]);
		Assert.Equal("Created", UiScanResult(tool.ResetMemoryScan())["state"]);
		Assert.Equal(1L, ReadGlobal("resetCalls"));
		tool.Dispose();
		Assert.Equal(1L, ReadGlobal("resetCalls"));
	}

	[Fact]
	public void MainScanner_HiddenWindowReset_ShowsWindowBeforeNativeFocusAction()
	{
		using RuntimeScope scope = CreateScope();
		InstallMainScanner();
		InstallStubs("complete(false); f.Visible=false");
		ScanTool tool = new(CreateDirectLuaClient());
		Assert.Equal("Created", UiScanResult(tool.ResetMemoryScan())["state"]);
		Assert.Equal(1L, ReadGlobal("showCalls"));
		Assert.Equal(1L, ReadGlobal("resetCalls"));
	}

	[Fact]
	public void MainScanner_ManualScanAndTabSwitch_ReadsTheCurrentlyVisibleFoundList()
	{
		using RuntimeScope scope = CreateScope();
		InstallMainScanner();
		ScanTool tool = new(CreateDirectLuaClient());
		InstallStubs("complete(false)");

		Assert.Equal(3L, UiScanResult(tool.GetMemoryScanResults())["count"]);
		InstallStubs(
			"ms={LastScanType='stNextScan',LastScanWasRegionScan=false,ErrorString='',FoundList={Count=1,Address={[0]='ff01'},Value={[0]='99'}}}");
		Dictionary<string, object?> switched = UiScanResult(tool.GetMemoryScanResults());
		Dictionary<string, object?> match =
			Assert.IsType<Dictionary<string, object?>>(Assert.Single(Assert.IsType<object?[]>(switched["results"])));
		Assert.Equal("0xFF01", match["address"]);
		Assert.Equal("99", match["value"]);
		Assert.Equal(0L, ReadGlobal("firstCalls"));
		Assert.Equal(0L, ReadGlobal("nextCalls"));
		Assert.Empty(Assert.IsType<object?[]>(UiScanResult(tool.GetMemoryScanResults(startIndex: 10))["results"]));

		object list = tool.ListMemoryScanners();
		ToolResultAssert.IsSuccess(list);
		Dictionary<string, object?> listed =
			Assert.IsType<Dictionary<string, object?>>(
				Assert.Single(ToolResultAssert.GetProperty<List<object>>(list, "scanners")));
		Assert.Equal(1L, listed["count"]);
		tool.Dispose();
		Assert.Equal(0L, ReadGlobal("resetCalls"));
	}

	[Fact]
	public void MainScanner_UnknownInitialBaseline_RequiresNarrowingBeforeReading()
	{
		using RuntimeScope scope = CreateScope();
		InstallMainScanner();
		ScanTool tool = new(CreateDirectLuaClient());
		UiScanResult(tool.MemoryScan(comparison: "unknown"));
		Assert.Equal(4L, ReadGlobal("requestedComparison"));
		InstallStubs("complete(true)");

		Assert.Equal("BaselineReady", UiScanResult(tool.GetMemoryScanStatus())["state"]);
		Assert.False(ToolResultAssert.GetProperty<bool>(tool.GetMemoryScanResults(), "success"));
		UiScanResult(tool.NextMemoryScan(comparison: "unchanged"));
		Assert.Equal(9L, ReadGlobal("requestedComparison"));
		InstallStubs("complete(false)");
		Assert.Equal(true, UiScanResult(tool.GetMemoryScanStatus())["resultsReady"]);
	}

	[Fact]
	public void MainScanner_Busy_RefusesReadsResetAndTargetSwitchWithoutMutatingState()
	{
		using RuntimeScope scope = CreateScope();
		InstallMainScanner();
		ICheatEngineClient client = CreateDirectLuaClient();
		ScanTool tool = new(client);
		InstallStubs("f.btnNewScan.Enabled=false");

		Assert.Contains("main UI scanner is busy",
			ToolResultAssert.GetProperty<string>(tool.ResetMemoryScan(), "error"));
		Assert.Contains("main UI scanner is busy",
			ToolResultAssert.GetProperty<string>(tool.GetMemoryScanResults(), "error"));
		Assert.Contains("main UI scanner is busy",
			ToolResultAssert.GetProperty<string>(tool.NextMemoryScan(value: "25"), "error"));
		ProcessTool processes = new(client, TestRuntime.Info, scans: tool);
		ToolResultAssert.IsFailure(processes.OpenProcess("another-process"),
			"The main UI scanner is busy. Wait for it to finish or cancel it in Cheat Engine before switching targets.");
		InstallStubs(
			"created=0; files=0; createProcess=function() created=created+1; getOpenedProcessID=function() return 88 end end; openFileAsProcess=function() files=files+1 end; getOpenedFileSize=function() return 4 end");
		LuaProcessTool luaProcesses = new(client, scans: tool);
		ToolResultAssert.IsFailure(luaProcesses.CreateProcess("disposable.exe"),
			"The main UI scanner is busy. Wait for it to finish or cancel it in Cheat Engine before switching targets.");
		ToolResultAssert.IsFailure(luaProcesses.OpenFileAsProcess("disposable.bin"),
			"The main UI scanner is busy. Wait for it to finish or cancel it in Cheat Engine before switching targets.");
		Assert.Equal(0L, ReadGlobal("created"));
		Assert.Equal(0L, ReadGlobal("files"));
		InstallStubs("f.btnNewScan.Enabled=true");
		ToolResultAssert.IsSuccess(luaProcesses.CreateProcess("disposable.exe"));
		ToolResultAssert.IsSuccess(luaProcesses.OpenFileAsProcess("disposable.bin"));
		Assert.Equal(1L, ReadGlobal("created"));
		Assert.Equal(1L, ReadGlobal("files"));
		Assert.Equal(0L, ReadGlobal("resetCalls"));
		Assert.Equal(0L, ReadGlobal("firstCalls"));
		Assert.Equal(0L, ReadGlobal("nextCalls"));
	}

	[Fact]
	public void MainScanner_RepeatDelay_RefusesTargetChangesAndResetBetweenIterations()
	{
		using RuntimeScope scope = CreateScope();
		InstallMainScanner();
		InstallStubs("complete(false); f.cbRepeatUntilStopped={Visible=true,Checked=true}");
		ScanTool tool = new(CreateDirectLuaClient());
		Assert.Equal("Scanning", UiScanResult(tool.GetMemoryScanStatus())["state"]);
		Assert.NotNull(tool.PrepareForTargetChange());
		Assert.Contains("main UI scanner is busy",
			ToolResultAssert.GetProperty<string>(tool.ResetMemoryScan(), "error"));
		Assert.Contains("main UI scanner is busy",
			ToolResultAssert.GetProperty<string>(tool.NextMemoryScan(value: "25"), "error"));
		Assert.Equal(0L, ReadGlobal("resetCalls"));
		InstallStubs("f.cbRepeatUntilStopped.Checked=false");
		Assert.Null(tool.PrepareForTargetChange());
		Assert.Equal("Created", UiScanResult(tool.ResetMemoryScan())["state"]);
	}

	[Fact]
	public void MainScanner_CeResetsAfterFailedOrCancelledStart_DoesNotClaimResultsReady()
	{
		using RuntimeScope scope = CreateScope();
		InstallMainScanner();
		ScanTool tool = new(CreateDirectLuaClient());
		UiScanResult(tool.MemoryScan(value: "25"));
		InstallStubs("f.btnNewScan.Enabled=true; ms.LastScanType='stNewScan'; ms.ErrorString=''");
		Dictionary<string, object?> status = UiScanResult(tool.GetMemoryScanStatus());
		Assert.Equal("Created", status["state"]);
		Assert.Equal(false, status["resultsReady"]);
		Assert.False(ToolResultAssert.GetProperty<bool>(tool.GetMemoryScanResults(), "success"));
		Assert.Equal(1L, ReadGlobal("firstCalls"));
	}

	[Fact]
	public void MainScanner_NoTargetAndDisabledButtons_AllowsTargetSelection()
	{
		using RuntimeScope scope = CreateScope();
		InstallMainScanner();
		InstallStubs("getOpenedProcessID=function() return 0 end; f.btnNewScan.Enabled=false");
		ICheatEngineClient client = CreateDirectLuaClient();
		ScanTool tool = new(client);
		Assert.Equal("NoTarget", UiScanResult(tool.GetMemoryScanStatus())["state"]);
		Assert.Null(tool.PrepareForTargetChange());
		Assert.Contains("Open a target process",
			ToolResultAssert.GetProperty<string>(tool.MemoryScan(value: "25"), "error"));
		Assert.Equal(0L, ReadGlobal("firstCalls"));
	}

	[Fact]
	public void MainScanner_RangeAndUnicodeValues_UseControlsAndEncodedData()
	{
		using RuntimeScope scope = CreateScope();
		InstallMainScanner();
		ScanTool tool = new(CreateDirectLuaClient());
		UiScanResult(tool.MemoryScan(value: "10", comparison: "between", upperValue: "20"));
		Assert.Equal(3L, ReadGlobal("requestedComparison"));
		Assert.Equal("20", ReadGlobal("requestedUpper"));
		InstallStubs("complete(false)");
		UiScanResult(tool.ResetMemoryScan());
		const string text = "fox'); error('injected') -- \u2603";
		UiScanResult(tool.MemoryScan(valueType: "wstring", value: text));
		Assert.Equal(text, ReadGlobal("requestedValue"));
		Assert.Equal("wstring", UiScanResult(tool.GetMemoryScanStatus())["valueType"]);
	}

	[Theory]
	[InlineData(true, "exact", "10", null, 0)]
	[InlineData(true, "greater", "10", null, 1)]
	[InlineData(true, "less", "10", null, 2)]
	[InlineData(true, "between", "10", "20", 3)]
	[InlineData(true, "unknown", null, null, 4)]
	[InlineData(false, "exact", "10", null, 0)]
	[InlineData(false, "greater", "10", null, 1)]
	[InlineData(false, "less", "10", null, 2)]
	[InlineData(false, "between", "10", "20", 3)]
	[InlineData(false, "increased", null, null, 4)]
	[InlineData(false, "increasedBy", "10", null, 5)]
	[InlineData(false, "decreased", null, null, 6)]
	[InlineData(false, "decreasedBy", "10", null, 7)]
	[InlineData(false, "changed", null, null, 8)]
	[InlineData(false, "unchanged", null, null, 9)]
	public void MainScanner_Comparison_SelectsMatchingNativeOption(bool first, string comparison, string? value,
		string? upperValue, long expected)
	{
		using RuntimeScope scope = CreateScope();
		InstallMainScanner();
		ScanTool tool = new(CreateDirectLuaClient());
		if (!first)
		{
			InstallStubs("complete(false)");
		}

		UiScanResult(first
			? tool.MemoryScan(value: value, comparison: comparison, upperValue: upperValue)
			: tool.NextMemoryScan(value: value, comparison: comparison, upperValue: upperValue));
		Assert.Equal(expected, ReadGlobal("requestedComparison"));
		Assert.Equal(first ? 1L : 0L, ReadGlobal("firstCalls"));
		Assert.Equal(first ? 0L : 1L, ReadGlobal("nextCalls"));
	}

	[Theory]
	[InlineData("byte", "25", 1, false, false)]
	[InlineData("int16", "25", 2, false, false)]
	[InlineData("int32", "25", 3, false, false)]
	[InlineData("int64", "25", 4, false, false)]
	[InlineData("float", "25.25", 5, false, false)]
	[InlineData("double", "25.25", 6, false, false)]
	[InlineData("string", "fox", 7, false, false)]
	[InlineData("wstring", "fox", 7, true, false)]
	[InlineData("bytes", "AB CD", 8, false, true)]
	public void MainScanner_ValueType_SelectsMatchingNativeTypeAndEncoding(string type, string value, long expected,
		bool unicode, bool hexadecimal)
	{
		using RuntimeScope scope = CreateScope();
		InstallMainScanner();
		ScanTool tool = new(CreateDirectLuaClient());
		Dictionary<string, object?> result = UiScanResult(tool.MemoryScan(valueType: type, value: value));
		Assert.Equal(type, result["valueType"]);
		Assert.Equal(expected, ReadGlobal("requestedType"));
		Assert.Equal(unicode, ReadGlobal("requestedUnicode"));
		Assert.Equal(hexadecimal, ReadGlobal("requestedHex"));
	}

	[Theory]
	[InlineData(-1, 1)]
	[InlineData(0, 0)]
	[InlineData(0, 1025)]
	[InlineData(long.MaxValue, 1)]
	public void MainScanner_InvalidPage_RefusesBeforeLua(long startIndex, int maximumResults)
	{
		ScanTool tool = new(ClientTestDouble.Client());
		object result = tool.GetMemoryScanResults(startIndex: startIndex, maximumResults: maximumResults);
		Assert.False(ToolResultAssert.GetProperty<bool>(result, "success"));
		Assert.DoesNotContain("No client double", ToolResultAssert.GetProperty<string>(result, "error"));
	}

	[Fact]
	public void MainScanner_FailedScanOrOversizedValue_DoesNotReportReadableResults()
	{
		using RuntimeScope scope = CreateScope();
		InstallMainScanner();
		ScanTool tool = new(CreateDirectLuaClient());
		InstallStubs("complete(false); ms.ErrorString='native scan failed'");
		Dictionary<string, object?> failed = UiScanResult(tool.GetMemoryScanStatus());
		Assert.Equal("Failed", failed["state"]);
		Assert.Equal("native scan failed", failed["error"]);
		Assert.False(ToolResultAssert.GetProperty<bool>(tool.GetMemoryScanResults(), "success"));
		InstallStubs("ms.ErrorString=''; ms.FoundList.Value[0]=string.rep('x',4097)");
		Assert.False(ToolResultAssert.GetProperty<bool>(tool.GetMemoryScanResults(), "success"));
		InstallStubs("ms.FoundList.Value[0]=string.rep('x',4096)");
		Assert.Equal(3L, UiScanResult(tool.GetMemoryScanResults())["count"]);
	}

	private static Dictionary<string, object?> UiScanResult(object result)
	{
		Dictionary<string, object?> fields = Assert.IsType<Dictionary<string, object?>>(result);
		Assert.Equal(true, fields["success"]);
		return fields;
	}

	private static void InstallMainScanner()
	{
		InstallStubs("""
		             firstCalls=0; nextCalls=0; resetCalls=0; showCalls=0
		             local function owned() error('The GUI scanner must never be destroyed, waited, or initialized by MCP') end
		             ms={LastScanType='stNewScan',LastScanWasRegionScan=false,ErrorString='',destroy=owned,waitTillDone=owned}
		             ms.FoundList={Count=0,Address={[0]='abcd',[1]='ef00',[2]='ef04'},Value={[0]='25',[1]='25',[2]='25'},initialize=owned,deinitialize=owned,destroy=owned}
		             f={Visible=true,btnNewScan={Enabled=true},btnNextScan={Enabled=false},VarType={ItemIndex=3},ScanType={Items={Count=5}},
		               cbUnicode={Checked=false},cbHexadecimal={Checked=false},Scanvalue={Text=''},scanvalue2={Text=''},cbNot={Checked=true}}
		             f.findComponentByName=function(name) return f[name] end
		             f.show=function() showCalls=showCalls+1; f.Visible=true end
		             f.VarType.OnChange=function() f.ScanType.Items.Count=f.VarType.ItemIndex>=7 and 1 or 5 end
		             f.ScanType.OnChange=function() end
		             local function capture()
		               requestedValue=f.Scanvalue.Text; requestedUpper=f.scanvalue2.Text; requestedComparison=f.ScanType.ItemIndex; modifier=f.cbNot.Checked
		               requestedType=f.VarType.ItemIndex; requestedUnicode=f.cbUnicode.Checked; requestedHex=f.cbHexadecimal.Checked
		               f.btnNewScan.Enabled=false; f.btnNextScan.Enabled=false
		             end
		             f.btnNewScan.doClick=function()
		               if ms.LastScanType~='stNewScan' then assert(f.Visible, 'Cannot focus hidden scan controls'); resetCalls=resetCalls+1; ms.LastScanType='stNewScan'; ms.FoundList.Count=0; f.btnNextScan.Enabled=false; return end
		               firstCalls=firstCalls+1; capture(); ms.LastScanType='stFirstScan'
		             end
		             f.btnNextScan.doClick=function() nextCalls=nextCalls+1; capture(); ms.LastScanType='stNextScan' end
		             complete=function(baseline)
		               ms.LastScanType='stFirstScan'; ms.LastScanWasRegionScan=baseline; ms.FoundList.Count=3
		               f.btnNewScan.Enabled=true; f.btnNextScan.Enabled=true; f.ScanType.Items.Count=11
		             end
		             getMainForm=function() return f end
		             getCurrentMemscan=function() return ms end
		             getOpenedProcessID=function() return 77 end
		             """);
	}
}
