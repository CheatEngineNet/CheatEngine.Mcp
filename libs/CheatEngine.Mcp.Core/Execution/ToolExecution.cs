using CheatEngine.Client;
using CheatEngine.Client.Inspection;
using CheatEngine.Client.Results;
using CheatEngine.SDK.Engine.Inspection;
using CheatEngine.SDK.Engine.Values;

namespace CheatEngine.Mcp.Core.Execution;

/// <summary>Runs one compound Cheat Engine operation through Client dispatch and shapes its in-band result.</summary>
public static class ToolExecution
{
	/// <summary>Dispatches the whole body to Cheat Engine's main thread and maps Client failures to a result.</summary>
	/// <param name="client">The activation's Client.</param>
	/// <param name="body">The complete operation, including target inspection and use.</param>
	/// <returns>The body's result, or a failure result with kind, operation and host effect.</returns>
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

	/// <summary>Creates an unsuccessful result with a caller-facing error message.</summary>
	/// <param name="error">The error message.</param>
	/// <returns>An object with <c>success = false</c> and <c>error</c>.</returns>
	public static object Error(string error)
	{
		return new { success = false, error };
	}

	/// <summary>Creates an unsuccessful result that preserves the Client failure classification.</summary>
	/// <param name="failure">The Client failure.</param>
	/// <returns>An object with <c>success = false</c>, <c>error</c>, <c>kind</c>, <c>operation</c> and <c>hostEffect</c>.</returns>
	public static object Failure(CheatEngineFailure failure)
	{
		return new
		{
			success = false,
			error = failure.Message,
			kind = failure.Kind.ToString(),
			operation = failure.Operation,
			hostEffect = failure.HostEffect.ToString()
		};
	}

	/// <summary>Resolves a Cheat Engine address expression inside the current dispatch.</summary>
	/// <param name="client">The activation's Client.</param>
	/// <param name="expression">An address, symbol or Cheat Engine expression.</param>
	/// <returns>The resolved address.</returns>
	public static Address Address(ICheatEngineClient client, string expression)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(expression);
		return client.Inspection.ResolveAddress(new SymbolExpression(expression), AddressResolutionMode.Default);
	}
}
