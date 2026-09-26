using CheatEngine.Client;
using CheatEngine.Client.Dispatching;
using CheatEngine.Client.Results;
using CheatEngine.Mcp.Tools;

namespace CheatEngine.Mcp.Tests;

public sealed class ToolExecutionTests
{
	[Fact]
	public void Run_ClientDispatcher_ExecutesBodyOnce()
	{
		int calls = 0;
		object result = ToolExecution.Run(ClientTestDouble.Client(), () => new { success = true, count = ++calls });
		ToolResultAssert.IsSuccess(result);
		Assert.Equal(1, calls);
	}

	[Fact]
	public void Run_ClientFailure_PreservesClassificationAndHostEffect()
	{
		CheatEngineFailure failure = new CheatEngineFailure(CheatEngineFailureKind.InvalidState,
			"Memory.Write", "Target changed", hostEffect: CheatEngineHostEffect.NotStarted);
		object result = ToolExecution.Run(ClientTestDouble.Client(), () => throw failure.ToException());
		ToolResultAssert.IsFailure(result, "Target changed");
		ToolResultAssert.HasPropertyValue(result, "kind", "InvalidState");
		ToolResultAssert.HasPropertyValue(result, "hostEffect", "NotStarted");
	}

	[Fact]
	public void Run_StoppingActivation_PassesCancellationAndDoesNotExecuteBody()
	{
		using CancellationTokenSource stopping = new CancellationTokenSource();
		stopping.Cancel();
		bool called = false;
		ICheatEngineDispatcher dispatcher = ClientTestDouble.Create<ICheatEngineDispatcher>((_, args) =>
		{
			CancellationToken token = Assert.IsAssignableFrom<CancellationToken>(args![1]);
			Assert.Equal(stopping.Token, token);
			token.ThrowIfCancellationRequested();
			return null;
		});
		ICheatEngineClient client = ClientTestDouble.Create<ICheatEngineClient>((method, _) => method.Name switch
		{
			"get_Dispatcher" => dispatcher,
			"get_Stopping" => stopping.Token,
			_ => throw new InvalidOperationException(method.Name)
		});
		object result = ToolExecution.Run(client, () => { called = true; return new { success = true }; });
		Assert.False(called);
		ToolResultAssert.HasPropertyValue(result, "success", false);
	}
}
