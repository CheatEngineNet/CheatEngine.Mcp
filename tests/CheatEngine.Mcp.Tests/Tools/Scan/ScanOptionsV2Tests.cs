using System.Collections.Immutable;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;

using CheatEngine.Client;
using CheatEngine.Client.Inspection;
using CheatEngine.Client.Results;
using CheatEngine.Client.Scanning;
using CheatEngine.Mcp.Core.Contract;
using CheatEngine.Mcp.Core.Features;
using CheatEngine.Mcp.Core.Lua;
using CheatEngine.Mcp.Core.Values;
using CheatEngine.Mcp.Tests.Support;
using CheatEngine.Mcp.Tools.Memory;
using CheatEngine.Mcp.Tools.Scan;
using CheatEngine.SDK.Engine.Inspection;
using CheatEngine.SDK.Engine.Values;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using Xunit.Sdk;

namespace CheatEngine.Mcp.Tests.Tools.Scan;

/// <summary>
///     The named scanner's <c>includeMapped</c> override and the main scanner's read-only settings, over a fixed Lua
///     double that tracks Cheat Engine's <c>MEM_MAPPED</c> override.
/// </summary>
public sealed class ScanOptionsV2Tests
{
	private static CancellationToken Token => TestContext.Current.CancellationToken;

	[Fact]
	public void IncludeMapped_ScansInsideTheOverrideAndEndsItBeforeReturning()
	{
		Harness harness = new();

		ScanState status = harness.Tools.First("emu", value: "100", includeMapped: true,
			cancellationToken: Token);

		Assert.Equal(("ResultsReady", true, false),
			(status.State, harness.Session.MappedDuringScan, harness.Lua.OverrideActive));
		Assert.Equal([MappedMemoryOverride.SetScript, MappedMemoryOverride.EndScript], harness.Lua.Bodies);
		// The end runs without a token, so the caller cannot cancel it.
		Assert.False(harness.Lua.EndTokenCanBeCanceled);
		Assert.Equal(1, harness.Session.FirstCalls);
	}

	[Fact]
	public void WithoutIncludeMapped_ANamedScanRunsNoFixedLua()
	{
		Harness harness = new();

		harness.Tools.First("emu", value: "100", cancellationToken: Token);
		ScanStatusResult status = harness.Tools.GetStatus("emu", Token);

		Assert.Empty(harness.Lua.Bodies);
		Assert.Equal((1, false), (harness.Session.FirstCalls, harness.Session.MappedDuringScan));
		// Only main has Cheat Engine settings to report.
		Assert.Equal(("ResultsReady", null), (status.State, status.Settings));
	}

	[Fact]
	public void IncludeMapped_WhenTheScanFails_EndsTheOverrideAndReportsTheScanFailure()
	{
		Harness harness = new();
		harness.Session.FirstFailure = ScanFailure();

		CheatEngineToolException failed = Assert.Throws<CheatEngineToolException>(() =>
			harness.Tools.First("emu", value: "100", includeMapped: true, cancellationToken: Token));

		// The dispatch maps the Client's failure as usual once the override has ended.
		Assert.Equal(("Cheat Engine could not read the regions.", ToolHostEffect.Unknown),
			(failed.Error.Message, failed.Error.HostEffect));
		Assert.False(harness.Lua.OverrideActive);
		Assert.Equal([MappedMemoryOverride.SetScript, MappedMemoryOverride.EndScript], harness.Lua.Bodies);
	}

	[Fact]
	public void IncludeMapped_WhenTheCallerCancelsTheScan_StillEndsTheOverride()
	{
		Harness harness = new();
		using CancellationTokenSource cancellation = new();
		harness.Session.DuringFirst = cancellation.Cancel;
		harness.Session.FirstFailure = new OperationCanceledException(cancellation.Token);

		Assert.ThrowsAny<OperationCanceledException>(() =>
			harness.Tools.First("emu", value: "100", includeMapped: true, cancellationToken: cancellation.Token));

		Assert.False(harness.Lua.OverrideActive);
		Assert.Equal([MappedMemoryOverride.SetScript, MappedMemoryOverride.EndScript], harness.Lua.Bodies);
		Assert.False(harness.Lua.EndTokenCanBeCanceled);
	}

	[Fact]
	public void IncludeMapped_WithoutTheOverrideInCheatEngine_IsUnsupportedAndCreatesNoSession()
	{
		Harness harness = new();
		harness.Lua.SetError = new LuaScriptError("unsupported",
			"This Cheat Engine has no setSpecialScanOptionsOverride, so a scan cannot include mapped memory.",
			"not_started", "Omit includeMapped.");

		CheatEngineToolException refused = Assert.Throws<CheatEngineToolException>(() =>
			harness.Tools.First("emu", value: "100", includeMapped: true, cancellationToken: Token));

		Assert.Equal((ToolErrorKind.Unsupported, ToolHostEffect.NotStarted, false),
			(refused.Error.Kind, refused.Error.HostEffect, refused.Error.Retryable));
		// A refusal before the script started proves that nothing is set: no end, no session.
		Assert.Equal([MappedMemoryOverride.SetScript], harness.Lua.Bodies);
		Assert.Empty(harness.Created);
		Assert.Equal(ToolErrorKind.NotFound,
			Assert.Throws<CheatEngineToolException>(() => harness.Tools.GetStatus("emu", Token)).Error.Kind);
	}

	[Fact]
	public void IncludeMapped_WhoseOverrideFailedAfterItMayHaveStarted_StillEndsIt()
	{
		Harness harness = new();
		harness.Lua.SetError = new LuaScriptError("host_refused",
			"Cheat Engine refused the MEM_MAPPED scan override: denied", "unknown", null);

		CheatEngineToolException failed = Assert.Throws<CheatEngineToolException>(() =>
			harness.Tools.First("emu", value: "100", includeMapped: true, cancellationToken: Token));

		Assert.Equal((ToolErrorKind.HostRefused, ToolHostEffect.Unknown), (failed.Error.Kind, failed.Error.HostEffect));
		Assert.Equal([MappedMemoryOverride.SetScript, MappedMemoryOverride.EndScript], harness.Lua.Bodies);
		Assert.False(harness.Lua.OverrideActive);
		Assert.Empty(harness.Created);
	}

	[Theory]
	[InlineData(CheatEngineFailureKind.Cancelled, ToolErrorKind.Cancelled)]
	[InlineData(CheatEngineFailureKind.InvalidState, ToolErrorKind.InvalidState)]
	public void IncludeMapped_WhoseOverrideTheClientRefusedBeforeAnyEffect_RunsNoEnd(CheatEngineFailureKind kind,
		ToolErrorKind expected)
	{
		// A cancellation observed before dispatch, or a stopping activation, refuses the call before any effect.
		Harness harness = new();
		harness.Lua.SetFault = new CheatEngineFailure(kind, "Lua.Execute", "The call was refused.",
			hostEffect: CheatEngineHostEffect.NotStarted).ToException(CancellationToken.None);

		CheatEngineToolException refused = Assert.Throws<CheatEngineToolException>(() =>
			harness.Tools.First("emu", value: "100", includeMapped: true, cancellationToken: Token));

		Assert.Equal((expected, ToolHostEffect.NotStarted, "The call was refused."),
			(refused.Error.Kind, refused.Error.HostEffect, refused.Error.Message));
		// The end would also end a Lua script's own override, which MCP never changed here.
		Assert.Equal([MappedMemoryOverride.SetScript], harness.Lua.Bodies);
		Assert.Empty(harness.Created);
	}

	[Fact]
	public void IncludeMapped_WhoseOverrideCannotBeRemoved_IsPartialEffectThatKeepsTheResults()
	{
		Harness harness = new();
		harness.Lua.EndError = new LuaScriptError("host_refused",
			"Cheat Engine did not end the scan-region override: locked", "unknown", null);

		CheatEngineToolException failed = Assert.Throws<CheatEngineToolException>(() =>
			harness.Tools.First("emu", value: "100", includeMapped: true, cancellationToken: Token));

		Assert.Equal((ToolErrorKind.PartialEffect, ToolHostEffect.CleanupUnconfirmed, false),
			(failed.Error.Kind, failed.Error.HostEffect, failed.Error.Retryable));
		Assert.Equal(
			"The scan completed, but Cheat Engine's MEM_MAPPED scan override could not be removed: Cheat Engine did not end the scan-region override: locked",
			failed.Error.Message);
		Assert.Contains("includeMapped=true", failed.Error.Hint, StringComparison.Ordinal);
		ScanMappedOverrideFailure details = Assert.IsType<ScanMappedOverrideFailure>(
			failed.Error.Details!.Value.Deserialize(ScanJsonContext.Default.ScanMappedOverrideFailure));
		Assert.Equal(("emu", true, "ResultsReady"), (details.ScannerName, details.ScanCompleted,
			details.Status?.State));
		Assert.Equal("ResultsReady", harness.Tools.GetStatus("emu", Token).State);
	}

	[Fact]
	public void IncludeMapped_WhenTheScanAndTheEndFail_ReportsCheatEnginesScanErrorInThePartialEffect()
	{
		Harness harness = new();
		harness.Session.FirstFailure = ScanFailure();
		harness.Lua.EndFault = new InvalidOperationException("The Lua runtime is gone.");

		CheatEngineToolException failed = Assert.Throws<CheatEngineToolException>(() =>
			harness.Tools.First("emu", value: "100", includeMapped: true, cancellationToken: Token));

		Assert.Equal((ToolErrorKind.PartialEffect, ToolHostEffect.CleanupUnconfirmed),
			(failed.Error.Kind, failed.Error.HostEffect));
		// The Client's own failure names the scan error; an unexpected end fault is never quoted.
		Assert.Equal(
			"The scan failed (Cheat Engine could not read the regions.), and Cheat Engine's MEM_MAPPED scan override could not be removed: an unexpected fault",
			failed.Error.Message);
		Assert.Same(harness.Session.FirstFailure, failed.InnerException);
		Assert.False(failed.Error.Details!.Value.GetProperty("scanCompleted").GetBoolean());
		Assert.Equal("emu", failed.Error.Details!.Value.GetProperty("scannerName").GetString());
	}

	[Fact]
	public void IncludeMapped_WhenTheCancelledScansEndFails_SaysTheScanDidNotComplete()
	{
		Harness harness = new();
		using CancellationTokenSource cancellation = new();
		harness.Session.DuringFirst = cancellation.Cancel;
		harness.Session.FirstFailure = new OperationCanceledException(cancellation.Token);
		harness.Lua.EndError = new LuaScriptError("host_refused", "locked", "unknown", null);

		CheatEngineToolException failed = Assert.Throws<CheatEngineToolException>(() =>
			harness.Tools.First("emu", value: "100", includeMapped: true, cancellationToken: cancellation.Token));

		Assert.Equal(
			"The scan did not complete, and Cheat Engine's MEM_MAPPED scan override could not be removed: locked",
			failed.Error.Message);
	}

	[Fact]
	public void IncludeMapped_WhoseOverrideCouldNeitherBeSetNorRemoved_SaysThatNoScanRan()
	{
		Harness harness = new();
		harness.Lua.SetFault = new CheatEngineFailure(CheatEngineFailureKind.RuntimeChanged, "Lua.Execute",
			"The Lua runtime changed.", hostEffect: CheatEngineHostEffect.Unknown).ToException(CancellationToken.None);
		harness.Lua.EndError = new LuaScriptError("host_refused", "locked", "unknown", null);

		CheatEngineToolException failed = Assert.Throws<CheatEngineToolException>(() =>
			harness.Tools.First("emu", value: "100", includeMapped: true, cancellationToken: Token));

		Assert.Equal((ToolErrorKind.PartialEffect, ToolHostEffect.CleanupUnconfirmed),
			(failed.Error.Kind, failed.Error.HostEffect));
		Assert.Equal(
			"Cheat Engine's MEM_MAPPED scan override could not be set (The Lua runtime changed.), and it could not be removed: locked",
			failed.Error.Message);
		Assert.Empty(harness.Created);
		Assert.False(failed.Error.Details!.Value.GetProperty("scanCompleted").GetBoolean());
	}

	[Fact]
	public void IncludeMapped_OnMain_IsRefusedBeforeDispatchWithTheSettingHint()
	{
		Harness harness = new();

		CheatEngineToolException refused = Assert.Throws<CheatEngineToolException>(() =>
			harness.Tools.First(value: "100", includeMapped: true, cancellationToken: Token));

		Assert.Equal((ToolErrorKind.InvalidArgument, ToolHostEffect.NotStarted),
			(refused.Error.Kind, refused.Error.HostEffect));
		Assert.StartsWith("includeMapped:", refused.Error.Message, StringComparison.Ordinal);
		Assert.Contains("Edit > Settings > Scan Settings", refused.Error.Hint, StringComparison.Ordinal);
		Assert.Empty(harness.Lua.Bodies);
	}

	[Theory]
	[InlineData("held", ToolErrorKind.InvalidState)]
	[InlineData("range", ToolErrorKind.NotFound)]
	[InlineData("limit", ToolErrorKind.LimitExceeded)]
	public void IncludeMapped_RefusalsThatNeedNoOverride_ComeBeforeIt(string refusal, ToolErrorKind expected)
	{
		Harness harness = new();
		if (refusal == "held")
		{
			harness.Tools.First("emu", value: "1", cancellationToken: Token);
		}
		else if (refusal == "limit")
		{
			for (int index = 0; index < 32; index++)
			{
				harness.Tools.First($"s{index}", value: "1", cancellationToken: Token);
			}
		}

		int created = harness.Created.Count;
		CheatEngineToolException refused = Assert.Throws<CheatEngineToolException>(() => harness.Tools.First(
			"emu", value: "100", startAddress: refusal == "range" ? "missing.dll" : null,
			endAddress: refusal == "range" ? "500000" : null, includeMapped: true, cancellationToken: Token));

		Assert.Equal((expected, ToolHostEffect.NotStarted), (refused.Error.Kind, refused.Error.HostEffect));
		// No override was set or ended, so a Lua script's own override stays untouched.
		Assert.Empty(harness.Lua.Bodies);
		Assert.Equal(created, harness.Created.Count);
	}

	[Fact]
	public void NestedIncludeMappedScans_ShareOneOverrideThatTheOutermostScanEnds()
	{
		Harness harness = new();
		ScanState? inner = null;
		harness.Session.DuringFirst = () =>
			inner = harness.Tools.First("inner", value: "5", includeMapped: true, cancellationToken: Token);

		ScanState outer = harness.Tools.First("outer", value: "100", includeMapped: true, cancellationToken: Token);

		Assert.Equal(("ResultsReady", "ResultsReady"), (outer.State, inner?.State));
		Assert.Equal((true, true), (harness.Session.MappedDuringScan, harness.Created[1].MappedDuringScan));
		// The inner scan neither set the override again nor ended it under the outer scan.
		Assert.Equal([MappedMemoryOverride.SetScript, MappedMemoryOverride.EndScript], harness.Lua.Bodies);
		Assert.False(harness.Lua.OverrideActive);
	}

	[Theory]
	[InlineData(true, "named")]
	[InlineData(true, "main")]
	[InlineData(false, "mapped")]
	public void NestedScansThatDisagreeOnIncludeMapped_AreRefusedAsBusyBeforeAnyEffect(bool outerMapped,
		string inner)
	{
		// A scan nested in another scan's wait lists its regions while that scan's override is on, or off.
		Harness harness = new();
		CheatEngineToolException? refused = null;
		harness.Session.DuringFirst = () => refused = Assert.Throws<CheatEngineToolException>(() =>
			harness.Tools.First(inner == "main" ? "main" : "inner", value: "5", includeMapped: inner == "mapped",
				cancellationToken: Token));

		harness.Tools.First("outer", value: "100", includeMapped: outerMapped, cancellationToken: Token);

		Assert.NotNull(refused);
		Assert.Equal((ToolErrorKind.Busy, ToolHostEffect.NotStarted, true),
			(refused.Error.Kind, refused.Error.HostEffect, refused.Error.Retryable));
		Assert.EndsWith("scan_first was not started.", refused.Error.Message, StringComparison.Ordinal);
		Assert.Single(harness.Created);
		string[] bodies = outerMapped ? [MappedMemoryOverride.SetScript, MappedMemoryOverride.EndScript] : [];
		Assert.Equal(bodies, harness.Lua.Bodies);
		Assert.Equal(outerMapped, harness.Session.MappedDuringScan);
	}

	[Fact]
	public void MainFirst_OutsideAnyOverride_StartsCheatEnginesScan()
	{
		Harness harness = new();

		ScanState status = harness.Tools.First(value: "100", cancellationToken: Token);

		Assert.Equal(("main", "Scanning"), (status.ScannerName, status.State));
		Assert.Equal([ScanScripts.MainFirstScript], harness.Lua.Bodies);
	}

	[Fact]
	public void Composition_ScanToolsUseTheActivationsSharedOverrideOwner()
	{
		ServiceCollection services = new();
		services.AddSingleton(ClientTestDouble.Client());
		services.AddLogging();
		new CheatEngineMcpBuilder(services, CheatEngineMcpMode.Backend).AddExecutionServices().AddScanTools();
		using ServiceProvider root = services.BuildServiceProvider(new ServiceProviderOptions
		{
			ValidateOnBuild = true,
			ValidateScopes = true
		});
		using IServiceScope scope = root.CreateScope();
		ScanTools tools = scope.ServiceProvider.GetRequiredService<ScanTools>();
		MappedMemoryOverride shared = scope.ServiceProvider.GetRequiredService<MappedMemoryOverride>();

		// While another container's scan without includeMapped holds the shared owner, this one is refused.
		CheatEngineToolException refused = shared.Follow(CheatEngineToolNames.AobFind,
			() => Assert.Throws<CheatEngineToolException>(() =>
				tools.First("emu", value: "1", includeMapped: true, cancellationToken: Token)));

		Assert.Equal(ToolErrorKind.Busy, refused.Error.Kind);
		Assert.Same(shared, scope.ServiceProvider.GetRequiredService<MappedMemoryOverride>());
	}

	[Fact]
	public void MainStatus_CarriesTheReadOnlyScanSettingsThatOnlyScanGetStatusReads()
	{
		Harness harness = new();
		ScanMainSettings settings = new("0000000000000000", "00007fffffffffff", ProtectionRequirement.Required,
			ProtectionRequirement.Any, ProtectionRequirement.Excluded, true, 4, null, false, false, false,
			ScanRounding.RoundedExtreme, false, true, false, true, true, false);
		harness.Lua.MainSettings = settings;

		ScanStatusResult status = harness.Tools.GetStatus(cancellationToken: Token);
		ScanState listed = harness.Tools.ListScanners(Token).Scanners[0];

		Assert.Equal(settings, status.Settings);
		Assert.Equal([ScanScripts.MainStatusSettingsScript, ScanScripts.MainStatusScript], harness.Lua.Bodies);
		// scan_list_scanners reads main without its settings; scan_get_status adds them to the same state.
		Assert.Equal(ScanStatusResult.From(listed, settings), status);
	}

	[Fact]
	public void MainSettings_SerializeWithContractEnumsAndOmitUnavailableFields()
	{
		ScanStatusResult status = new("main", "ui", "Created", false, false,
			Settings: new ScanMainSettings(Writable: ProtectionRequirement.Any, FastScan: true, Alignment: 16,
				Rounding: ScanRounding.RoundedExtreme, MemMapped: false));

		JsonObject json = JsonSerializer.SerializeToNode(status, ScanJsonContext.Default.ScanStatusResult)!
			.AsObject();
		JsonObject settings = json["settings"]!.AsObject();

		Assert.Equal(["writable", "fastScan", "alignment", "rounding", "memMapped"],
			settings.Select(static property => property.Key));
		Assert.Equal(("any", "rounded_extreme", 16), (settings["writable"]!.GetValue<string>(),
			settings["rounding"]!.GetValue<string>(), settings["alignment"]!.GetValue<int>()));
		Assert.Equal(ScanRounding.Truncated,
			JsonSerializer.Deserialize("\"truncated\"", ScanJsonContext.Default.ScanRounding));
	}

	/// <summary>A failed first scan as the Client reports it: a Client exception, not a tool exception.</summary>
	private static CheatEngineClientException ScanFailure()
	{
		return Assert.IsAssignableFrom<CheatEngineClientException>(new CheatEngineFailure(
			CheatEngineFailureKind.IndeterminateHostResult, "ValueScans.FirstScan",
			"Cheat Engine could not read the regions.", hostEffect: CheatEngineHostEffect.Unknown).ToException(
			CancellationToken.None));
	}

	/// <summary>A scan container over named session doubles, an inspection double and a fixed Lua double.</summary>
	private sealed class Harness
	{
		public Harness()
		{
			Session = new SessionDouble(Lua);
			IValueScanner scanner = ClientTestDouble.Create<IValueScanner>((method, _) =>
			{
				Assert.Equal(nameof(IValueScanner.CreateSession), method.Name);
				SessionDouble created = Created.Count == 0 ? Session : new SessionDouble(Lua);
				Created.Add(created);
				return created.Session;
			});
			IInspectionClient inspection = ClientTestDouble.Create<IInspectionClient>(static (method, arguments) =>
				method.Name == nameof(IInspectionClient.TryResolveAddress)
					? Resolve(arguments!)
					: throw new XunitException($"Unexpected inspection call: {method.Name}."));
			IOptions<McpExecutionOptions> options = Options.Create(new McpExecutionOptions());
			ICheatEngineClient client = ClientTestDouble.Client((nameof(ICheatEngineClient.ValueScans), scanner),
				(nameof(ICheatEngineClient.Inspection), inspection));
			ToolDispatch dispatch = new(client, new McpFeatureGate(Options.Create(new McpFeatureOptions())), options,
				new DispatchStatistics(options), TimeProvider.System, new RecordingLogger<ToolDispatch>(), Lua);
			Tools = new ScanTools(dispatch, new TargetResources());
		}

		public LuaDouble Lua
		{
			get;
		} = new();

		/// <summary>The first session created, which later sessions follow.</summary>
		public SessionDouble Session
		{
			get;
		}

		public List<SessionDouble> Created
		{
			get;
		} = [];

		public ScanTools Tools
		{
			get;
		}

		private static bool Resolve(object?[] arguments)
		{
			string expression = ((SymbolExpression) arguments[0]!).Value;
			if (HexParse.TryAddress(expression, out ulong address))
			{
				arguments[2] = new Address(address);
				arguments[3] = default(CheatEngineFailure);
				return true;
			}

			arguments[2] = default(Address);
			arguments[3] = new CheatEngineFailure(CheatEngineFailureKind.NotFound, "Inspection.ResolveAddress",
				$"'{expression}' is not a symbol.", hostEffect: CheatEngineHostEffect.Completed);
			return false;
		}
	}

	/// <summary>Answers the scan scripts and tracks Cheat Engine's MEM_MAPPED override like Cheat Engine 7.7.</summary>
	private sealed class LuaDouble : IFixedLuaExecutor
	{
		private static readonly string[] KnownBodies =
		[
			MappedMemoryOverride.SetScript, MappedMemoryOverride.EndScript, ScanScripts.MainStatusSettingsScript,
			ScanScripts.MainStatusScript, ScanScripts.MainFirstScript
		];

		public List<string> Bodies
		{
			get;
		} = [];

		public bool OverrideActive
		{
			get;
			private set;
		}

		public bool? EndTokenCanBeCanceled
		{
			get;
			private set;
		}

		/// <summary>The failure the set script declares, with its host effect.</summary>
		public LuaScriptError? SetError
		{
			get;
			set;
		}

		/// <summary>An exception the Client raises for the set script.</summary>
		public Exception? SetFault
		{
			get;
			set;
		}

		/// <summary>The failure the end script declares.</summary>
		public LuaScriptError? EndError
		{
			get;
			set;
		}

		/// <summary>An exception raised for the end script.</summary>
		public Exception? EndFault
		{
			get;
			set;
		}

		public ScanMainSettings? MainSettings
		{
			get;
			set;
		}

		public LuaJsonResult<T> Execute<T>(string operation, string source, JsonTypeInfo<T> resultType,
			LuaJsonBufferPool buffers, LuaOpaqueValueHandling opaque, CancellationToken cancellationToken)
		{
			string body = KnownBodies.FirstOrDefault(known => source.EndsWith(known, StringComparison.Ordinal)) ??
						  throw new XunitException($"Unexpected fixed Lua for {operation}.");
			Bodies.Add(body);
			if (body == MappedMemoryOverride.SetScript)
			{
				cancellationToken.ThrowIfCancellationRequested();
				if (SetFault is not null)
				{
					throw SetFault;
				}

				if (SetError is { } error)
				{
					OverrideActive = error.HostEffect != "not_started";
					return new LuaJsonResult<T>(default, error, 0);
				}

				OverrideActive = true;
				return Answer(true, resultType);
			}

			if (body == MappedMemoryOverride.EndScript)
			{
				EndTokenCanBeCanceled = cancellationToken.CanBeCanceled;
				if (EndFault is not null)
				{
					throw EndFault;
				}

				if (EndError is not null)
				{
					return new LuaJsonResult<T>(default, EndError, 0);
				}

				OverrideActive = false;
				return Answer(true, resultType);
			}

			if (body == ScanScripts.MainFirstScript)
			{
				return Answer(new ScanUiStatus("main", "ui", "Scanning", true, false, null, "int32", 77), resultType);
			}

			ScanMainSettings? settings = body == ScanScripts.MainStatusSettingsScript ? MainSettings : null;
			return Answer(new ScanUiStatus("main", "ui", "Created", false, false, null, "int32", 77, null, settings),
				resultType);
		}

		private static LuaJsonResult<T> Answer<TValue, T>(TValue value, JsonTypeInfo<T> resultType)
		{
			Assert.Equal(typeof(TValue), resultType.Type);
			return new LuaJsonResult<T>((T) (object) value!, null, 0);
		}
	}

	/// <summary>One named Client session whose first scan records the override and can run work or fail.</summary>
	private sealed class SessionDouble
	{
		private readonly LuaDouble _lua;
		private ValueScanSessionState _state = ValueScanSessionState.Created;

		public SessionDouble(LuaDouble lua)
		{
			_lua = lua;
			Session = ClientTestDouble.Create<IValueScanSession>(Handle);
		}

		public IValueScanSession Session
		{
			get;
		}

		/// <summary>
		///     Work that runs inside the scan, as a dispatch queued meanwhile runs inside Cheat Engine's wait.
		/// </summary>
		public Action? DuringFirst
		{
			get;
			set;
		}

		public Exception? FirstFailure
		{
			get;
			set;
		}

		public int FirstCalls
		{
			get;
			private set;
		}

		public bool MappedDuringScan
		{
			get;
			private set;
		}

		private object? Handle(MethodInfo method, object?[]? arguments)
		{
			return method.Name switch
			{
				"get_State" => _state,
				"get_Invalidation" => ValueScanInvalidationKind.None,
				"FirstScan" => First(),
				"GetResultCount" => 1UL,
				"Read" => new ValueScanPage(0, 1,
					ImmutableArray.Create(new ValueScanMatch(new Address(0x1000), "100"))),
				"Release" => new LeaseReleaseOutcome(LeaseReleaseKind.Released, CheatEngineHostEffect.Completed),
				_ => throw new XunitException($"Unexpected session call: {method.Name}.")
			};
		}

		private object? First()
		{
			FirstCalls++;
			MappedDuringScan = _lua.OverrideActive;
			_state = ValueScanSessionState.Scanning;
			DuringFirst?.Invoke();
			if (FirstFailure is not null)
			{
				_state = ValueScanSessionState.Created;
				throw FirstFailure;
			}

			_state = ValueScanSessionState.ResultsReady;
			return null;
		}
	}
}
