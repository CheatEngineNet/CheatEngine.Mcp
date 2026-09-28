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
	[InlineData(-1, 512, "maxBacktrack")]
	[InlineData(65537, 512, "maxBacktrack")]
	[InlineData(4096, 0, "maximumFields")]
	[InlineData(4096, 2049, "maximumFields")]
	public void GetObject_BoundsOutsideTheirRange_RefuseBeforeDispatch(int maxBacktrack, int maximumFields,
		string parameter)
	{
		StateTestHarness harness = new();

		ToolError error = Refusal(() => Tools(harness).GetObject("player", maxBacktrack, maximumFields, Token));

		Assert.Equal((ToolErrorKind.LimitExceeded, ToolHostEffect.NotStarted), (error.Kind, error.HostEffect));
		Assert.Equal(parameter, Parameter(error));
		Assert.Equal(0, harness.Dispatches);
	}

	[Theory]
	[InlineData(0, 1)]
	[InlineData(65536, 2048)]
	public void GetObject_TrimmedAddressAndBounds_ReachTheObjectBodyInOneDispatch(int maxBacktrack, int maximumFields)
	{
		StateTestHarness harness = new();
		MonoObject answer = new("5000", 92, "4096", "Player", "Game", "8192", 1,
			[new MonoObjectField("health", 92, false, TypeName: "System.Int32", ElementType: 8, Address: "505C",
				Value: "100")]);
		harness.Answer<MonoObject>(_ => answer);

		MonoObject found = Tools(harness).GetObject(" [player]+5C ", maxBacktrack, maximumFields, Token);

		Assert.Same(answer, found);
		(string operation, string source) = Assert.Single(harness.LuaCalls);
		Assert.Equal(CheatEngineToolNames.MonoGetObject, operation);
		Assert.Contains($"[1] = \"[player]+5C\", [2] = {maxBacktrack}, [3] = {maximumFields}", source,
			StringComparison.Ordinal);
		Assert.EndsWith(MonoLuaScripts.Object, source, StringComparison.Ordinal);
		Assert.Equal(1, harness.Dispatches);
	}

	[Fact]
	public void NameFilters_PastTheLimit_RefuseBeforeDispatch()
	{
		StateTestHarness harness = new();
		MonoTools tools = Tools(harness);
		string filter = new('n', 257);

		ToolError[] errors =
		[
			Refusal(() => tools.ListClasses("image", nameContains: filter, cancellationToken: Token)),
			Refusal(() => tools.ListFields("class", nameContains: filter, cancellationToken: Token)),
			Refusal(() => tools.ListMethods("class", nameContains: filter, cancellationToken: Token))
		];

		Assert.All(errors, static error =>
		{
			Assert.Equal((ToolErrorKind.InvalidArgument, ToolHostEffect.NotStarted), (error.Kind, error.HostEffect));
			Assert.Equal("nameContains", Parameter(error));
			Assert.Equal("nameContains: must be at most 256 characters.", error.Message);
		});
		Assert.Equal(0, harness.Dispatches);
	}

	[Theory]
	[InlineData(null, "nil")]
	[InlineData("", "nil")]
	[InlineData(" Player ", "\" Player \"")]
	public void NameFilters_ReachLuaUnchangedAndAnEmptyFilterAsNil(string? filter, string encoded)
	{
		StateTestHarness harness = new();
		harness.Answer<MonoClassPage>(static _ => new MonoClassPage("8192", [], 0));
		harness.Answer<MonoFieldList>(static _ => new MonoFieldList("4096", []));
		harness.Answer<MonoMethodPage>(static _ => new MonoMethodPage("4096", [], 0));
		MonoTools tools = Tools(harness);

		tools.ListClasses(" 8192 ", 5, 10, filter, Token);
		tools.ListFields(" 4096 ", true, 7, filter, Token);
		tools.ListMethods(" 4096 ", 5, 10, filter, Token);

		Assert.Equal(
		[
			(CheatEngineToolNames.MonoListClasses, $"[1] = \"8192\", [2] = 5, [3] = 10, [4] = {encoded} }}"),
			(CheatEngineToolNames.MonoListFields, $"[1] = \"4096\", [2] = true, [3] = 7, [4] = {encoded} }}"),
			(CheatEngineToolNames.MonoListMethods, $"[1] = \"4096\", [2] = 5, [3] = 10, [4] = {encoded} }}")
		], harness.LuaCalls.Select(static call => (call.Operation, Arguments(call.Source))));
	}

	[Fact]
	public void InvokeMethod_ArgumentLimits_RefuseBeforeDispatch()
	{
		StateTestHarness countHarness = new();
		MonoInvokeArgument[] tooMany = [.. Enumerable.Repeat(new MonoInvokeArgument(2, "1"), 65)];

		ToolError count = Refusal(() => Tools(countHarness).InvokeMethod("method", arguments: tooMany,
			cancellationToken: Token));

		Assert.Equal((ToolErrorKind.LimitExceeded, ToolHostEffect.NotStarted), (count.Kind, count.HostEffect));
		Assert.Equal("arguments", Parameter(count));
		Assert.Equal(0, countHarness.Dispatches);

		StateTestHarness lengthHarness = new();
		ToolError length = Refusal(() => Tools(lengthHarness).InvokeMethod("method",
			arguments: [new MonoInvokeArgument(6, new string('v', 4097))], cancellationToken: Token));

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
			arguments: [new MonoInvokeArgument(6, null!)], cancellationToken: Token));

		Assert.Equal((ToolErrorKind.InvalidArgument, ToolHostEffect.NotStarted),
			(nullValue.Kind, nullValue.HostEffect));
		Assert.Equal("arguments", Parameter(nullValue));
		Assert.Equal(0, nullValueHarness.Dispatches);
	}

	[Theory]
	[InlineData(8, "7")]
	[InlineData(14, "text")]
	[InlineData(-1, "0")]
	[InlineData(7, "0")]
	public void InvokeMethod_TypeCodeCheatEngineDoesNotWrite_RefusesBeforeDispatch(int type, string value)
	{
		StateTestHarness harness = new();

		ToolError error = Refusal(() => Tools(harness).InvokeMethod("method",
			arguments: [new MonoInvokeArgument(2, "1"), new MonoInvokeArgument(type, value)],
			cancellationToken: Token));

		Assert.Equal((ToolErrorKind.InvalidArgument, ToolHostEffect.NotStarted), (error.Kind, error.HostEffect));
		Assert.Equal("arguments", Parameter(error));
		Assert.Contains("entry 1", error.Message, StringComparison.Ordinal);
		Assert.Contains("2 (int32)", error.Message, StringComparison.Ordinal);
		Assert.Equal(0, harness.Dispatches);
	}

	[Theory]
	[InlineData(0, "256")]
	[InlineData(0, "maybe")]
	[InlineData(1, "65536")]
	[InlineData(2, "4294967296")]
	[InlineData(2, "-2147483649")]
	[InlineData(2, "1.5")]
	[InlineData(3, "18446744073709551616")]
	[InlineData(3, "0xZZ")]
	[InlineData(4, "1e39")]
	[InlineData(5, "NaN")]
	[InlineData(5, "Infinity")]
	[InlineData(12, " ")]
	public void InvokeMethod_ValueOutsideItsType_RefusesBeforeDispatch(int type, string value)
	{
		StateTestHarness harness = new();

		ToolError error = Refusal(() => Tools(harness).InvokeMethod("method",
			arguments: [new MonoInvokeArgument(type, value)], cancellationToken: Token));

		Assert.Equal((ToolErrorKind.InvalidArgument, ToolHostEffect.NotStarted), (error.Kind, error.HostEffect));
		Assert.Equal("arguments", Parameter(error));
		Assert.Contains("entry 0", error.Message, StringComparison.Ordinal);
		Assert.Equal(0, harness.Dispatches);
	}

	[Fact]
	public void InvokeMethod_TypedValues_ReachLuaAsCheatEngineWritesThem()
	{
		StateTestHarness harness = new();
		harness.Answer<MonoMethodInvocation>(static _ => new MonoMethodInvocation(true, "17"));

		MonoMethodInvocation invoked = Tools(harness).InvokeMethod("42", " 401000 ",
			[
				new MonoInvokeArgument(0, "True"), new MonoInvokeArgument(0, "-1"), new MonoInvokeArgument(1, "0xFFFF"),
				new MonoInvokeArgument(2, "-7"), new MonoInvokeArgument(3, "18446744073709551615"),
				new MonoInvokeArgument(3, "-2"), new MonoInvokeArgument(4, "1.5"),
				new MonoInvokeArgument(5, "-2.25e-3"), new MonoInvokeArgument(6, " text "),
				new MonoInvokeArgument(12, " player+10 ")
			], Token);

		Assert.Equal(new MonoMethodInvocation(true, "17"), invoked);
		(string operation, string source) = Assert.Single(harness.LuaCalls);
		Assert.Equal(CheatEngineToolNames.MonoInvokeMethod, operation);
		Assert.Contains(
			"[1] = \"42\", [2] = \"401000\", [3] = {{0,1,},{0,-1,},{1,65535,},{2,-7,},{3,0xFFFFFFFFFFFFFFFF,}," +
			"{3,0xFFFFFFFFFFFFFFFE,},{4,1.5,},{5,-0.00225,},{6,\" text \",},{12,\"player+10\",},}",
			source, StringComparison.Ordinal);
	}

	[Fact]
	public void PollInstanceSearch_LimitPastTheDescribedMaximum_IsRefused()
	{
		StateTestHarness harness = new();
		harness.Answer<MonoInstanceBatch>(static _ => new MonoInstanceBatch([], 0));
		MonoTools tools = Tools(harness);
		MonoInstanceSearch started = tools.StartInstanceSearch("class", cancellationToken: Token);

		ToolError error = Refusal(() => tools.PollInstanceSearch(started.JobId, 0, 1001));

		Assert.Equal(ToolErrorKind.InvalidArgument, error.Kind);
		Assert.Equal("limit: must be between 1 and 1000.", error.Message);
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
		string id = Assert.Single(harness.Resources.List()).Id;
		MonoDetachResult detached = tools.Detach(Token);

		Assert.True(attached.Attached);
		Assert.Equal(7, attached.CollectorVersion);
		Assert.Equal((true, false), (detached.Detached, detached.AlreadyEnded));
		Assert.Equal(
		[
			"collector_dll_remains_loaded", "mono_error_ok_patch_remains_until_target_restart",
			"uses_mono_table_option_remains"
		], detached.RemainingEffects);
		Assert.Equal([CheatEngineToolNames.MonoAttach, CheatEngineToolNames.MonoDetach],
			harness.LuaCalls.Select(static call => call.Operation));
		// The attach records its attachment under the resource id, and the detach closes only that attachment.
		Assert.All(harness.LuaCalls, call => Assert.Contains($"[1] = \"{id}\"", call.Source, StringComparison.Ordinal));
		Assert.StartsWith("mono-", id, StringComparison.Ordinal);
		Assert.Equal((2, 0), (harness.Dispatches, harness.Resources.Count));
	}

	[Fact]
	public void Detach_AttachmentThatEndedOutsideMcp_ReleasesOnlyTheResourceAndReportsIt()
	{
		StateTestHarness harness = new();
		harness.Answer<MonoAttachResult>(static _ => new MonoAttachResult(true, false, 7, ["collector_injected"]));
		harness.Answer<MonoDetachResult>(static _ => new MonoDetachResult(true, ["collector_dll_remains_loaded"],
			true));
		MonoTools tools = Tools(harness);
		tools.Attach(Token);

		MonoDetachResult detached = tools.Detach(Token);
		ToolError again = Refusal(() => tools.Detach(Token));

		Assert.Equal((true, true), (detached.Detached, detached.AlreadyEnded));
		Assert.Equal(
		[
			"collector_dll_remains_loaded", "mono_error_ok_patch_remains_until_target_restart",
			"uses_mono_table_option_remains"
		], detached.RemainingEffects);
		Assert.Equal(0, harness.Resources.Count);
		Assert.Equal(ToolErrorKind.NotFound, again.Kind);
		Assert.Equal(2, harness.Dispatches);
	}

	[Theory]
	[InlineData(false, ResourceReleaseKind.Released)]
	[InlineData(true, ResourceReleaseKind.ExternallyRemoved)]
	public void ReleaseAll_TracksWhetherTheCollectorAttachmentWasStillThisActivations(bool alreadyEnded,
		ResourceReleaseKind expected)
	{
		StateTestHarness harness = new();
		harness.Answer<MonoAttachResult>(static _ => new MonoAttachResult(true, false, 7, ["collector_injected"]));
		harness.Answer<MonoDetachResult>(_ => new MonoDetachResult(true, [], alreadyEnded));
		MonoTools tools = Tools(harness);
		tools.Attach(Token);

		ReleaseAllResult released = harness.Dispatch.Run(CheatEngineToolNames.RuntimeReleaseResources,
			token => harness.Resources.ReleaseAll(token), Token);

		ReleasedResource resource = Assert.Single(released.Released);
		Assert.Equal(("mono", expected, true), (resource.Resource.Kind, resource.Release.Kind,
			resource.Release.IsComplete));
		Assert.Null(released.Failed);
		Assert.Equal(ToolErrorKind.NotFound, Refusal(() => tools.Detach(Token)).Kind);
		MonoAttachResult reattached = tools.Attach(Token);
		Assert.True(reattached.Attached);
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

	// The encoded a table after its count, up to the end of its literal: "[1] = ... }".
	private static string Arguments(string source)
	{
		int start = source.IndexOf("[1] = ", StringComparison.Ordinal);
		int end = source.IndexOf(" };", start, StringComparison.Ordinal);
		return source[start..(end + 2)];
	}
}
