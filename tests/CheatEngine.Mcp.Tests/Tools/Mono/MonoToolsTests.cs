using CheatEngine.Mcp.Core.Contract;
using CheatEngine.Mcp.Core.Jobs;
using CheatEngine.Mcp.Tests.Support;
using CheatEngine.Mcp.Tools.Mono;

namespace CheatEngine.Mcp.Tests.Tools.Mono;

/// <summary>Validation, capability gates, job ownership and cleanup for the Mono collector tools.</summary>
public sealed class MonoToolsTests
{
	private static readonly TimeSpan Wait = TimeSpan.FromSeconds(10);
	private static CancellationToken Token => TestContext.Current.CancellationToken;

	[Fact]
	public void ListClasses_EmptyImageHandle_RefusesBeforeDispatch()
	{
		StateTestHarness harness = new();

		ToolError error = Refusal(() => Tools(harness).ListClasses(" ", cancellationToken: Token));

		Assert.Equal((ToolErrorKind.InvalidArgument, ToolHostEffect.NotStarted), (error.Kind, error.HostEffect));
		Assert.Equal("imageHandle", Parameter(error));
		Assert.Equal(0, harness.Dispatches);
	}

	[Theory]
	[InlineData(0)]
	[InlineData(2049)]
	public void ListFields_FieldLimitOutsideBounds_RefusesBeforeDispatch(int maximumFields)
	{
		StateTestHarness harness = new();

		ToolError error = Refusal(() => Tools(harness).ListFields("class", maximumFields: maximumFields,
			cancellationToken: Token));

		Assert.Equal((ToolErrorKind.LimitExceeded, ToolHostEffect.NotStarted), (error.Kind, error.HostEffect));
		Assert.Equal("maximumFields", Parameter(error));
		Assert.Equal(0, harness.Dispatches);
	}

	[Fact]
	public void FindClass_EmptyClassName_RefusesBeforeDispatch()
	{
		StateTestHarness harness = new();

		ToolError error = Refusal(() => Tools(harness).FindClass(null, " ", Token));

		Assert.Equal((ToolErrorKind.InvalidArgument, ToolHostEffect.NotStarted), (error.Kind, error.HostEffect));
		Assert.Equal("className", Parameter(error));
		Assert.Equal(0, harness.Dispatches);
	}

	[Fact]
	public void InvokeMethod_ArgumentLimits_RefuseBeforeDispatch()
	{
		StateTestHarness countHarness = new();
		MonoInvokeArgument[] tooMany = [.. Enumerable.Repeat(new MonoInvokeArgument(8, "1"), 65)];

		ToolError count = Refusal(() => Tools(countHarness).InvokeMethod("method", arguments: tooMany,
			cancellationToken: Token));

		Assert.Equal((ToolErrorKind.LimitExceeded, ToolHostEffect.NotStarted), (count.Kind, count.HostEffect));
		Assert.Equal("arguments", Parameter(count));
		Assert.Equal(0, countHarness.Dispatches);

		StateTestHarness lengthHarness = new();
		ToolError length = Refusal(() => Tools(lengthHarness).InvokeMethod("method",
			arguments: [new MonoInvokeArgument(8, new string('v', 4097))], cancellationToken: Token));

		Assert.Equal((ToolErrorKind.LimitExceeded, ToolHostEffect.NotStarted), (length.Kind, length.HostEffect));
		Assert.Equal("arguments", Parameter(length));
		Assert.Equal(0, lengthHarness.Dispatches);
	}

	[Fact]
	public void InvokeMethod_NullArgumentShape_RefusesBeforeDispatch()
	{
		StateTestHarness nullElementHarness = new();
		ToolError nullElement = Refusal(() => Tools(nullElementHarness).InvokeMethod("method",
			arguments: [null!], cancellationToken: Token));

		Assert.Equal((ToolErrorKind.InvalidArgument, ToolHostEffect.NotStarted),
			(nullElement.Kind, nullElement.HostEffect));
		Assert.Equal("arguments", Parameter(nullElement));
		Assert.Equal(0, nullElementHarness.Dispatches);

		StateTestHarness nullValueHarness = new();
		ToolError nullValue = Refusal(() => Tools(nullValueHarness).InvokeMethod("method",
			arguments: [new MonoInvokeArgument(8, null!)], cancellationToken: Token));

		Assert.Equal((ToolErrorKind.InvalidArgument, ToolHostEffect.NotStarted),
			(nullValue.Kind, nullValue.HostEffect));
		Assert.Equal("arguments", Parameter(nullValue));
		Assert.Equal(0, nullValueHarness.Dispatches);
	}

	[Theory]
	[InlineData(0)]
	[InlineData(2049)]
	public void StartInstanceSearch_ResultLimitOutsideBounds_RefusesBeforeCreatingAJob(int maximumResults)
	{
		StateTestHarness harness = new();

		ToolError error = Refusal(() => Tools(harness).StartInstanceSearch("class", maximumResults,
			cancellationToken: Token));

		Assert.Equal((ToolErrorKind.LimitExceeded, ToolHostEffect.NotStarted), (error.Kind, error.HostEffect));
		Assert.Equal("maximumResults", Parameter(error));
		Assert.Equal((0, 0), (harness.Dispatches, harness.Jobs.Count));
	}

	[Theory]
	[InlineData(CheatEngineToolNames.MonoAttach, "{}")]
	[InlineData(CheatEngineToolNames.MonoCompileMethod, "{\"methodHandle\":2}")]
	[InlineData(CheatEngineToolNames.MonoInvokeMethod, "{\"methodHandle\":2}")]
	public async Task TargetCodeExecutionDisabled_RefusesEachGatedToolBeforeBindingOrDispatch(string tool,
		string arguments)
	{
		RecordingDispatcher dispatcher = new();
		using TestActivation activation = new(ClientTestDouble.Client(dispatcher.Dispatcher, CancellationToken.None),
			new Dictionary<string, string?> { ["Mcp:EnableTargetCodeExecution"] = "false" });
		await using TestMcpPipeline pipeline = await TestMcpPipeline.StartAsync(activation.Manifest,
			McpPrimitiveBinding.FromTargets(activation.Targets));

		ToolError error = TestMcpPipeline.AssertError(await pipeline.CallAsync(tool, arguments),
			ToolErrorKind.CapabilityDisabled);

		Assert.Equal((ToolHostEffect.NotStarted, false), (error.HostEffect, error.Retryable));
		Assert.Equal($"{tool} is disabled by the Mcp:EnableTargetCodeExecution setting.", error.Message);
		Assert.Equal(0, dispatcher.Calls);
	}

	[Fact]
	public async Task InstanceSearch_StartPollAndStop_RetainsTheBoundedResultThenDiscardsIt()
	{
		StateTestHarness harness = new();
		harness.Answer<MonoInstanceBatch>(static _ => new MonoInstanceBatch(
			[new MonoInstance("3000"), new MonoInstance("4000")], 3));
		MonoTools tools = Tools(harness);

		MonoInstanceSearch started = tools.StartInstanceSearch("class", 2, cancellationToken: Token);
		ManagedJob<MonoInstance> job = harness.Jobs.Get<ManagedJob<MonoInstance>>(started.JobId, "monoinstances");
		await job.Completion.WaitAsync(Wait, Token);
		MonoInstanceSearchPage page = tools.PollInstanceSearch(started.JobId, 0, 2);

		Assert.StartsWith($"monoinstances-{harness.Jobs.Namespace}-", started.JobId, StringComparison.Ordinal);
		Assert.Equal(("class", 2), (started.ClassHandle, started.MaximumResults));
		Assert.Equal(started.JobId, page.Job.JobId);
		Assert.Equal([new MonoInstance("3000"), new MonoInstance("4000")], page.Instances);
		Assert.Equal((1L, 2L, false, 0L), (page.FirstSequence, page.NextAfterSequence, page.More, page.Dropped));
		Assert.Equal([CheatEngineToolNames.MonoStartInstanceSearch],
			harness.LuaCalls.Select(static call => call.Operation));

		JobStopResult stopped = harness.Jobs.Stop(started.JobId, Token);
		ToolError afterStop = Refusal(() => tools.PollInstanceSearch(started.JobId));

		Assert.True(stopped.AlreadyReleased);
		Assert.Equal(ToolErrorKind.NotFound, afterStop.Kind);
		Assert.Equal((0, 0), (harness.Jobs.Count, harness.Resources.Count));
	}

	[Fact]
	public void AttachThenDetach_ReleasesTheActivationOwnedCollectorResource()
	{
		StateTestHarness harness = new();
		harness.Answer<MonoAttachResult>(static _ => new MonoAttachResult(true, false, 7,
			["collector_injected"]));
		harness.Answer<MonoDetachResult>(static _ => new MonoDetachResult(true,
			["collector_dll_remains_loaded"]));
		MonoTools tools = Tools(harness);

		MonoAttachResult attached = tools.Attach(Token);
		MonoDetachResult detached = tools.Detach(Token);

		Assert.True(attached.Attached);
		Assert.Equal(7, attached.CollectorVersion);
		Assert.True(detached.Detached);
		Assert.Equal(["collector_dll_remains_loaded", "mono_error_ok_patch_remains_until_target_restart"],
			detached.RemainingEffects);
		Assert.Equal([CheatEngineToolNames.MonoAttach, CheatEngineToolNames.MonoDetach],
			harness.LuaCalls.Select(static call => call.Operation));
		Assert.Equal((2, 0), (harness.Dispatches, harness.Resources.Count));
	}

	[Fact]
	public void Detach_WithoutActivationOwnedAttachment_RefusesBeforeDispatch()
	{
		StateTestHarness harness = new();

		ToolError error = Refusal(() => Tools(harness).Detach(Token));

		Assert.Equal((ToolErrorKind.NotFound, ToolHostEffect.NotStarted), (error.Kind, error.HostEffect));
		Assert.Equal(0, harness.Dispatches);
		Assert.Equal(0, harness.Resources.Count);
	}

	private static MonoTools Tools(StateTestHarness harness)
	{
		return new MonoTools(harness.Dispatch, harness.Jobs, harness.Resources);
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
