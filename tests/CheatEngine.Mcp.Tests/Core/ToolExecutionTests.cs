using CheatEngine.Client;
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
}
