using CheatEngine.Mcp.Core.Contract;
using CheatEngine.Mcp.Core.Execution;
using CheatEngine.Mcp.Core.Jobs;
using CheatEngine.Mcp.Core.Targets;
using CheatEngine.Mcp.Tests.Tools.Memory;
using CheatEngine.Mcp.Tools.Code;
using CheatEngine.SDK.Engine.Runtime;

using Microsoft.Extensions.Options;

namespace CheatEngine.Mcp.Tests.Tools.Code;

/// <summary>
///     Admission checks for background code jobs: their address, selection epoch and pointer width are one target-bound
///     snapshot before either starter registers work.
/// </summary>
public sealed class CodeToolsJobStarterTests
{
	private static CancellationToken Token => TestContext.Current.CancellationToken;

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public void StartJob_TargetChangedDuringResolution_RefusesBeforeRegisteringOrOperating(bool dissect)
	{
		TargetDouble target = new();
		target.Epochs.Enqueue(1);
		target.Epochs.Enqueue(2);
		using JobRegistry jobs = Jobs(target);
		CodeTools tools = new(target.Dispatch, jobs);

		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() => Start(tools, dissect, "1000", 1));

		Assert.Equal((ToolErrorKind.TargetChanged, ToolHostEffect.NotApplied),
			(exception.Error.Kind, exception.Error.HostEffect));
		Assert.Equal(0, jobs.Count);
		Assert.Empty(target.CallsTo("Memory"));
		Assert.Equal(0, target.Mutations);
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public void StartJob_TargetChangedDuringPointerWidthRead_RefusesBeforeRegisteringOrOperating(bool dissect)
	{
		TargetDouble target = new();
		target.Epochs.Enqueue(1);
		target.Epochs.Enqueue(1);
		target.Epochs.Enqueue(2);
		using JobRegistry jobs = Jobs(target);
		CodeTools tools = new(target.Dispatch, jobs);

		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() => Start(tools, dissect, "1000", 1));

		Assert.Equal((ToolErrorKind.TargetChanged, ToolHostEffect.NotApplied),
			(exception.Error.Kind, exception.Error.HostEffect));
		Assert.Equal(0, jobs.Count);
		Assert.Empty(target.CallsTo("Memory"));
		Assert.Equal(0, target.Mutations);
	}

	[Theory]
	[InlineData(false, "FFFFFFFF", 4)]
	[InlineData(true, "FFFFFFFF", 4)]
	[InlineData(false, "FFFFFFFFFFFFFFFF", 8)]
	[InlineData(true, "FFFFFFFFFFFFFFFF", 8)]
	public void StartJob_RangeOutsideTargetAddressSpace_RefusesBeforeRegisteringOrOperating(bool dissect,
		string address, int pointerBytes)
	{
		TargetDouble target = new()
		{
			Bitness = pointerBytes == 4 ? PointerSize.Bit32 : PointerSize.Bit64
		};
		using JobRegistry jobs = Jobs(target);
		CodeTools tools = new(target.Dispatch, jobs);

		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() => Start(tools, dissect, address, 2));

		Assert.Equal((ToolErrorKind.InvalidArgument, ToolHostEffect.NotStarted),
			(exception.Error.Kind, exception.Error.HostEffect));
		Assert.Equal(0, jobs.Count);
		Assert.Empty(target.CallsTo("Memory"));
		Assert.Equal(0, target.Mutations);
	}

	private static JobRegistry Jobs(TargetDouble target)
	{
		IOptions<McpExecutionOptions> options = Options.Create(new McpExecutionOptions());
		return new JobRegistry(target.Dispatch, new TargetResources(), options, TimeProvider.System);
	}

	private static void Start(CodeTools tools, bool dissect, string address, int size)
	{
		if (dissect)
		{
			tools.StartDissect(address, size, cancellationToken: Token);
			return;
		}

		tools.StartSearch(address, size, "nop", cancellationToken: Token);
	}
}
