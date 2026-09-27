namespace CheatEngine.Mcp.Tests.NativeLua;

/// <summary>Native Lua fixture shared by scanner v2 coverage.</summary>
public sealed partial class NativeLuaToolRuntimeTests
{
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
