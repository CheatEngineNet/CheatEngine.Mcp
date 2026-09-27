using CheatEngine.Client;
using CheatEngine.Client.Dispatching;
using CheatEngine.Client.Results;
using CheatEngine.Mcp.Core.Contract;
using CheatEngine.Mcp.Tests.Support;

namespace CheatEngine.Mcp.Tests.Core;

public sealed class ToolExecutionTests
{
	[Fact]
	public void RunT_Body_ReceivesTheLinkedTokenAndReturnsItsResult()
	{
		using CancellationTokenSource request = new();
		using CancellationTokenSource stopping = new();
		RecordingDispatcher dispatcher = new();
		ICheatEngineClient client = ClientTestDouble.Client(dispatcher.Dispatcher, stopping.Token);
		bool cancelledByStopping = false;

		string result = ToolExecution.Run(client, token =>
		{
			stopping.Cancel();
			cancelledByStopping = token.IsCancellationRequested;
			return "done";
		}, request.Token);

		Assert.Equal("done", result);
		Assert.Equal(1, dispatcher.Calls);
		Assert.True(cancelledByStopping);
		Assert.False(request.IsCancellationRequested);
	}

	[Fact]
	public void RunT_NonContractExceptionInsideTheBody_IsInternalUnknown()
	{
		InvalidOperationException fault = new("secret detail");

		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() =>
			ToolExecution.Run<int>(ClientTestDouble.Client(), _ => throw fault, CancellationToken.None));

		Assert.Equal(ToolErrorKind.Internal, exception.Error.Kind);
		Assert.Equal(ToolHostEffect.Unknown, exception.Error.HostEffect);
		Assert.Equal(CheatEngineToolFilters.UnexpectedMessage, exception.Error.Message);
		Assert.Same(fault, exception.InnerException);
	}

	[Fact]
	public void RunT_ClientFailureBeforeAdmission_KeepsNotStarted()
	{
		RecordingDispatcher dispatcher = new()
		{
			Admission = static token => new CheatEngineFailure(CheatEngineFailureKind.InvalidState, "Dispatcher.Invoke",
				"The activation is stopping.", hostEffect: CheatEngineHostEffect.NotStarted).Throw(token)
		};
		bool ran = false;

		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() =>
			ToolExecution.Run(ClientTestDouble.Client(dispatcher.Dispatcher, CancellationToken.None),
				_ => ran = true, CancellationToken.None));

		Assert.False(ran);
		Assert.Equal(ToolErrorKind.InvalidState, exception.Error.Kind);
		Assert.Equal(ToolHostEffect.NotStarted, exception.Error.HostEffect);
	}

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
		CheatEngineFailure failure = new(CheatEngineFailureKind.InvalidState,
			"Memory.Write", "Target changed", hostEffect: CheatEngineHostEffect.NotStarted);
		object result = ToolExecution.Run(ClientTestDouble.Client(), () => throw failure.ToException());
		ToolResultAssert.IsFailure(result, "Target changed");
		ToolResultAssert.HasPropertyValue(result, "kind", "InvalidState");
		ToolResultAssert.HasPropertyValue(result, "hostEffect", "NotStarted");
	}

	[Fact]
	public void Run_StoppingActivation_PassesCancellationAndDoesNotExecuteBody()
	{
		using CancellationTokenSource stopping = new();
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
		object result = ToolExecution.Run(client, () =>
		{
			called = true;
			return new
			{
				success = true
			};
		});
		Assert.False(called);
		ToolResultAssert.HasPropertyValue(result, "success", false);
	}
}
