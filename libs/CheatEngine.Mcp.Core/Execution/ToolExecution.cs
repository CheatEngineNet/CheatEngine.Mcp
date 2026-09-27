using System.ComponentModel;

using CheatEngine.Client;
using CheatEngine.Client.Inspection;
using CheatEngine.Client.Results;
using CheatEngine.Mcp.Core.Composition;
using CheatEngine.Mcp.Core.Contract;
using CheatEngine.SDK.Engine.Inspection;
using CheatEngine.SDK.Engine.Values;

namespace CheatEngine.Mcp.Core.Execution;

/// <summary>Runs one compound Cheat Engine operation through Client dispatch and maps its failures to the contract.</summary>
public static class ToolExecution
{
	internal const string NotAdmittedMessage =
		"Cheat Engine MCP could not dispatch the operation; nothing was run.";

	/// <summary>
	///     Dispatches the whole body to Cheat Engine's main thread, linking the MCP request's token with the activation's
	///     stopping token, and maps every failure to <see cref="CheatEngineToolException" />.
	/// </summary>
	/// <remarks>
	///     <para>
	///         The linked token is observed before dispatch admission and passed to the body. A body that has applied its
	///         first
	///         mutation must pass <see cref="ICheatEngineClient.Stopping" /> to later Client calls, so a caller's cancellation
	///         can no longer split a composite effect, and must report a composite failure with
	///         <see cref="CheatEngineToolException.PartialEffect{TDetails}" />.
	///     </para>
	///     <para>
	///         A <see cref="CheatEngineToolException" /> passes unchanged. A Client failure keeps its kind, operation and host
	///         effect (<see cref="ToolFailureMapping" />). A caller's cancellation is rethrown as
	///         <see cref="OperationCanceledException" />, which the MCP SDK never answers. Any other exception raised
	///         <b>inside</b> the body becomes <c>internal</c> with <c>unknown</c>, because the body may already have touched
	///         Cheat Engine; only an exception raised before the body started may report <c>not_started</c>.
	///     </para>
	/// </remarks>
	/// <typeparam name="T">The body's result.</typeparam>
	/// <param name="client">The activation's Client.</param>
	/// <param name="body">The complete operation, including target inspection and use; it receives the linked token.</param>
	/// <param name="cancellationToken">The MCP request's token.</param>
	/// <returns>The body's result.</returns>
	/// <exception cref="CheatEngineToolException">The operation failed; the error says what it did to the host.</exception>
	/// <exception cref="OperationCanceledException">The caller cancelled the request.</exception>
	public static T Run<T>(ICheatEngineClient client, Func<CancellationToken, T> body,
		CancellationToken cancellationToken)
	{
		return Run(client, body, new DispatchProbe(null), cancellationToken);
	}

	/// <summary>Transition only: dispatches a legacy tool body and shapes <c>{ success, ... }</c> results in band.</summary>
	/// <remarks>
	///     Kept for the legacy tools until they migrate to <see cref="ToolDispatch" />; new tools
	///     never use it.
	/// </remarks>
	/// <param name="client">The activation's Client.</param>
	/// <param name="body">The complete operation, including target inspection and use.</param>
	/// <returns>The body's result, or a failure result with kind, operation and host effect.</returns>
	[EditorBrowsable(EditorBrowsableState.Never)]
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

	/// <summary>Transition only: creates a legacy unsuccessful result with a caller-facing error message.</summary>
	/// <param name="error">The error message.</param>
	/// <returns>An object with <c>success = false</c> and <c>error</c>.</returns>
	[EditorBrowsable(EditorBrowsableState.Never)]
	public static object Error(string error)
	{
		return new { success = false, error };
	}

	/// <summary>Transition only: creates a legacy unsuccessful result that keeps the Client failure classification.</summary>
	/// <param name="failure">The Client failure.</param>
	/// <returns>An object with <c>success = false</c>, <c>error</c>, <c>kind</c>, <c>operation</c> and <c>hostEffect</c>.</returns>
	[EditorBrowsable(EditorBrowsableState.Never)]
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

	/// <summary>
	///     The implementation of <see cref="Run{T}(ICheatEngineClient, Func{CancellationToken, T}, CancellationToken)" />
	///     .
	/// </summary>
	/// <param name="client">The activation's Client.</param>
	/// <param name="body">The operation.</param>
	/// <param name="probe">Records whether and when the body ran on Cheat Engine's main thread.</param>
	/// <param name="cancellationToken">The MCP request's token.</param>
	internal static T Run<T>(ICheatEngineClient client, Func<CancellationToken, T> body, DispatchProbe probe,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(client);
		ArgumentNullException.ThrowIfNull(body);
		ArgumentNullException.ThrowIfNull(probe);
		CancellationToken stopping = client.Stopping;
		using CancellationTokenSource? linked = cancellationToken.CanBeCanceled && stopping.CanBeCanceled
			? CancellationTokenSource.CreateLinkedTokenSource(stopping, cancellationToken)
			: null;
		CancellationToken token = linked?.Token ?? (cancellationToken.CanBeCanceled ? cancellationToken : stopping);
		try
		{
			return client.Dispatcher.Invoke(() =>
			{
				probe.Enter();
				try
				{
					return body(token);
				}
				finally
				{
					probe.Exit();
				}
			}, token);
		}
		catch (CheatEngineToolException)
		{
			throw;
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested &&
		                                         !stopping.IsCancellationRequested)
		{
			// The SDK never answers a cancelled request; the dispatch facade logs one that had started.
			throw;
		}
		catch (CheatEngineOperationCanceledException exception)
		{
			throw CheatEngineToolException.FromFailure(exception.Failure, stopping.IsCancellationRequested);
		}
		catch (CheatEngineClientException exception)
		{
			throw CheatEngineToolException.FromFailure(exception.Failure, stopping.IsCancellationRequested);
		}
		catch (OperationCanceledException exception) when (stopping.IsCancellationRequested)
		{
			ToolHostEffect effect = probe.Entered ? ToolHostEffect.Unknown : ToolHostEffect.NotStarted;
			throw new CheatEngineToolException(new ToolError(ToolErrorKind.Stopping,
					"The plugin activation is stopping.", null, effect, false, ToolFailureMapping.ActivationEndedHint),
				exception);
		}
		catch (Exception exception) when (probe.Entered)
		{
			throw CheatEngineToolException.Internal(CheatEngineToolFilters.UnexpectedMessage, exception);
		}
		catch (Exception exception)
		{
			throw new CheatEngineToolException(new ToolError(ToolErrorKind.Internal, NotAdmittedMessage, null,
				ToolHostEffect.NotStarted, false, CheatEngineToolException.InternalHint), exception);
		}
	}
}
