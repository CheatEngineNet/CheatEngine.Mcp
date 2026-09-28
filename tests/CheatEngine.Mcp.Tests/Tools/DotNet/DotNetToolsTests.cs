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

	[Fact]
	public void NameFilters_PastTheLimit_RefuseBeforeDispatch()
	{
		StateTestHarness harness = new();
		DotNetTools tools = Tools(harness);
		string filter = new('n', 257);

		ToolError[] errors =
		[
			Refusal(() => tools.ListTypes("140", nameContains: filter, cancellationToken: Token)),
			Refusal(() => tools.ListMethods("140", "33554437", nameContains: filter, cancellationToken: Token))
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
	[InlineData("Game.Player", "\"Game.Player\"")]
	public void NameFilters_ReachLuaUnchangedAndAnEmptyFilterAsNil(string? filter, string encoded)
	{
		StateTestHarness harness = new();
		harness.Answer<DotNetTypePage>(static _ => new DotNetTypePage("140", [], 0));
		harness.Answer<DotNetMethodPage>(static _ => new DotNetMethodPage("140", "33554437", [], 0));
		DotNetTools tools = Tools(harness);

		tools.ListTypes(" 140 ", 5, 10, filter, Token);
		tools.ListMethods(" 140 ", " 33554437 ", 5, 10, filter, Token);

		Assert.Equal(
		[
			(CheatEngineToolNames.DotNetListTypes, $"[1] = \"140\", [2] = 5, [3] = 10, [4] = {encoded} }}"),
			(CheatEngineToolNames.DotNetListMethods,
				$"[1] = \"140\", [2] = \"33554437\", [3] = 5, [4] = 10, [5] = {encoded} }}")
		], harness.LuaCalls.Select(static call => (call.Operation, Arguments(call.Source))));
	}

	[Theory]
	[InlineData(" ", "6000001", "moduleHandle")]
	[InlineData("140", "", "methodToken")]
	[InlineData("140", "12345678901234567890123456789012345678901234567890123456789012345", "methodToken")]
	public void GetMethodParameters_InvalidHandle_RefusesBeforeDispatch(string moduleHandle, string methodToken,
		string parameter)
	{
		StateTestHarness harness = new();

		ToolError error = Refusal(() => Tools(harness).GetMethodParameters(moduleHandle, methodToken, Token));

		Assert.Equal((ToolErrorKind.InvalidArgument, ToolHostEffect.NotStarted), (error.Kind, error.HostEffect));
		Assert.Equal(parameter, Parameter(error));
		Assert.Equal(0, harness.Dispatches);
	}

	[Fact]
	public void GetMethodParameters_CollectorCodes_AreNamedAfterOneFixedLuaDispatch()
	{
		StateTestHarness harness = new();
		harness.Answer<DotNetMethodParameters>(static _ => new DotNetMethodParameters(" 140 ", "100663297",
		[
			new DotNetParameter(0, "speed", 0x0C), new DotNetParameter(1, "target", 0x12),
			new DotNetParameter(2, "unnamed", 0)
		], "System.Void (System.Single, Game.Player, ?)"));

		DotNetMethodParameters parameters = Tools(harness).GetMethodParameters(" 140 ", " 100663297 ", Token);

		Assert.Equal(
		[
			new DotNetParameter(0, "speed", 0x0C, "Single"), new DotNetParameter(1, "target", 0x12, "Class"),
			new DotNetParameter(2, "unnamed", 0)
		], parameters.Parameters);
		Assert.Equal("System.Void (System.Single, Game.Player, ?)", parameters.Signature);
		(string operation, string source) = Assert.Single(harness.LuaCalls);
		Assert.Equal(CheatEngineToolNames.DotNetGetMethodParameters, operation);
		Assert.Contains("[1] = \"140\", [2] = \"100663297\"", source, StringComparison.Ordinal);
		Assert.Equal(1, harness.Dispatches);
	}

	[Theory]
	[InlineData(0x02, "Boolean")]
	[InlineData(0x08, "Int32")]
	[InlineData(0x0E, "String")]
	[InlineData(0x11, "ValueType")]
	[InlineData(0x1C, "Object")]
	[InlineData(0x1D, "SZArray")]
	[InlineData(0x17, null)]
	[InlineData(0x55, null)]
	public void ElementTypeName_EcmaCodes_UseTheirCorElementTypeNames(int elementType, string? expected)
	{
		Assert.Equal(expected, DotNetTools.ElementTypeName(elementType));
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

	[Fact]
	public void PollInstanceSearch_LimitPastTheDescribedMaximum_IsRefused()
	{
		StateTestHarness harness = new();
		harness.Answer<DotNetInstanceBatch>(static _ => new DotNetInstanceBatch([], 0));
		DotNetTools tools = Tools(harness);
		DotNetInstanceSearch started = tools.StartInstanceSearch("module", "token", cancellationToken: Token);

		ToolError error = Refusal(() => tools.PollInstanceSearch(started.JobId, 0, 1001));

		Assert.Equal(ToolErrorKind.InvalidArgument, error.Kind);
		Assert.Equal("limit: must be between 1 and 1000.", error.Message);
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

	// The encoded a table after its count, up to the end of its literal: "[1] = ... }".
	private static string Arguments(string source)
	{
		int start = source.IndexOf("[1] = ", StringComparison.Ordinal);
		int end = source.IndexOf(" };", start, StringComparison.Ordinal);
		return source[start..(end + 2)];
	}
}
