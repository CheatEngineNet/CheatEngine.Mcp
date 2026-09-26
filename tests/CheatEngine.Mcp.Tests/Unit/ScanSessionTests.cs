using System.Collections.Immutable;

using CheatEngine.Client;
using CheatEngine.Client.Results;
using CheatEngine.Client.Scanning;
using CheatEngine.Mcp.Tools;
using CheatEngine.SDK.Engine.Values;

namespace CheatEngine.Mcp.Tests;

public sealed class ScanSessionTests
{
	[Fact]
	public void NamedSessions_IndependentResultsNarrowingAndReset_AffectOnlyAddressedSession()
	{
		SessionProbe alpha = new("alpha", new Address(0x1111), "alpha result");
		SessionProbe beta = new("beta", new Address(0x2222), "beta result");
		ScanTool tool = CreateTool([alpha, beta]);

		ToolResultAssert.IsSuccess(tool.MemoryScan("alpha", "int32", "10"));
		ToolResultAssert.IsSuccess(tool.MemoryScan("beta", "int32", "20"));
		object alphaResults = tool.GetMemoryScanResults("alpha");
		object betaResults = tool.GetMemoryScanResults("beta");

		Assert.Equal("alpha result", ResultValue(alphaResults));
		Assert.Equal("beta result", ResultValue(betaResults));

		ToolResultAssert.IsSuccess(tool.NextMemoryScan("alpha", "50"));
		ToolResultAssert.IsSuccess(tool.ResetMemoryScan("alpha"));

		Assert.Equal(1, alpha.NextCalls);
		Assert.Equal(0, beta.NextCalls);
		Assert.Equal(1, alpha.ReleaseCalls);
		Assert.Equal(0, beta.ReleaseCalls);
		ToolResultAssert.IsFailure(tool.GetMemoryScanResults("alpha"), "No scan exists with that scannerName.");
		Assert.Equal("beta result", ResultValue(tool.GetMemoryScanResults("beta")));
	}

	[Fact]
	public void NamedSessions_AtCapacityRefusesThirtyThirdAndReusesReleasedSlot()
	{
		List<SessionProbe> sessions = Enumerable.Range(0, 33)
			.Select(index => new SessionProbe($"scan-{index}", new Address((ulong) index + 1), index.ToString(System.Globalization.CultureInfo.InvariantCulture)))
			.ToList();
		ScanTool tool = CreateTool(sessions);

		for (int index = 0; index < 32; index++)
		{
			ToolResultAssert.IsSuccess(tool.MemoryScan($"scan-{index}", "int32", index.ToString(System.Globalization.CultureInfo.InvariantCulture)));
		}

		ToolResultAssert.IsFailure(tool.MemoryScan("scan-32", "int32", "32"),
			"At most 32 named scan sessions may be active.");
		ToolResultAssert.IsSuccess(tool.ResetMemoryScan("scan-0"));
		ToolResultAssert.IsSuccess(tool.MemoryScan("scan-32", "int32", "32"));

		Assert.Equal(1, sessions[0].ReleaseCalls);
		Assert.Equal(1, sessions[32].FirstCalls);
	}

	[Fact]
	public void ResetMemoryScan_RetryableRelease_RetainsSessionAndTargetChangeLeaseUntilSuccess()
	{
		SessionProbe alpha = new("alpha", new Address(0x1111), "alpha result",
			[LeaseReleaseKind.CleanupUnavailable, LeaseReleaseKind.Released]);
		TargetResources resources = new();
		ScanTool tool = CreateTool([alpha], resources);

		ToolResultAssert.IsSuccess(tool.MemoryScan("alpha", "int32", "10"));
		object failedReset = tool.ResetMemoryScan("alpha");

		ToolResultAssert.HasPropertyValue(failedReset, "success", false);
		ToolResultAssert.IsSuccess(tool.GetMemoryScanStatus("alpha"));
		Assert.NotNull(resources.PrepareForTargetChange());

		ToolResultAssert.IsSuccess(tool.ResetMemoryScan("alpha"));

		Assert.Equal(2, alpha.ReleaseCalls);
		ToolResultAssert.IsFailure(tool.GetMemoryScanStatus("alpha"), "No scan exists with that scannerName.");
		Assert.Null(resources.PrepareForTargetChange());
	}

	[Fact]
	public void Dispose_ReleasesEveryIndependentSessionWithoutLuaOrUi()
	{
		SessionProbe alpha = new("alpha", new Address(0x1111), "alpha result");
		SessionProbe beta = new("beta", new Address(0x2222), "beta result");
		ScanTool tool = CreateTool([alpha, beta]);

		ToolResultAssert.IsSuccess(tool.MemoryScan("alpha", "int32", "10"));
		ToolResultAssert.IsSuccess(tool.MemoryScan("beta", "int32", "20"));

		tool.Dispose();
		tool.Dispose();

		Assert.Equal(1, alpha.ReleaseCalls);
		Assert.Equal(1, beta.ReleaseCalls);
	}

	private static ScanTool CreateTool(IReadOnlyList<SessionProbe> sessions, TargetResources? resources = null)
	{
		Queue<SessionProbe> available = new(sessions);
		IValueScanner scanner = ClientTestDouble.Create<IValueScanner>((method, _) => method.Name switch
		{
			"CreateSession" when available.TryDequeue(out SessionProbe? session) => session.Session,
			_ => throw new Xunit.Sdk.XunitException($"Unexpected value-scanner call: {method.Name}.")
		});
		return new ScanTool(ClientTestDouble.Client((nameof(ICheatEngineClient.ValueScans), scanner)), resources);
	}

	private static string ResultValue(object result)
	{
		object[] results = ToolResultAssert.GetProperty<object[]>(result, "results");
		Assert.Single(results);
		return ToolResultAssert.GetProperty<string>(results[0], "value");
	}

	private sealed class SessionProbe
	{
		private readonly Queue<LeaseReleaseKind> _releaseOutcomes;
		private ValueScanSessionState _state = ValueScanSessionState.Created;

		public SessionProbe(string name, Address address, string result, IEnumerable<LeaseReleaseKind>? releaseOutcomes = null)
		{
			Name = name;
			Address = address;
			Result = result;
			_releaseOutcomes = new Queue<LeaseReleaseKind>(releaseOutcomes ?? [LeaseReleaseKind.Released]);
			Session = ClientTestDouble.Create<IValueScanSession>(Handle);
		}

		public Address Address
		{
			get;
		}
		public int FirstCalls
		{
			get; private set;
		}
		public string Name
		{
			get;
		}
		public int NextCalls
		{
			get; private set;
		}
		public int ReleaseCalls
		{
			get; private set;
		}
		public string Result
		{
			get;
		}
		public IValueScanSession Session
		{
			get;
		}

		private object? Handle(System.Reflection.MethodInfo method, object?[]? arguments) => method.Name switch
		{
			"get_State" => _state,
			"FirstScan" => FirstScan(),
			"NextScan" => NextScan(),
			"GetResultCount" => 1UL,
			"Read" => new ValueScanPage(0, 1, ImmutableArray.Create(new ValueScanMatch(Address, Result))),
			"Release" => Release(),
			_ => throw new Xunit.Sdk.XunitException($"Unexpected session call for {Name}: {method.Name}.")
		};

		private object? FirstScan()
		{
			FirstCalls++;
			_state = ValueScanSessionState.ResultsReady;
			return null;
		}

		private object? NextScan()
		{
			NextCalls++;
			return null;
		}

		private LeaseReleaseOutcome Release()
		{
			ReleaseCalls++;
			LeaseReleaseKind kind = _releaseOutcomes.Count > 0 ? _releaseOutcomes.Dequeue() : LeaseReleaseKind.Released;
			return new LeaseReleaseOutcome(kind, CheatEngineHostEffect.Completed);
		}
	}
}
