using System.Collections.Immutable;
using System.Reflection;

using CheatEngine.Client;
using CheatEngine.Client.Results;
using CheatEngine.Client.Scanning;
using CheatEngine.Mcp.Core.Contract;
using CheatEngine.Mcp.Core.Features;
using CheatEngine.Mcp.Tests.Support;
using CheatEngine.Mcp.Tools.Scan;
using CheatEngine.SDK.Engine.Values;

using Microsoft.Extensions.Options;

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
		Assert.Equal((1, 0, 1, 0), (alpha.NextCalls, beta.NextCalls, alpha.ReleaseCalls, beta.ReleaseCalls));
		Assert.Equal("beta", Assert.Single(tools.ListResults("beta", cancellationToken: Token).Results).Value);
		CheatEngineToolException missing =
			Assert.Throws<CheatEngineToolException>(() => tools.GetStatus("alpha", Token));
		Assert.Equal(ToolErrorKind.NotFound, missing.Error.Kind);
	}

	[Fact]
	public void IndependentSession_RetryableResetReportsPartialEffectAndRetainsTheResource()
	{
		SessionProbe probe = new(new Address(0x1111), "value",
			[LeaseReleaseKind.CleanupUnavailable, LeaseReleaseKind.Released]);
		TargetResources resources = new();
		using ScanTools tools = CreateTools([probe], resources);
		tools.First("alpha", value: "10", cancellationToken: Token);

		CheatEngineToolException failed = Assert.Throws<CheatEngineToolException>(() => tools.Reset("alpha", Token));

		Assert.Equal((ToolErrorKind.PartialEffect, ToolHostEffect.Unknown, true),
			(failed.Error.Kind, failed.Error.HostEffect, failed.Error.Retryable));
		Assert.Equal("alpha", tools.GetStatus("alpha", Token).ScannerName);
		Assert.Throws<CheatEngineToolException>(() => resources.EnsureCanChangeTarget(
			new TargetTransition(1, 2), Token));
		Assert.True(tools.Reset("alpha", Token).Removed);
		resources.EnsureCanChangeTarget(new TargetTransition(1, 2), Token);
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

	private static ScanTools CreateTools(IReadOnlyList<SessionProbe> probes, TargetResources? resources = null)
	{
		Queue<SessionProbe> available = new(probes);
		IValueScanner scanner = ClientTestDouble.Create<IValueScanner>((method, _) => method.Name switch
		{
			"CreateSession" when available.TryDequeue(out SessionProbe? session) => session.Session,
			_ => throw new XunitException($"Unexpected value-scanner call: {method.Name}.")
		});
		IOptions<McpExecutionOptions> options = Options.Create(new McpExecutionOptions());
		ICheatEngineClient client = ClientTestDouble.Client((nameof(ICheatEngineClient.ValueScans), scanner));
		ToolDispatch dispatch = new(client, new McpFeatureGate(Options.Create(new McpFeatureOptions())), options,
			new DispatchStatistics(options), TimeProvider.System, new RecordingLogger<ToolDispatch>());
		return new ScanTools(dispatch, resources ?? new TargetResources());
	}

	private sealed class SessionProbe
	{
		private readonly Queue<LeaseReleaseKind> _outcomes;
		private bool _released;
		private ValueScanSessionState _state = ValueScanSessionState.Created;

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

		public int NextCalls
		{
			get;
			private set;
		}

		public int ReleaseCalls
		{
			get;
			private set;
		}

		public IValueScanSession Session
		{
			get;
		}

		public string Value
		{
			get;
		}

		private object? Handle(MethodInfo method, object?[]? arguments)
		{
			return method.Name switch
			{
				"get_State" => _state,
				"FirstScan" => First(),
				"NextScan" => Next(),
				"GetResultCount" => 1UL,
				"Read" => new ValueScanPage(0, 1, ImmutableArray.Create(new ValueScanMatch(Address, Value))),
				"Release" => Release(),
				"get_IsReleased" => _released,
				"get_RequiresManualRecovery" => false,
				_ => throw new XunitException($"Unexpected session call: {method.Name}.")
			};
		}

		private object? First()
		{
			_state = ValueScanSessionState.ResultsReady;
			return null;
		}

		private object? Next()
		{
			NextCalls++;
			return null;
		}

		private LeaseReleaseOutcome Release()
		{
			ReleaseCalls++;
			LeaseReleaseKind kind = _outcomes.Count > 0 ? _outcomes.Dequeue() : LeaseReleaseKind.Released;
			LeaseReleaseOutcome outcome = new(kind, CheatEngineHostEffect.Completed);
			_released = !outcome.IsRetryable;
			return outcome;
		}
	}
}
