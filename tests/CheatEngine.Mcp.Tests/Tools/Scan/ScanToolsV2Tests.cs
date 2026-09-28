using System.Collections.Immutable;
using System.Reflection;
using System.Text.Json.Nodes;

using CheatEngine.Client;
using CheatEngine.Client.Inspection;
using CheatEngine.Client.Results;
using CheatEngine.Client.Scanning;
using CheatEngine.Mcp.Core.Contract;
using CheatEngine.Mcp.Core.Features;
using CheatEngine.Mcp.Core.Values;
using CheatEngine.Mcp.Tests.Support;
using CheatEngine.Mcp.Tools.Memory;
using CheatEngine.Mcp.Tools.Scan;
using CheatEngine.SDK.Engine.Inspection;
using CheatEngine.SDK.Engine.Values;

using Microsoft.Extensions.Options;

using ModelContextProtocol.Server;

using Xunit.Sdk;

namespace CheatEngine.Mcp.Tests.Tools.Scan;

/// <summary>Independent Client scan lifecycle tests for the v2 scan tools.</summary>
public sealed class ScanToolsV2Tests
{
	private static CancellationToken Token => TestContext.Current.CancellationToken;

	[Fact]
	public void IndependentSessions_NarrowReadAndDelete_AffectOnlyTheNamedSession()
	{
		SessionProbe alpha = new(new Address(0x1111), "alpha");
		SessionProbe beta = new(new Address(0x2222), "beta");
		using ScanTools tools = CreateTools([alpha, beta]);

		Assert.True(tools.First("alpha", value: "10", cancellationToken: Token).ResultsReady);
		Assert.True(tools.First("beta", value: "20", cancellationToken: Token).ResultsReady);
		Assert.Equal("alpha", Assert.Single(tools.ListResults("alpha", cancellationToken: Token).Results).Value);
		Assert.Equal("beta", Assert.Single(tools.ListResults("beta", cancellationToken: Token).Results).Value);

		tools.Next("alpha", "50", cancellationToken: Token);
		ScanReleaseResult deleted = tools.Delete("alpha", Token);

		Assert.True(deleted.Removed);
		Assert.Null(deleted.Status);
		Assert.Equal((1, 0, 1, 0), (alpha.NextCalls, beta.NextCalls, alpha.ReleaseCalls, beta.ReleaseCalls));
		Assert.Equal("beta", Assert.Single(tools.ListResults("beta", cancellationToken: Token).Results).Value);
		CheatEngineToolException missing =
			Assert.Throws<CheatEngineToolException>(() => tools.GetStatus("alpha", Token));
		Assert.Equal(ToolErrorKind.NotFound, missing.Error.Kind);
		Assert.NotNull(missing.Error.Hint);
	}

	[Fact]
	public void IndependentResults_AddressesAreUppercaseHexadecimalWithoutPrefix()
	{
		SessionProbe probe = new(new Address(0x7FF6A1B2C3D0), "25");
		using ScanTools tools = CreateTools([probe]);
		tools.First("alpha", value: "25", cancellationToken: Token);

		ScanMatch match = Assert.Single(tools.ListResults("alpha", cancellationToken: Token).Results);

		Assert.Equal("7FF6A1B2C3D0", match.Address);
		Assert.Equal(HexFormat.Address(probe.Address), match.Address);
	}

	[Fact]
	public void IndependentResults_StartPastTheEnd_ReadAsAnEmptyLastPage()
	{
		SessionProbe probe = new(new Address(0x1111), "25");
		using ScanTools tools = CreateTools([probe]);
		tools.First("alpha", value: "25", cancellationToken: Token);

		ScanResultsResult page = tools.ListResults("alpha", 1, cancellationToken: Token);

		Assert.Equal((1UL, false, null), (page.Count, page.HasMore, page.NextStartIndex));
		Assert.Empty(page.Results);
		Assert.Equal(0, probe.ReadCalls);
	}

	[Fact]
	public void IndependentSession_ResetClearsResultsAndKeepsTheSessionAndItsResource()
	{
		SessionProbe probe = new(new Address(0x1111), "value");
		TargetResources resources = new();
		using ScanTools tools = CreateTools([probe], resources);
		tools.First("alpha", valueType: "float", value: "1.5", cancellationToken: Token);

		ScanReleaseResult reset = tools.Reset("alpha", Token);

		Assert.Equal(("alpha", false, false, false), (reset.ScannerName, reset.Removed, reset.Retryable,
			reset.RequiresManualRecovery));
		Assert.Equal(("Created", false, null), (reset.Status!.State, reset.Status.ResultsReady,
			reset.Status.ValueType));
		Assert.Equal((1, 0), (probe.ResetCalls, probe.ReleaseCalls));
		Assert.Equal("Created", tools.GetStatus("alpha", Token).State);
		Assert.Throws<CheatEngineToolException>(() => resources.EnsureCanChangeTarget(
			new TargetTransition(1, 2), Token));

		ScanState restarted = tools.First("alpha", valueType: "int16", value: "7", cancellationToken: Token);

		Assert.Equal(("ResultsReady", "int16"), (restarted.State, restarted.ValueType));
		Assert.Equal(2, probe.FirstCalls);
	}

	[Theory]
	[InlineData(ValueScanInvalidationKind.HostCallFailed)]
	[InlineData(ValueScanInvalidationKind.Unknown)]
	public void IndependentSession_ResetIsIdempotentAndRecoversAnInvalidatedSession(
		ValueScanInvalidationKind invalidation)
	{
		SessionProbe probe = new(new Address(0x1111), "value");
		using ScanTools tools = CreateTools([probe]);
		tools.First("alpha", value: "10", cancellationToken: Token);
		probe.State = ValueScanSessionState.Invalidated;
		probe.Invalidation = invalidation;

		Assert.Equal("Invalidated", tools.GetStatus("alpha", Token).State);
		Assert.Equal("Created", tools.Reset("alpha", Token).Status!.State);
		Assert.Equal("Created", tools.Reset("alpha", Token).Status!.State);

		Assert.Equal((1, 0), (probe.ResetCalls, probe.ReleaseCalls));
	}

	[Theory]
	[InlineData(ValueScanSessionState.Scanning)]
	[InlineData(ValueScanSessionState.Closed)]
	public void IndependentSession_ResetOfASessionThatAcceptsOnlyItsRelease_IsInvalidState(
		ValueScanSessionState state)
	{
		SessionProbe probe = new(new Address(0x1111), "value");
		using ScanTools tools = CreateTools([probe]);
		tools.First("alpha", value: "10", cancellationToken: Token);
		probe.State = state;

		CheatEngineToolException refused = Assert.Throws<CheatEngineToolException>(() => tools.Reset("alpha", Token));

		Assert.Equal((ToolErrorKind.InvalidState, ToolHostEffect.NotStarted),
			(refused.Error.Kind, refused.Error.HostEffect));
		Assert.Contains("scan_delete", refused.Error.Hint, StringComparison.Ordinal);
		Assert.Equal(0, probe.ResetCalls);
	}

	[Theory]
	[InlineData(ValueScanInvalidationKind.TargetChanged, LeaseReleaseKind.RefusedTargetChanged, false)]
	[InlineData(ValueScanInvalidationKind.RuntimeChanged, LeaseReleaseKind.CleanupUnavailable, true)]
	public void IndependentSession_ResetAfterATargetOrRuntimeChange_IsInvalidStatePointingAtScanDelete(
		ValueScanInvalidationKind invalidation, LeaseReleaseKind release, bool retryable)
	{
		// The Client refuses such a session's release after a target change and cannot begin it after a Lua runtime
		// change, so scan_delete reports partial_effect and the session keeps blocking a target change.
		SessionProbe probe = new(new Address(0x1111), "value", [release]);
		SessionProbe other = new(new Address(0x2222), "other");
		TargetResources resources = new();
		using ScanTools tools = CreateTools([probe, other], resources);
		tools.First("alpha", value: "10", cancellationToken: Token);
		probe.State = ValueScanSessionState.Invalidated;
		probe.Invalidation = invalidation;

		CheatEngineToolException refused = Assert.Throws<CheatEngineToolException>(() => tools.Reset("alpha", Token));

		Assert.Equal((ToolErrorKind.InvalidState, ToolHostEffect.NotStarted),
			(refused.Error.Kind, refused.Error.HostEffect));
		Assert.Equal("Release it with scan_delete and follow its hint if the release is incomplete; a new scan can " +
					 "start at once under another scannerName.", refused.Error.Hint);
		Assert.Equal(0, probe.ResetCalls);
		CheatEngineToolException incomplete =
			Assert.Throws<CheatEngineToolException>(() => tools.Delete("alpha", Token));
		Assert.Equal((ToolErrorKind.PartialEffect, retryable), (incomplete.Error.Kind, incomplete.Error.Retryable));
		Assert.Equal("alpha", tools.GetStatus("alpha", Token).ScannerName);
		Assert.Throws<CheatEngineToolException>(() => resources.EnsureCanChangeTarget(
			new TargetTransition(1, 2), Token));
		Assert.True(tools.First("beta", value: "10", cancellationToken: Token).ResultsReady);
	}

	[Fact]
	public void IndependentSession_ReleaseRefusedAfterATargetChange_IsForgottenByAnAcknowledgement()
	{
		SessionProbe probe = new(new Address(0x1111), "value", [LeaseReleaseKind.RefusedTargetChanged]);
		SessionProbe renewed = new(new Address(0x2222), "renewed");
		TargetResources resources = new();
		using ScanTools tools = CreateTools([probe, renewed], resources);
		tools.First("alpha", value: "10", cancellationToken: Token);
		probe.State = ValueScanSessionState.Invalidated;
		probe.Invalidation = ValueScanInvalidationKind.TargetChanged;
		CheatEngineToolException incomplete =
			Assert.Throws<CheatEngineToolException>(() => tools.Delete("alpha", Token));

		string id = Assert.Single(resources.List()).Id;
		resources.Acknowledge([id], Token);

		Assert.Equal((ToolErrorKind.PartialEffect, false), (incomplete.Error.Kind, incomplete.Error.Retryable));
		resources.EnsureCanChangeTarget(new TargetTransition(1, 2), Token);
		Assert.True(tools.First("alpha", value: "10", cancellationToken: Token).ResultsReady);
		Assert.Equal(1, renewed.FirstCalls);
	}

	[Fact]
	public void IndependentSession_RetryableDeleteReportsPartialEffectAndRetainsTheResource()
	{
		SessionProbe probe = new(new Address(0x1111), "value",
			[LeaseReleaseKind.CleanupUnavailable, LeaseReleaseKind.Released]);
		TargetResources resources = new();
		using ScanTools tools = CreateTools([probe], resources);
		tools.First("alpha", value: "10", cancellationToken: Token);

		CheatEngineToolException failed =
			Assert.Throws<CheatEngineToolException>(() => tools.Delete("alpha", Token));

		Assert.Equal((ToolErrorKind.PartialEffect, ToolHostEffect.Unknown, true),
			(failed.Error.Kind, failed.Error.HostEffect, failed.Error.Retryable));
		Assert.Equal("alpha", tools.GetStatus("alpha", Token).ScannerName);
		Assert.Throws<CheatEngineToolException>(() => resources.EnsureCanChangeTarget(
			new TargetTransition(1, 2), Token));
		Assert.True(tools.Delete("alpha", Token).Removed);
		resources.EnsureCanChangeTarget(new TargetTransition(1, 2), Token);
	}

	[Fact]
	public void IndependentSession_StopReleasesTheSession()
	{
		SessionProbe probe = new(new Address(0x1111), "value");
		using ScanTools tools = CreateTools([probe]);
		tools.First("alpha", value: "10", cancellationToken: Token);

		ScanStopResult stopped = tools.Stop("alpha", Token);

		Assert.Equal((true, true, null), (stopped.StopRequested, stopped.Removed, stopped.Status));
		Assert.Equal(1, probe.ReleaseCalls);
	}

	[Fact]
	public void First_InvalidTypedValue_RefusesBeforeClientDispatch()
	{
		using ScanTools tools = CreateTools([]);

		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() =>
			tools.First(value: "not-an-int32", cancellationToken: Token));

		Assert.Equal((ToolErrorKind.InvalidArgument, ToolHostEffect.NotStarted),
			(exception.Error.Kind, exception.Error.HostEffect));
	}

	[Fact]
	public void First_NamedSessionInAnotherState_AsksForAResetAndLeavesTheSession()
	{
		SessionProbe probe = new(new Address(0x1111), "value");
		using ScanTools tools = CreateTools([probe]);
		tools.First("alpha", value: "10", cancellationToken: Token);

		CheatEngineToolException refused = Assert.Throws<CheatEngineToolException>(() =>
			tools.First("alpha", value: "10", cancellationToken: Token));

		Assert.Equal(ToolErrorKind.InvalidState, refused.Error.Kind);
		Assert.Contains("scan_reset", refused.Error.Hint, StringComparison.Ordinal);
		Assert.Equal(1, probe.FirstCalls);
	}

	[Theory]
	[InlineData("string", "fox", "greater", "a", null, "comparison")]
	[InlineData("string", "fox", "changed", null, null, "comparison")]
	[InlineData("bytes", "48 8B", "increased", null, null, "comparison")]
	[InlineData("string", "fox", "exact", "", null, "value")]
	[InlineData("float", "1.5", "exact", "NaN", null, "value")]
	[InlineData("double", "1.5", "increasedBy", "-Infinity", null, "value")]
	[InlineData("int32", "10", "exact", "11", 2, "floatDecimals")]
	[InlineData("int32", "10", "exact", "0x0B", null, "value")]
	public void Next_FactoryRefusals_AreInvalidArgumentBeforeTheScan(string type, string first, string comparison,
		string? value, int? floatDecimals, string parameter)
	{
		SessionProbe probe = new(new Address(0x1111), "value");
		using ScanTools tools = CreateTools([probe]);
		tools.First("alpha", valueType: type, value: first, cancellationToken: Token);

		CheatEngineToolException refused = Assert.Throws<CheatEngineToolException>(() =>
			tools.Next("alpha", value, comparison, floatDecimals: floatDecimals, cancellationToken: Token));

		Assert.Equal((ToolErrorKind.InvalidArgument, ToolHostEffect.NotStarted),
			(refused.Error.Kind, refused.Error.HostEffect));
		Assert.StartsWith(parameter + ":", refused.Error.Message, StringComparison.Ordinal);
		Assert.Equal(0, probe.NextCalls);
	}

	[Fact]
	public void Next_ArgumentErrorsThatDoNotNeedTheType_AreRefusedBeforeDispatch()
	{
		using ScanTools tools = CreateTools([]);

		CheatEngineToolException comparison = Assert.Throws<CheatEngineToolException>(() =>
			tools.Next("missing", comparison: "unknown", cancellationToken: Token));
		CheatEngineToolException value = Assert.Throws<CheatEngineToolException>(() =>
			tools.Next("missing", "1", "changed", cancellationToken: Token));

		Assert.Equal(ToolErrorKind.InvalidArgument, comparison.Error.Kind);
		Assert.Equal(ToolErrorKind.InvalidArgument, value.Error.Kind);
	}

	[Fact]
	public void NamedSessionWithoutResults_NextAndListAreInvalidStateWithAHint()
	{
		SessionProbe probe = new(new Address(0x1111), "value");
		using ScanTools tools = CreateTools([probe]);
		tools.First("alpha", value: "10", cancellationToken: Token);
		tools.Reset("alpha", Token);

		CheatEngineToolException next = Assert.Throws<CheatEngineToolException>(() =>
			tools.Next("alpha", "11", cancellationToken: Token));
		CheatEngineToolException list = Assert.Throws<CheatEngineToolException>(() =>
			tools.ListResults("alpha", cancellationToken: Token));

		Assert.Equal((ToolErrorKind.InvalidState, ToolErrorKind.InvalidState), (next.Error.Kind, list.Error.Kind));
		Assert.NotNull(next.Error.Hint);
		Assert.NotNull(list.Error.Hint);
		Assert.Equal((0, 0), (probe.NextCalls, probe.ReadCalls));
	}

	[Fact]
	public void First_FloatDecimals_SetTheTextOfTheClientValue()
	{
		SessionProbe exact = new(new Address(0x1111), "value");
		SessionProbe rounded = new(new Address(0x2222), "value");
		using ScanTools tools = CreateTools([exact, rounded]);

		tools.First("exact", valueType: "float", value: "25.256", cancellationToken: Token);
		tools.First("rounded", valueType: "float", value: "25.256", floatDecimals: 1, cancellationToken: Token);
		tools.Next("rounded", "25", "between", "26", floatDecimals: 0, cancellationToken: Token);

		Assert.Equal("25.256001", exact.LastFirst!.Value.Value?.Text);
		Assert.Equal("25.3", rounded.LastFirst!.Value.Value?.Text);
		Assert.Equal(("25", "26"), (rounded.LastNext!.Value.Value?.Text, rounded.LastNext.Value.UpperValue?.Text));
	}

	[Fact]
	public void First_NamedScopeOptions_NarrowTheClientRequest()
	{
		SessionProbe probe = new(new Address(0x1111), "value");
		using ScanTools tools = CreateTools([probe]);

		tools.First("alpha", value: "10", startAddress: "game.exe", endAddress: "0x500000",
			writable: ProtectionRequirement.Required, executable: ProtectionRequirement.Excluded,
			alignment: 4, cancellationToken: Token);

		ValueScanFirstRequest request = probe.LastFirst!.Value;
		Assert.Equal((new Address(0x400000), new Address(0x500000)), (request.StartAddress, request.StopAddress));
		Assert.Equal(
			(ScanProtectionRequirement.Excluded, ScanProtectionRequirement.Unspecified,
				ScanProtectionRequirement.Required),
			(request.Protection.Executable, request.Protection.CopyOnWrite, request.Protection.Writable));
		Assert.Equal((ScanAlignmentMode.AlignedTo, 4), (request.Alignment.Mode, request.Alignment.Divisor));
	}

	[Fact]
	public void First_NamedScopeWithoutOptions_ScansTheWholeAddressSpaceUnfiltered()
	{
		SessionProbe probe = new(new Address(0x1111), "value");
		using ScanTools tools = CreateTools([probe]);

		tools.First("alpha", value: "10", cancellationToken: Token);

		ValueScanFirstRequest request = probe.LastFirst!.Value;
		Assert.True(request.Protection.IsUnspecified);
		Assert.Equal(ScanAlignmentMode.None, request.Alignment.Mode);
		Assert.Equal(ValueScanFirstRequest.Exact(ValueScanValue.FromInt32(10)).StopAddress, request.StopAddress);
	}

	[Theory]
	[InlineData("500000", "400000", ToolErrorKind.InvalidArgument)]
	[InlineData("400000", "400000", ToolErrorKind.InvalidArgument)]
	[InlineData("400000", "missing", ToolErrorKind.NotFound)]
	public void First_NamedScopeThatCannotBeResolved_CreatesNoSession(string start, string end,
		ToolErrorKind expected)
	{
		SessionProbe probe = new(new Address(0x1111), "value");
		Queue<SessionProbe> available = new([probe]);
		using ScanTools tools = CreateToolsFrom(available);

		CheatEngineToolException refused = Assert.Throws<CheatEngineToolException>(() =>
			tools.First("alpha", value: "10", startAddress: start, endAddress: end, cancellationToken: Token));

		Assert.Equal((expected, ToolHostEffect.NotStarted), (refused.Error.Kind, refused.Error.HostEffect));
		Assert.Single(available);
		Assert.Equal(ToolErrorKind.NotFound,
			Assert.Throws<CheatEngineToolException>(() => tools.GetStatus("alpha", Token)).Error.Kind);
	}

	[Theory]
	[InlineData("startAddress")]
	[InlineData("writable")]
	[InlineData("alignment")]
	[InlineData("lastDigits")]
	public void First_MainWithANamedOnlyOption_IsRefusedBeforeDispatch(string parameter)
	{
		using ScanTools tools = CreateTools([]);

		CheatEngineToolException refused = Assert.Throws<CheatEngineToolException>(() => tools.First(value: "10",
			startAddress: parameter == "startAddress" ? "400000" : null,
			endAddress: parameter == "startAddress" ? "500000" : null,
			writable: parameter == "writable" ? ProtectionRequirement.Required : ProtectionRequirement.Any,
			alignment: parameter == "alignment" ? 4 : null, lastDigits: parameter == "lastDigits" ? "0" : null,
			cancellationToken: Token));

		Assert.Equal((ToolErrorKind.InvalidArgument, ToolHostEffect.NotStarted),
			(refused.Error.Kind, refused.Error.HostEffect));
		Assert.StartsWith(parameter + ":", refused.Error.Message, StringComparison.Ordinal);
	}

	[Theory]
	[InlineData(nameof(ScanTools.First), McpDispatchClass.HostScan)]
	[InlineData(nameof(ScanTools.Next), McpDispatchClass.HostScan)]
	[InlineData(nameof(ScanTools.GetStatus), McpDispatchClass.Short)]
	[InlineData(nameof(ScanTools.ListResults), McpDispatchClass.Short)]
	[InlineData(nameof(ScanTools.Reset), McpDispatchClass.Short)]
	public void DispatchClasses_DeclareThatNamedScansHoldCheatEnginesMainThread(string method, string expected)
	{
		McpMetaAttribute meta = Assert.Single(typeof(ScanTools).GetMethod(method)!
			.GetCustomAttributes<McpMetaAttribute>(), static attribute => attribute.Name == McpDispatchClass.MetaKey);

		Assert.Equal(expected, JsonNode.Parse(meta.JsonValue)!.GetValue<string>());
	}

	private static ScanTools CreateTools(IReadOnlyList<SessionProbe> probes, TargetResources? resources = null)
	{
		return CreateToolsFrom(new Queue<SessionProbe>(probes), resources);
	}

	private static ScanTools CreateToolsFrom(Queue<SessionProbe> available, TargetResources? resources = null)
	{
		IValueScanner scanner = ClientTestDouble.Create<IValueScanner>((method, _) => method.Name switch
		{
			"CreateSession" when available.TryDequeue(out SessionProbe? session) => session.Session,
			_ => throw new XunitException($"Unexpected value-scanner call: {method.Name}.")
		});
		IInspectionClient inspection = ClientTestDouble.Create<IInspectionClient>(static (method, arguments) =>
			method.Name == nameof(IInspectionClient.TryResolveAddress)
				? Resolve(arguments!)
				: throw new XunitException($"Unexpected inspection call: {method.Name}."));
		IOptions<McpExecutionOptions> options = Options.Create(new McpExecutionOptions());
		ICheatEngineClient client = ClientTestDouble.Client((nameof(ICheatEngineClient.ValueScans), scanner),
			(nameof(ICheatEngineClient.Inspection), inspection));
		ToolDispatch dispatch = new(client, new McpFeatureGate(Options.Create(new McpFeatureOptions())), options,
			new DispatchStatistics(options), TimeProvider.System, new RecordingLogger<ToolDispatch>());
		return new ScanTools(dispatch, resources ?? new TargetResources());
	}

	private static bool Resolve(object?[] arguments)
	{
		string expression = ((SymbolExpression) arguments[0]!).Value;
		ulong address = 0x400000;
		if (expression == "game.exe" || HexParse.TryAddress(expression, out address))
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

	private sealed class SessionProbe
	{
		private readonly Queue<LeaseReleaseKind> _outcomes;
		private bool _manualRecovery;
		private bool _released;

		public SessionProbe(Address address, string value, IEnumerable<LeaseReleaseKind>? outcomes = null)
		{
			Address = address;
			Value = value;
			_outcomes = new Queue<LeaseReleaseKind>(outcomes ?? [LeaseReleaseKind.Released]);
			Session = ClientTestDouble.Create<IValueScanSession>(Handle);
		}

		public Address Address
		{
			get;
		}

		public int FirstCalls
		{
			get;
			private set;
		}

		public ValueScanFirstRequest? LastFirst
		{
			get;
			private set;
		}

		public ValueScanNextRequest? LastNext
		{
			get;
			private set;
		}

		public int NextCalls
		{
			get;
			private set;
		}

		public int ReadCalls
		{
			get;
			private set;
		}

		public int ReleaseCalls
		{
			get;
			private set;
		}

		public int ResetCalls
		{
			get;
			private set;
		}

		public IValueScanSession Session
		{
			get;
		}

		public ValueScanSessionState State
		{
			get;
			set;
		} = ValueScanSessionState.Created;

		public ValueScanInvalidationKind Invalidation
		{
			get;
			set;
		} = ValueScanInvalidationKind.None;

		public string Value
		{
			get;
		}

		private object? Handle(MethodInfo method, object?[]? arguments)
		{
			return method.Name switch
			{
				"get_State" => State,
				"get_Invalidation" => Invalidation,
				"FirstScan" => First((ValueScanFirstRequest) arguments![0]!),
				"NextScan" => Next((ValueScanNextRequest) arguments![0]!),
				"Reset" => Reset(),
				"GetResultCount" => 1UL,
				"Read" => Read(),
				"Release" => Release(),
				"get_IsReleased" => _released,
				"get_RequiresManualRecovery" => _manualRecovery,
				_ => throw new XunitException($"Unexpected session call: {method.Name}.")
			};
		}

		private object? First(ValueScanFirstRequest request)
		{
			Assert.Equal(ValueScanSessionState.Created, State);
			FirstCalls++;
			LastFirst = request;
			State = ValueScanSessionState.ResultsReady;
			return null;
		}

		private object? Next(ValueScanNextRequest request)
		{
			NextCalls++;
			LastNext = request;
			return null;
		}

		private object? Reset()
		{
			ResetCalls++;
			State = ValueScanSessionState.Created;
			return null;
		}

		private ValueScanPage Read()
		{
			ReadCalls++;
			return new ValueScanPage(0, 1, ImmutableArray.Create(new ValueScanMatch(Address, Value)));
		}

		private LeaseReleaseOutcome Release()
		{
			ReleaseCalls++;
			LeaseReleaseKind kind = _outcomes.Count > 0 ? _outcomes.Dequeue() : LeaseReleaseKind.Released;
			LeaseReleaseOutcome outcome = new(kind, CheatEngineHostEffect.Completed);
			_released = !outcome.IsRetryable;
			_manualRecovery = outcome.RequiresManualRecovery;
			return outcome;
		}
	}
}
