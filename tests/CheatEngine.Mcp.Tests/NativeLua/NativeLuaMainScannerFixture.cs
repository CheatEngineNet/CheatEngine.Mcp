using CheatEngine.Mcp.Core.Contract;
using CheatEngine.Mcp.Core.Features;
using CheatEngine.Mcp.Tools.Scan;

namespace CheatEngine.Mcp.Tests.NativeLua;

/// <summary>
///     Native Lua fixture shared by scanner v2 coverage, and the main scanner's typed precondition refusals, stop
///     request, Hex box, value text and address format.
/// </summary>
public sealed partial class NativeLuaToolRuntimeTests
{
	[Fact]
	public void ScanV2_MainScannerBusy_IsARetryableBusyRefusalBeforeAnyEffect()
	{
		using RuntimeScope scope = CreateScope();
		InstallMainScanner();
		InstallStubs("complete(false); f.btnNewScan.Enabled=false");
		using ScanTools tools = new(CreateNativeDispatch(new McpFeatureOptions()), new TargetResources());

		CheatEngineToolException[] refusals =
		[
			Assert.Throws<CheatEngineToolException>(() => tools.First(value: "25", cancellationToken: Token)),
			Assert.Throws<CheatEngineToolException>(() => tools.Next(value: "25", cancellationToken: Token)),
			Assert.Throws<CheatEngineToolException>(() => tools.ListResults(cancellationToken: Token)),
			Assert.Throws<CheatEngineToolException>(() => tools.Reset(cancellationToken: Token))
		];

		Assert.All(refusals, static refusal =>
		{
			Assert.Equal((ToolErrorKind.Busy, ToolHostEffect.NotStarted, true),
				(refusal.Error.Kind, refusal.Error.HostEffect, refusal.Error.Retryable));
			Assert.Contains("scan_get_status", refusal.Error.Hint, StringComparison.Ordinal);
		});
		Assert.Equal((0L, 0L, 0L), (ReadGlobal("firstCalls"), ReadGlobal("nextCalls"), ReadGlobal("resetCalls")));
		Assert.Equal("Scanning", tools.GetStatus(cancellationToken: Token).State);
	}

	[Fact]
	public void ScanV2_MainScannerWithoutProcess_IsNotAttached()
	{
		using RuntimeScope scope = CreateScope();
		InstallMainScanner();
		InstallStubs("getOpenedProcessID=function() return 0 end");
		using ScanTools tools = new(CreateNativeDispatch(new McpFeatureOptions()), new TargetResources());

		CheatEngineToolException first =
			Assert.Throws<CheatEngineToolException>(() => tools.First(value: "25", cancellationToken: Token));
		CheatEngineToolException next =
			Assert.Throws<CheatEngineToolException>(() => tools.Next(value: "25", cancellationToken: Token));

		Assert.Equal((ToolErrorKind.NotAttached, ToolErrorKind.NotAttached), (first.Error.Kind, next.Error.Kind));
		Assert.Equal((ToolHostEffect.NotStarted, "Attach a process with process_attach."),
			(first.Error.HostEffect, first.Error.Hint));
		Assert.Equal("NoTarget", tools.GetStatus(cancellationToken: Token).State);
		Assert.Equal((0L, 0L), (ReadGlobal("firstCalls"), ReadGlobal("nextCalls")));
	}

	[Fact]
	public void ScanV2_MainScannerHoldingAScan_RefusesAnotherFirstScanAsInvalidState()
	{
		using RuntimeScope scope = CreateScope();
		InstallMainScanner();
		InstallStubs("complete(false)");
		using ScanTools tools = new(CreateNativeDispatch(new McpFeatureOptions()), new TargetResources());

		CheatEngineToolException refused =
			Assert.Throws<CheatEngineToolException>(() => tools.First(value: "25", cancellationToken: Token));

		Assert.Equal((ToolErrorKind.InvalidState, ToolHostEffect.NotStarted),
			(refused.Error.Kind, refused.Error.HostEffect));
		Assert.Contains("scan_reset", refused.Error.Hint, StringComparison.Ordinal);
		Assert.Equal(0L, ReadGlobal("firstCalls"));
		Assert.Equal("ResultsReady", tools.GetStatus(cancellationToken: Token).State);
	}

	[Fact]
	public void ScanV2_MainScannerWithoutAScan_RefusesNarrowingAndReadingAsInvalidState()
	{
		using RuntimeScope scope = CreateScope();
		InstallMainScanner();
		using ScanTools tools = new(CreateNativeDispatch(new McpFeatureOptions()), new TargetResources());

		CheatEngineToolException next =
			Assert.Throws<CheatEngineToolException>(() => tools.Next(value: "25", cancellationToken: Token));
		CheatEngineToolException read =
			Assert.Throws<CheatEngineToolException>(() => tools.ListResults(cancellationToken: Token));

		Assert.Equal((ToolErrorKind.InvalidState, ToolErrorKind.InvalidState), (next.Error.Kind, read.Error.Kind));
		Assert.Contains("scan_first", next.Error.Hint, StringComparison.Ordinal);
		Assert.Contains("scan_first", read.Error.Hint, StringComparison.Ordinal);
		Assert.Equal(0L, ReadGlobal("nextCalls"));
	}

	[Fact]
	public void ScanV2_MainScannerFailure_IsInvalidStateCarryingCheatEnginesError()
	{
		using RuntimeScope scope = CreateScope();
		InstallMainScanner();
		InstallStubs("complete(false); ms.ErrorString='Out of scan buffer memory'");
		using ScanTools tools = new(CreateNativeDispatch(new McpFeatureOptions()), new TargetResources());

		CheatEngineToolException next =
			Assert.Throws<CheatEngineToolException>(() => tools.Next(value: "25", cancellationToken: Token));
		CheatEngineToolException read =
			Assert.Throws<CheatEngineToolException>(() => tools.ListResults(cancellationToken: Token));

		Assert.Equal((ToolErrorKind.InvalidState, ToolErrorKind.InvalidState), (next.Error.Kind, read.Error.Kind));
		Assert.Contains("Out of scan buffer memory", next.Error.Message, StringComparison.Ordinal);
		Assert.Contains("scan_reset", read.Error.Hint, StringComparison.Ordinal);
		Assert.Equal(("Failed", "Out of scan buffer memory"),
			(tools.GetStatus(cancellationToken: Token).State, tools.GetStatus(cancellationToken: Token).Error));
		Assert.Equal(0L, ReadGlobal("nextCalls"));
	}

	[Theory]
	[InlineData(9, "exact", "1", ToolErrorKind.InvalidState, null)]
	[InlineData(0, "exact", "1", ToolErrorKind.InvalidState, null)]
	[InlineData(7, "greater", "1", ToolErrorKind.InvalidArgument, "comparison")]
	[InlineData(8, "changed", null, ToolErrorKind.InvalidArgument, "comparison")]
	[InlineData(7, "exact", "", ToolErrorKind.InvalidArgument, "value")]
	[InlineData(5, "exact", "NaN", ToolErrorKind.InvalidArgument, "value")]
	[InlineData(3, "exact", "1.5", ToolErrorKind.InvalidArgument, "value")]
	public void ScanV2_MainScannerNext_ChecksTheValueAgainstTheUiTypeBeforeNarrowing(long varType,
		string comparison, string? value, ToolErrorKind expected, string? parameter)
	{
		using RuntimeScope scope = CreateScope();
		InstallMainScanner();
		InstallStubs($"complete(false); f.VarType.ItemIndex={varType}");
		using ScanTools tools = new(CreateNativeDispatch(new McpFeatureOptions()), new TargetResources());

		CheatEngineToolException refused = Assert.Throws<CheatEngineToolException>(() =>
			tools.Next(value: value, comparison: comparison, cancellationToken: Token));

		Assert.Equal((expected, ToolHostEffect.NotStarted), (refused.Error.Kind, refused.Error.HostEffect));
		if (parameter is not null)
		{
			Assert.StartsWith(parameter + ":", refused.Error.Message, StringComparison.Ordinal);
		}

		Assert.Equal(0L, ReadGlobal("nextCalls"));
	}

	[Theory]
	[InlineData(true, "float", "25.256", null, "25.256001")]
	[InlineData(true, "float", "25.256", 2, "25.26")]
	[InlineData(true, "double", "0.1", null, "0.100000000000")]
	[InlineData(false, "double", "1.26", 1, "1.3")]
	public void ScanV2_MainScannerFloats_WriteTheValueWithFloatDecimals(bool first, string valueType, string value,
		int? floatDecimals, string expected)
	{
		using RuntimeScope scope = CreateScope();
		InstallMainScanner();
		if (!first)
		{
			InstallStubs("complete(false); f.VarType.ItemIndex=6");
		}

		using ScanTools tools = new(CreateNativeDispatch(new McpFeatureOptions()), new TargetResources());

		ScanState status = first
			? tools.First(valueType: valueType, value: value, floatDecimals: floatDecimals, cancellationToken: Token)
			: tools.Next(value: value, floatDecimals: floatDecimals, cancellationToken: Token);

		Assert.Equal("Scanning", status.State);
		Assert.Equal(expected, ReadGlobal("requestedValue"));
	}

	[Fact]
	public void ScanV2_MainScannerRows_UseUppercaseHexadecimalWithoutPrefixOrLeadingZeros()
	{
		using RuntimeScope scope = CreateScope();
		InstallMainScanner();
		InstallStubs("complete(false); ms.FoundList.Address={[0]='00007ff6a1b2c3d0',[1]='0x0',[2]='0X00ef04'}");
		using ScanTools tools = new(CreateNativeDispatch(new McpFeatureOptions()), new TargetResources());

		ScanResultsResult page = tools.ListResults(cancellationToken: Token);

		Assert.Equal(["7FF6A1B2C3D0", "0", "EF04"], page.Results.Select(static row => row.Address));
		Assert.Equal((3UL, false, null), (page.Count, page.HasMore, page.NextStartIndex));
	}

	[Fact]
	public void ScanV2_MainScannerOversizedValue_IsALimit()
	{
		using RuntimeScope scope = CreateScope();
		InstallMainScanner();
		InstallStubs("complete(false); ms.FoundList.Value[1]=string.rep('x', 4097)");
		using ScanTools tools = new(CreateNativeDispatch(new McpFeatureOptions()), new TargetResources());

		CheatEngineToolException refused =
			Assert.Throws<CheatEngineToolException>(() => tools.ListResults(cancellationToken: Token));

		Assert.Equal(ToolErrorKind.LimitExceeded, refused.Error.Kind);
		Assert.Contains("memory_read", refused.Error.Hint, StringComparison.Ordinal);
		Assert.Single(tools.ListResults(maximumResults: 1, cancellationToken: Token).Results);
	}

	[Fact]
	public void ScanV2_MainScannerStop_ReportsWhetherAStopWasRequested()
	{
		using RuntimeScope scope = CreateScope();
		InstallMainScanner();
		using ScanTools tools = new(CreateNativeDispatch(new McpFeatureOptions()), new TargetResources());

		ScanStopResult idle = tools.Stop(cancellationToken: Token);
		tools.First(value: "25", cancellationToken: Token);
		ScanStopResult running = tools.Stop(cancellationToken: Token);
		InstallStubs("complete(false)");

		Assert.Equal((false, false, "Created"), (idle.StopRequested, idle.Removed, idle.Status!.State));
		// A non-forced stop returns at once: the scan reads Scanning until Cheat Engine's main thread ends it.
		Assert.Equal((true, "Scanning"), (running.StopRequested, running.Status!.State));
		Assert.Equal((1L, false), (ReadGlobal("stopCalls"), ReadGlobal("stopForce")));
		Assert.Equal("ResultsReady", tools.GetStatus(cancellationToken: Token).State);
	}

	[Fact]
	public void ScanV2_MainScannerStop_UnticksRepeatSoThatAnIdleRepetitionEnds()
	{
		using RuntimeScope scope = CreateScope();
		InstallMainScanner();
		InstallStubs("complete(false); f.cbRepeatUntilStopped={Visible=true,Checked=true}");
		using ScanTools tools = new(CreateNativeDispatch(new McpFeatureOptions()), new TargetResources());

		string repeating = tools.GetStatus(cancellationToken: Token).State;
		ScanStopResult stopped = tools.Stop(cancellationToken: Token);

		Assert.Equal("Scanning", repeating);
		Assert.Equal((true, "ResultsReady"), (stopped.StopRequested, stopped.Status!.State));
		Assert.Equal(0L, ReadGlobal("stopCalls"));
		Assert.False(ReadRepeatBox());
	}

	[Fact]
	public void ScanV2_MainScannerStop_OfARunningRepetition_UnticksRepeatAndStopsTheScan()
	{
		using RuntimeScope scope = CreateScope();
		InstallMainScanner();
		InstallStubs("complete(false); f.btnNewScan.Enabled=false; f.cbRepeatUntilStopped={Visible=true,Checked=true}");
		using ScanTools tools = new(CreateNativeDispatch(new McpFeatureOptions()), new TargetResources());

		ScanStopResult stopped = tools.Stop(cancellationToken: Token);

		Assert.Equal((true, "Scanning"), (stopped.StopRequested, stopped.Status!.State));
		Assert.Equal((1L, false), (ReadGlobal("stopCalls"), ReadGlobal("stopForce")));
		Assert.False(ReadRepeatBox());
	}

	[Fact]
	public void ScanV2_MainScannerWithoutTerminateScan_IsUnsupportedBeforeAnyEffect()
	{
		using RuntimeScope scope = CreateScope();
		InstallMainScanner();
		// Cheat Engine 7.7's MemScan has terminateScan and no terminate: a scanner with only terminate cannot stop.
		InstallStubs("""
		             f.btnNewScan.Enabled=false; f.cbRepeatUntilStopped={Visible=true,Checked=true}
		             ms.terminateScan=nil; ms.terminate=function() stopCalls=stopCalls+1 end
		             """);
		using ScanTools tools = new(CreateNativeDispatch(new McpFeatureOptions()), new TargetResources());

		CheatEngineToolException refused =
			Assert.Throws<CheatEngineToolException>(() => tools.Stop(cancellationToken: Token));

		Assert.Equal((ToolErrorKind.Unsupported, ToolHostEffect.NotStarted),
			(refused.Error.Kind, refused.Error.HostEffect));
		Assert.Equal(0L, ReadGlobal("stopCalls"));
		Assert.True(ReadRepeatBox());
	}

	[Fact]
	public void ScanV2_MainScannerNext_UnticksHexBeforeWritingTheDecimalValue()
	{
		using RuntimeScope scope = CreateScope();
		InstallMainScanner();
		// Like Cheat Engine's Hex click handler, a change of the box rewrites the value text.
		InstallStubs("""
		             complete(false)
		             local hex = {Checked=true}
		             f.cbHexadecimal = setmetatable({}, {__index=hex, __newindex=function(_, key, value)
		               if key == 'Checked' and value ~= hex.Checked then f.Scanvalue.Text='rewritten' end
		               hex[key] = value
		             end})
		             """);
		using ScanTools tools = new(CreateNativeDispatch(new McpFeatureOptions()), new TargetResources());

		ScanState status = tools.Next(value: "100", cancellationToken: Token);

		Assert.Equal("Scanning", status.State);
		Assert.Equal((false, "100"), (ReadGlobal("requestedHex"), ReadGlobal("requestedValue")));
	}

	[Fact]
	public void ScanV2_MainScannerNext_TicksHexForBytes()
	{
		using RuntimeScope scope = CreateScope();
		InstallMainScanner();
		InstallStubs("complete(false); f.VarType.ItemIndex=8; f.cbHexadecimal.Checked=false");
		using ScanTools tools = new(CreateNativeDispatch(new McpFeatureOptions()), new TargetResources());

		ScanState status = tools.Next(value: "48 8B 05", cancellationToken: Token);

		Assert.Equal(("Scanning", "bytes"), (status.State, status.ValueType));
		Assert.Equal((true, 1L), (ReadGlobal("requestedHex"), ReadGlobal("nextCalls")));
	}

	private static bool ReadRepeatBox()
	{
		InstallStubs("repeatChecked=f.cbRepeatUntilStopped.Checked");
		return Assert.IsType<bool>(ReadGlobal("repeatChecked"));
	}

	private static void InstallMainScanner()
	{
		InstallStubs("""
		             firstCalls=0; nextCalls=0; resetCalls=0; showCalls=0; stopCalls=0
		             local function owned() error('The GUI scanner must never be destroyed, waited, or initialized by MCP') end
		             ms={LastScanType='stNewScan',LastScanWasRegionScan=false,ErrorString='',destroy=owned,waitTillDone=owned}
		             ms.terminateScan=function(force) stopCalls=stopCalls+1; stopForce=force end
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
