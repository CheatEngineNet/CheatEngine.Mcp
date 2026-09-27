using CheatEngine.Mcp.Core.Contract;
using CheatEngine.Mcp.Core.Jobs;
using CheatEngine.Mcp.Tests.Support;
using CheatEngine.Mcp.Tools.DotNet;

namespace CheatEngine.Mcp.Tests.Tools.DotNet;

/// <summary>Validation and retained-job behaviour for the read-only .NET collector tools.</summary>
public sealed class DotNetToolsTests
{
	private static readonly TimeSpan Wait = TimeSpan.FromSeconds(10);
	private static CancellationToken Token => TestContext.Current.CancellationToken;

	[Fact]
	public void ListModules_EmptyDomainHandle_RefusesBeforeDispatch()
	{
		StateTestHarness harness = new();

		ToolError error = Refusal(() => Tools(harness).ListModules(" ", cancellationToken: Token));

		Assert.Equal((ToolErrorKind.InvalidArgument, ToolHostEffect.NotStarted), (error.Kind, error.HostEffect));
		Assert.Equal("domainHandle", Parameter(error));
		Assert.Equal(0, harness.Dispatches);
	}

	[Theory]
	[InlineData(-1, 100, "offset")]
	[InlineData(0, 0, "limit")]
	[InlineData(0, 1001, "limit")]
	public void ListDomains_InvalidPage_RefusesBeforeDispatch(int offset, int limit, string parameter)
	{
		StateTestHarness harness = new();

		ToolError error = Refusal(() => Tools(harness).ListDomains(offset, limit, Token));

		Assert.Equal(ToolHostEffect.NotStarted, error.HostEffect);
		Assert.Equal(parameter, Parameter(error));
		Assert.Equal(0, harness.Dispatches);
	}

	[Theory]
	[InlineData(0)]
	[InlineData(2049)]
	public void GetType_FieldLimitOutsideBounds_RefusesBeforeDispatch(int maximumFields)
	{
		StateTestHarness harness = new();

		ToolError error = Refusal(() => Tools(harness).GetType("module", "token", maximumFields, Token));

		Assert.Equal((ToolErrorKind.LimitExceeded, ToolHostEffect.NotStarted), (error.Kind, error.HostEffect));
		Assert.Equal("maximumFields", Parameter(error));
		Assert.Equal(0, harness.Dispatches);
	}

	[Theory]
	[InlineData("")]
	[InlineData(" ")]
	public void GetObject_EmptyAddress_RefusesBeforeDispatch(string address)
	{
		StateTestHarness harness = new();

		ToolError error = Refusal(() => Tools(harness).GetObject(address, cancellationToken: Token));

		Assert.Equal((ToolErrorKind.InvalidArgument, ToolHostEffect.NotStarted), (error.Kind, error.HostEffect));
		Assert.Equal("address", Parameter(error));
		Assert.Equal(0, harness.Dispatches);
	}

	[Fact]
	public void GetObject_AddressPastTheLimit_RefusesBeforeDispatch()
	{
		StateTestHarness harness = new();

		ToolError error = Refusal(() => Tools(harness).GetObject(new string('A', 513), cancellationToken: Token));

		Assert.Equal((ToolErrorKind.LimitExceeded, ToolHostEffect.NotStarted), (error.Kind, error.HostEffect));
		Assert.Equal("address", Parameter(error));
		Assert.Equal(0, harness.Dispatches);
	}

	[Theory]
	[InlineData(0)]
	[InlineData(2049)]
	public void StartInstanceSearch_ResultLimitOutsideBounds_RefusesBeforeCreatingAJob(int maximumResults)
	{
		StateTestHarness harness = new();

		ToolError error = Refusal(() => Tools(harness).StartInstanceSearch("module", "token", maximumResults,
			cancellationToken: Token));

		Assert.Equal((ToolErrorKind.LimitExceeded, ToolHostEffect.NotStarted), (error.Kind, error.HostEffect));
		Assert.Equal("maximumResults", Parameter(error));
		Assert.Equal((0, 0), (harness.Dispatches, harness.Jobs.Count));
	}

	[Fact]
	public async Task InstanceSearch_StartPollAndStop_RetainsTheBoundedResultThenDiscardsIt()
	{
		StateTestHarness harness = new();
		harness.Answer<DotNetInstanceBatch>(static _ => new DotNetInstanceBatch(
			[new DotNetInstance("1000"), new DotNetInstance("2000")], 4));
		DotNetTools tools = Tools(harness);

		DotNetInstanceSearch started = tools.StartInstanceSearch("module", "token", 2, cancellationToken: Token);
		ManagedJob<DotNetInstance> job = harness.Jobs.Get<ManagedJob<DotNetInstance>>(started.JobId,
			"dotnetinstances");
		await job.Completion.WaitAsync(Wait, Token);
		DotNetInstanceSearchPage page = tools.PollInstanceSearch(started.JobId, 0, 2);

		Assert.StartsWith($"dotnetinstances-{harness.Jobs.Namespace}-", started.JobId, StringComparison.Ordinal);
		Assert.Equal(("module", "token", 2), (started.ModuleHandle, started.TypeToken, started.MaximumResults));
		Assert.Equal(started.JobId, page.Job.JobId);
		Assert.Equal([new DotNetInstance("1000"), new DotNetInstance("2000")], page.Instances);
		Assert.Equal((1L, 2L, false, 0L), (page.FirstSequence, page.NextAfterSequence, page.More, page.Dropped));
		Assert.Equal([CheatEngineToolNames.DotNetStartInstanceSearch],
			harness.LuaCalls.Select(static call => call.Operation));

		JobStopResult stopped = harness.Jobs.Stop(started.JobId, Token);
		ToolError afterStop = Refusal(() => tools.PollInstanceSearch(started.JobId));

		Assert.True(stopped.AlreadyReleased);
		Assert.Equal(ToolErrorKind.NotFound, afterStop.Kind);
		Assert.Equal((0, 0), (harness.Jobs.Count, harness.Resources.Count));
	}

	private static DotNetTools Tools(StateTestHarness harness)
	{
		return new DotNetTools(harness.Dispatch, harness.Jobs);
	}

	private static ToolError Refusal(Action action)
	{
		return Assert.Throws<CheatEngineToolException>(action).Error;
	}

	private static string? Parameter(ToolError error)
	{
		return error.Details!.Value.GetProperty("parameter").GetString();
	}
}
