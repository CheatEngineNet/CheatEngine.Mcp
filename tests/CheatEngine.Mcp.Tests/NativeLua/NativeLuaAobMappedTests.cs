using System.Collections.Immutable;
using System.Text.Json;

using CheatEngine.Client;
using CheatEngine.Client.Results;
using CheatEngine.Client.Scanning;
using CheatEngine.Mcp.Core.Contract;
using CheatEngine.Mcp.Core.Features;
using CheatEngine.Mcp.Core.Values;
using CheatEngine.Mcp.Tests.Support;
using CheatEngine.Mcp.Tools.Aob;
using CheatEngine.SDK.Engine.Values;

using Microsoft.Extensions.Options;

namespace CheatEngine.Mcp.Tests.NativeLua;

/// <summary>
///     <c>includeMapped</c> of <c>aob_find</c> and <c>aob_find_value</c> on a real Lua 5.3 state, through the shared
///     <c>MEM_MAPPED</c> override scripts: the override is on while every scan of the call runs and ends once when
///     the call ends, also when a scan fails, and Cheat Engine's refusals reach the caller with the shared contract.
/// </summary>
public sealed partial class NativeLuaToolRuntimeTests
{
	private const string OverrideStub = """
	                                    mappedOverride = false
	                                    overrideCalls = 0
	                                    setSpecialScanOptionsOverride = function(options)
	                                      overrideCalls = overrideCalls + 1
	                                      mappedOverride = options.MEM_MAPPED == true
	                                    end
	                                    """;

	private const string MappedHint =
		"Omit includeMapped, or ask the user to tick MEM_MAPPED under Edit > Settings > Scan Settings.";

	[Fact]
	public void AobFind_IncludeMapped_OverrideIsOnDuringEveryPatternScanAndEndsOnce()
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs(OverrideStub);
		List<object?> duringScans = [];
		AobTools tools = new(MappedScanDispatch(() =>
		{
			duringScans.Add(ReadGlobal("mappedOverride"));
			return GlobalOutcome(0x401000);
		}));

		AobFindResult result = tools.Find(["48 8B", "90 90"], includeMapped: true, cancellationToken: Token);

		Assert.Equal(["401000"], result.Results[0].Matches);
		Assert.Equal(new object?[] { true, true }, duringScans);
		// One set for the whole call, then one end, whose {} leaves MEM_MAPPED unset.
		Assert.Equal(false, ReadGlobal("mappedOverride"));
		Assert.Equal(2L, ReadGlobal("overrideCalls"));
	}

	[Fact]
	public void AobFindValue_IncludeMappedScanFails_OverrideIsStillEnded()
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs(OverrideStub);
		AobTools tools = new(MappedScanDispatch(static () => new PatternScanOutcome(null,
			new CheatEngineFailure(CheatEngineFailureKind.TargetChanged, "Patterns.Scan", "Another target.",
				hostEffect: CheatEngineHostEffect.Completed), null, PatternScanHostOutcomeKind.TargetChanged,
			PatternScanRouteReason.UnscopedRequest, false)));

		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() =>
			tools.FindValue(McpValueType.Int32, "100", includeMapped: true, cancellationToken: Token));

		Assert.Equal(ToolErrorKind.TargetChanged, exception.Error.Kind);
		Assert.Equal(false, ReadGlobal("mappedOverride"));
		Assert.Equal(2L, ReadGlobal("overrideCalls"));
	}

	[Fact]
	public void AobFind_IncludeMappedWithoutTheOverride_IsUnsupportedBeforeAnyScan()
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs("setSpecialScanOptionsOverride = nil");
		int scans = 0;
		AobTools tools = new(MappedScanDispatch(() =>
		{
			scans++;
			return GlobalOutcome(0x401000);
		}));

		CheatEngineToolException refused = Assert.Throws<CheatEngineToolException>(() =>
			tools.Find(["48 8B"], includeMapped: true, cancellationToken: Token));

		Assert.Equal((ToolErrorKind.Unsupported, ToolHostEffect.NotStarted, CheatEngineToolNames.AobFind),
			(refused.Error.Kind, refused.Error.HostEffect, refused.Error.Operation));
		Assert.Equal(
			"This Cheat Engine has no setSpecialScanOptionsOverride, so a scan cannot include mapped memory.",
			refused.Error.Message);
		Assert.Equal(MappedHint, refused.Error.Hint);
		Assert.Equal(0, scans);
	}

	[Fact]
	public void AobFindValue_IncludeMappedWhoseOverrideRaises_IsHostRefusedAfterTheEnd()
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs("""
		             overrideCalls = 0
		             setSpecialScanOptionsOverride = function(options)
		               overrideCalls = overrideCalls + 1
		               if options.MEM_MAPPED then error('denied', 0) end
		             end
		             """);
		int scans = 0;
		AobTools tools = new(MappedScanDispatch(() =>
		{
			scans++;
			return GlobalOutcome(0x401000);
		}));

		CheatEngineToolException failed = Assert.Throws<CheatEngineToolException>(() =>
			tools.FindValue(McpValueType.Int32, "100", includeMapped: true, cancellationToken: Token));

		Assert.Equal((ToolErrorKind.HostRefused, ToolHostEffect.Unknown, CheatEngineToolNames.AobFindValue),
			(failed.Error.Kind, failed.Error.HostEffect, failed.Error.Operation));
		Assert.Equal(("Cheat Engine refused the MEM_MAPPED scan override: denied", MappedHint),
			(failed.Error.Message, failed.Error.Hint));
		// The set may have run before it raised, so the end ran too; no scan started.
		Assert.Equal(2L, ReadGlobal("overrideCalls"));
		Assert.Equal(0, scans);
	}

	[Fact]
	public void AobFind_IncludeMappedWhoseEndRaises_IsPartialEffectWithTheResults()
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs("""
		             setSpecialScanOptionsOverride = function(options)
		               if not options.MEM_MAPPED then error('locked', 0) end
		             end
		             """);
		AobTools tools = new(MappedScanDispatch(static () => GlobalOutcome(0x401000)));

		CheatEngineToolException failed = Assert.Throws<CheatEngineToolException>(() =>
			tools.Find(["48 8B"], includeMapped: true, cancellationToken: Token));

		Assert.Equal((ToolErrorKind.PartialEffect, ToolHostEffect.CleanupUnconfirmed, false),
			(failed.Error.Kind, failed.Error.HostEffect, failed.Error.Retryable));
		Assert.Equal(
			"The scan completed, but Cheat Engine's MEM_MAPPED scan override could not be removed: " +
			"Cheat Engine did not end the scan-region override: locked", failed.Error.Message);
		AobMappedOverrideFailure details = Assert.IsType<AobMappedOverrideFailure>(
			failed.Error.Details!.Value.Deserialize(AobJsonContext.Default.AobMappedOverrideFailure));
		Assert.True(details.ScanCompleted);
		Assert.Equal(["401000"], Assert.Single(details.Results!).Matches);
	}

	/// <summary>A dispatch whose Lua runs on the test state and whose pattern scanner is <paramref name="scan" />.</summary>
	private static ToolDispatch MappedScanDispatch(Func<PatternScanOutcome> scan)
	{
		ICheatEngineClient client = ClientTestDouble.Client(
			(nameof(ICheatEngineClient.Lua), CreateJsonLuaClient().Lua),
			(nameof(ICheatEngineClient.Patterns), ClientTestDouble.Create<IPatternScanner>((method, _) =>
			{
				Assert.Equal(nameof(IPatternScanner.ScanDetailed), method.Name);
				return scan();
			})));
		IOptions<McpExecutionOptions> options = Options.Create(new McpExecutionOptions());
		return new ToolDispatch(client, new McpFeatureGate(Options.Create(new McpFeatureOptions())), options,
			new DispatchStatistics(options), TimeProvider.System, new RecordingLogger<ToolDispatch>(),
			new PluginFixedLuaExecutor(client));
	}

	private static PatternScanOutcome GlobalOutcome(ulong match)
	{
		PatternScanMetrics metrics = new(PatternScanScope.GlobalHostScan, 1, 1, 0, 1, 0, 0, 0, true,
			TimeSpan.FromMilliseconds(2), TimeSpan.FromMilliseconds(1));
		return new PatternScanOutcome(new AobScanResult(ImmutableArray.Create(new Address(match)), false), null,
			metrics, PatternScanHostOutcomeKind.Matches, PatternScanRouteReason.UnscopedRequest, true);
	}
}
