using CheatEngine.Client;
using CheatEngine.Client.Inspection;
using CheatEngine.Client.Results;
using CheatEngine.SDK.Engine.Inspection;
using CheatEngine.SDK.Engine.Values;

namespace CheatEngine.Mcp.Tools;

internal static class ToolExecution
{
	public static object Run(ICheatEngineClient client, Func<object> body)
	{
		try
		{
			return client.Dispatcher.Invoke(body, client.Stopping);
		}
		catch (CheatEngineClientException exception)
		{
			return Failure(exception.Failure);
		}
		catch (CheatEngineOperationCanceledException exception)
		{
			return Failure(exception.Failure);
		}
		catch (Exception exception)
		{
			return Error(exception.Message);
		}
	}

	public static object Error(string error) => new { success = false, error };

	public static object Failure(CheatEngineFailure failure) => new
	{
		success = false,
		error = failure.Message,
		kind = failure.Kind.ToString(),
		operation = failure.Operation,
		hostEffect = failure.HostEffect.ToString()
	};

	public static Address Address(ICheatEngineClient client, string expression)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(expression);
		return client.Inspection.ResolveAddress(new SymbolExpression(expression), AddressResolutionMode.Default);
	}
}
