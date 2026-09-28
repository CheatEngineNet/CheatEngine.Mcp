using System.Reflection;

using CheatEngine.Client;
using CheatEngine.Client.Results;
using CheatEngine.Client.Scanning;
using CheatEngine.Mcp.Core.Contract;
using CheatEngine.Mcp.Core.Features;
using CheatEngine.Mcp.Tests.Support;
using CheatEngine.Mcp.Tools.Memory;
using CheatEngine.Mcp.Tools.Scan;

using Microsoft.Extensions.Options;

using Xunit.Sdk;

namespace CheatEngine.Mcp.Tests.NativeLua;

/// <summary>
///     Native Lua coverage of the main scanner's read-only settings and of the named scanner's <c>MEM_MAPPED</c>
///     override, with Cheat Engine 7.7's control names and <c>setSpecialScanOptionsOverride</c> stubbed.
/// </summary>
public sealed partial class NativeLuaToolRuntimeTests
{
	[Fact]
	public void ScanV2_MainScannerStatus_ReportsTheScanSettingsOfTheUi()
	{
		using RuntimeScope scope = CreateScope();
		InstallMainScanner();
		InstallStubs("""
		             f.FromAddress={Text='0000000000000000'}; f.ToAddress={Text='00007fffffffffff'}
		             f.cbWritable={State=1}; f.cbExecutable={State=2}; f.cbCopyOnWrite={State=0}
		             f.cbFastScan={Checked=true}; f.rbFsmAligned={Checked=true}; f.edtAlignment={Text='10'}
		             f.cbPresentMemoryOnly={Checked=false}; f.cbPauseWhileScanning={Checked=true}
		             f.rt1={Checked=false}; f.rt2={Checked=true}; f.rt3={Checked=false}
		             f.cbFloatSimple={Checked=true}; f.cbCaseSensitive={Checked=true}; f.cbCodePage={Checked=false}
		             local sf={cbMemPrivate={Checked=true},cbMemImage={Checked=true},cbMemMapped={Checked=false}}
		             sf.findComponentByName=function(name) return sf[name] end
		             getSettingsForm=function() return sf end
		             """);
		using ScanTools tools = new(CreateNativeDispatch(new McpFeatureOptions()), new TargetResources());

		ScanStatusResult status = tools.GetStatus(cancellationToken: Token);

		// Fast Scan's text is hexadecimal in Cheat Engine: 10 is a divisor of 16.
		Assert.Equal(new ScanMainSettings("0000000000000000", "00007fffffffffff", ProtectionRequirement.Required,
			ProtectionRequirement.Any, ProtectionRequirement.Excluded, true, 16, null, false, true, false,
			ScanRounding.RoundedExtreme, true, true, false, true, true, false), status.Settings);
		Assert.Equal(("Created", 77), (status.State, status.ProcessId));
		Assert.Equal(ScanStatusResult.From(Assert.Single(tools.ListScanners(Token).Scanners), status.Settings),
			status);
	}

	[Fact]
	public void ScanV2_MainScannerStatus_ReadsNamedBoxStatesAndLastDigitsWithoutASettingsForm()
	{
		using RuntimeScope scope = CreateScope();
		InstallMainScanner();
		InstallStubs("""
		             f.cbWritable={State='cbGrayed'}; f.cbExecutable={State='cbUnchecked'}
		             f.cbCopyOnWrite={State='cbChecked'}
		             f.cbFastScan={Checked=true}; f.rbFsmAligned={Checked=false}; f.edtAlignment={Text='a0'}
		             f.rt1={Checked=false}; f.rt2={Checked=false}; f.rt3={Checked=true}
		             """);
		using ScanTools tools = new(CreateNativeDispatch(new McpFeatureOptions()), new TargetResources());

		ScanStatusResult status = tools.GetStatus(cancellationToken: Token);

		// Without getSettingsForm the region kinds are omitted; the fixture's Hex box is the only other control.
		Assert.Equal(new ScanMainSettings(Writable: ProtectionRequirement.Any,
			Executable: ProtectionRequirement.Excluded, CopyOnWrite: ProtectionRequirement.Required, FastScan: true,
			LastDigits: "A0", Hexadecimal: false, Rounding: ScanRounding.Truncated), status.Settings);
	}

	[Theory]
	[InlineData(false, true, "10", null, null)]
	[InlineData(true, true, "zz", null, null)]
	[InlineData(true, true, "100000000", null, null)]
	[InlineData(true, true, " 8 ", 8, null)]
	[InlineData(true, false, "0f", null, "0F")]
	public void ScanV2_MainScannerStatus_ReportsFastScanOnlyWhenItApplies(bool fast, bool aligned, string text,
		int? alignment, string? lastDigits)
	{
		using RuntimeScope scope = CreateScope();
		InstallMainScanner();
		InstallStubs("f.cbFastScan={Checked=" + (fast ? "true" : "false") + "}; f.rbFsmAligned={Checked=" +
					 (aligned ? "true" : "false") + "}; f.edtAlignment={Text='" + text + "'}");
		using ScanTools tools = new(CreateNativeDispatch(new McpFeatureOptions()), new TargetResources());

		ScanMainSettings settings = tools.GetStatus(cancellationToken: Token).Settings!;

		Assert.Equal(((bool?) fast, alignment, lastDigits),
			(settings.FastScan, settings.Alignment, settings.LastDigits));
	}

	[Fact]
	public void ScanV2_MainScannerStatus_LeavesOutAnOversizedRangeAndAnAbsentSettingsTable()
	{
		using RuntimeScope scope = CreateScope();
		InstallMainScanner();
		InstallStubs("f.FromAddress={Text=string.rep('1', 257)}; f.ToAddress={Text='game.exe+1000'}");
		using ScanTools tools = new(CreateNativeDispatch(new McpFeatureOptions()), new TargetResources());

		ScanMainSettings settings = tools.GetStatus(cancellationToken: Token).Settings!;
		InstallStubs("f.FromAddress=nil; f.ToAddress=nil; f.cbHexadecimal=nil");
		ScanStatusResult bare = tools.GetStatus(cancellationToken: Token);

		Assert.Equal(((string?) null, "game.exe+1000"), (settings.StartAddress, settings.StopAddress));
		// A table without fields would copy as a JSON array, so the script omits it.
		Assert.Equal("Created", bare.State);
		Assert.Null(bare.Settings);
	}

	[Fact]
	public void ScanV2_NamedScanIncludingMapped_ScansInsideTheOverrideAndEndsIt()
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs("""
		             overrideCalls=0; mapped=nil
		             setSpecialScanOptionsOverride=function(options)
		               overrideCalls=overrideCalls+1; mapped=options.MEM_MAPPED
		             end
		             """);
		MappedScanSession session = new();
		using ScanTools tools = new(CreateNativeScanDispatch(session), new TargetResources());

		ScanState status = tools.First("emu", value: "100", includeMapped: true, cancellationToken: Token);

		Assert.Equal("ResultsReady", status.State);
		Assert.True(Assert.IsType<bool>(session.MappedDuringScan));
		// The override is set for the scan, then every override ends: {} leaves MEM_MAPPED unset.
		Assert.Equal(2L, ReadGlobal("overrideCalls"));
		Assert.Null(ReadGlobal("mapped"));
	}

	[Fact]
	public void ScanV2_NamedScanIncludingMapped_WithoutTheOverride_IsUnsupportedBeforeAnySession()
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs("setSpecialScanOptionsOverride=nil");
		MappedScanSession session = new();
		using ScanTools tools = new(CreateNativeScanDispatch(session), new TargetResources());

		CheatEngineToolException refused = Assert.Throws<CheatEngineToolException>(() =>
			tools.First("emu", value: "100", includeMapped: true, cancellationToken: Token));

		Assert.Equal((ToolErrorKind.Unsupported, ToolHostEffect.NotStarted, CheatEngineToolNames.ScanFirst),
			(refused.Error.Kind, refused.Error.HostEffect, refused.Error.Operation));
		Assert.Equal(
			"This Cheat Engine has no setSpecialScanOptionsOverride, so a scan cannot include mapped memory.",
			refused.Error.Message);
		Assert.Equal("Omit includeMapped, or ask the user to tick MEM_MAPPED under Edit > Settings > Scan Settings.",
			refused.Error.Hint);
		Assert.Equal((0, 0), (session.Created, session.FirstCalls));
	}

	[Fact]
	public void ScanV2_NamedScanIncludingMapped_WhoseOverrideRaises_IsHostRefusedAfterTheEnd()
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs("""
		             overrideCalls=0; mapped=nil
		             setSpecialScanOptionsOverride=function(options)
		               overrideCalls=overrideCalls+1
		               if options.MEM_MAPPED then error('denied', 0) end
		               mapped=options.MEM_MAPPED
		             end
		             """);
		MappedScanSession session = new();
		using ScanTools tools = new(CreateNativeScanDispatch(session), new TargetResources());

		CheatEngineToolException failed = Assert.Throws<CheatEngineToolException>(() =>
			tools.First("emu", value: "100", includeMapped: true, cancellationToken: Token));

		Assert.Equal((ToolErrorKind.HostRefused, ToolHostEffect.Unknown),
			(failed.Error.Kind, failed.Error.HostEffect));
		Assert.Equal("Cheat Engine refused the MEM_MAPPED scan override: denied", failed.Error.Message);
		// The set may have run before it raised, so the end ran too; no scan started.
		Assert.Equal(2L, ReadGlobal("overrideCalls"));
		Assert.Equal((0, 0), (session.Created, session.FirstCalls));
	}

	[Fact]
	public void ScanV2_NamedScanIncludingMapped_WhoseEndRaises_IsPartialEffectWithTheResults()
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs("""
		             setSpecialScanOptionsOverride=function(options)
		               if not options.MEM_MAPPED then error(string.rep('x', 600), 0) end
		               mapped=true
		             end
		             """);
		MappedScanSession session = new();
		using ScanTools tools = new(CreateNativeScanDispatch(session), new TargetResources());

		CheatEngineToolException failed = Assert.Throws<CheatEngineToolException>(() =>
			tools.First("emu", value: "100", includeMapped: true, cancellationToken: Token));

		Assert.Equal((ToolErrorKind.PartialEffect, ToolHostEffect.CleanupUnconfirmed),
			(failed.Error.Kind, failed.Error.HostEffect));
		// Cheat Engine's error text is cut to 512 characters.
		Assert.Equal(
			"The scan completed, but Cheat Engine's MEM_MAPPED scan override could not be removed: " +
			"Cheat Engine did not end the scan-region override: " + new string('x', 512), failed.Error.Message);
		Assert.True(failed.Error.Details!.Value.GetProperty("scanCompleted").GetBoolean());
		Assert.Equal(1, session.FirstCalls);
		Assert.Equal("ResultsReady", tools.GetStatus("emu", Token).State);
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public void ScanV2_MappedOverrideEnd_EndsEveryOverrideOrIsANoOpWithoutTheFunction(bool available)
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs(available
			? "ended=nil; setSpecialScanOptionsOverride=function(options) ended=next(options) == nil end"
			: "ended=nil; setSpecialScanOptionsOverride=nil");
		ToolDispatch dispatch = CreateNativeDispatch(new McpFeatureOptions());

		bool result = dispatch.RunLua(CheatEngineToolNames.ScanFirst, MappedMemoryOverride.EndScript,
			ScanJsonContext.Default.Boolean, Token);

		Assert.True(result);
		Assert.Equal(available ? true : null, ReadGlobal("ended"));
	}

	/// <summary>A native dispatch whose Client has the test Lua state and one named value-scan session.</summary>
	private static ToolDispatch CreateNativeScanDispatch(MappedScanSession session)
	{
		IOptions<McpExecutionOptions> options = Options.Create(new McpExecutionOptions());
		ICheatEngineClient client = ClientTestDouble.Client(
			(nameof(ICheatEngineClient.Lua), CreateJsonLuaClient().Lua),
			(nameof(ICheatEngineClient.ValueScans), session.Scanner));
		return new ToolDispatch(client, new McpFeatureGate(Options.Create(new McpFeatureOptions())), options,
			new DispatchStatistics(options), TimeProvider.System, new RecordingLogger<ToolDispatch>(),
			new PluginFixedLuaExecutor(client));
	}

	/// <summary>One named session that reads the stubbed override from the Lua state while it scans.</summary>
	private sealed class MappedScanSession
	{
		private ValueScanSessionState _state = ValueScanSessionState.Created;

		public MappedScanSession()
		{
			IValueScanSession session = ClientTestDouble.Create<IValueScanSession>(Handle);
			Scanner = ClientTestDouble.Create<IValueScanner>((method, _) =>
			{
				Assert.Equal(nameof(IValueScanner.CreateSession), method.Name);
				Created++;
				return session;
			});
		}

		public IValueScanner Scanner
		{
			get;
		}

		public int Created
		{
			get;
			private set;
		}

		public int FirstCalls
		{
			get;
			private set;
		}

		public object? MappedDuringScan
		{
			get;
			private set;
		}

		private object? Handle(MethodInfo method, object?[]? arguments)
		{
			switch (method.Name)
			{
				case "FirstScan":
					FirstCalls++;
					MappedDuringScan = ReadGlobal("mapped");
					_state = ValueScanSessionState.ResultsReady;
					return null;
				case "get_State":
					return _state;
				case "GetResultCount":
					return 1UL;
				case "Release":
					return new LeaseReleaseOutcome(LeaseReleaseKind.Released, CheatEngineHostEffect.Completed);
				default:
					throw new XunitException($"Unexpected session call: {method.Name}.");
			}
		}
	}
}
