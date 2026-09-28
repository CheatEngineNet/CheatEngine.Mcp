using System.Text.Json;

using CheatEngine.Client.Results;
using CheatEngine.Mcp.Core.Contract;
using CheatEngine.Mcp.Core.Features;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace CheatEngine.Mcp.Core.Composition;

/// <summary>The Core request filters every composition registers through <c>WithCheatEnginePrimitives</c>.</summary>
internal static partial class CheatEngineToolFilters
{
	internal const string UnexpectedMessage =
		"An unexpected error occurred in Cheat Engine MCP; the operation's outcome is unknown.";

	private const string LoggerCategory = "CheatEngine.Mcp.Core.Composition.CheatEngineToolFilters";

	// What a correlated log event records for an internal error that names no operation.
	private const string UnknownOperation = "unknown";

	/// <summary>
	///     The outermost call-tool filter: turns every failure into the <c>{"error":{...}}</c> envelope with
	///     <c>isError:true</c> and no structured content.
	/// </summary>
	/// <remarks>
	///     <para>
	///         A request whose own token was cancelled and <see cref="McpProtocolException" /> are rethrown: the SDK answers
	///         neither with a tool result. <see cref="CheatEngineToolException" /> keeps its error; Client exceptions keep
	///         their classification, operation and host effect.
	///     </para>
	///     <para>
	///         <see cref="ArgumentException" /> and <see cref="JsonException" /> map to <c>invalid_argument</c> with
	///         <c>not_started</c>. That is only sound because the SDK throws them while binding arguments, before the tool
	///         body runs: a tool body must never let them escape after it has touched Cheat Engine. Tool bodies run through
	///         the Core dispatch, which reports any non-contract exception raised after admission as <c>internal</c> with
	///         <c>unknown</c> instead.
	///     </para>
	///     <para>Anything else is logged and reported as <c>internal</c> with <c>unknown</c> and a generic message.</para>
	///     <para>
	///         Every <c>internal</c> error is correlated: its <c>details</c> carry a random <c>errorId</c>, which
	///         the log event of the failure (3002 for an unexpected exception, 3006 for an internal error the tool
	///         reported) records with the tool name and the exception's type name (<see cref="FailureType" />),
	///         never its message.
	///     </para>
	/// </remarks>
	/// <param name="next">The rest of the pipeline.</param>
	/// <returns>The wrapped handler.</returns>
	internal static McpRequestHandler<CallToolRequestParams, CallToolResult> MapErrors(
		McpRequestHandler<CallToolRequestParams, CallToolResult> next)
	{
		ArgumentNullException.ThrowIfNull(next);
		return async (context, cancellationToken) =>
		{
			try
			{
				return await next(context, cancellationToken).ConfigureAwait(false);
			}
			catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
			{
				throw;
			}
			catch (McpProtocolException)
			{
				throw;
			}
			catch (InputRequiredException)
			{
				throw;
			}
			catch (Exception exception)
			{
				ToolError error = McpErrorCorrelation.Correlate(Classify(exception), out string? errorId);
				if (errorId is not null)
				{
					string toolName = context.Params?.Name ?? string.Empty;
					if (exception is CheatEngineToolException)
					{
						LogInternalToolError(CreateLogger(context), toolName, error.Operation ?? UnknownOperation,
							FailureType(exception), errorId);
					}
					else
					{
						LogUnexpectedToolFailure(CreateLogger(context), toolName, FailureType(exception), errorId);
					}
				}

				return ToolErrorResults.Create(error);
			}
		};
	}

	/// <summary>
	///     Refuses a call to a tool whose <see cref="RequiresFeatureAttribute" /> names a disabled exposure switch, with
	///     <c>capability_disabled</c> and <c>not_started</c>, before argument binding and before any dispatch.
	/// </summary>
	/// <remarks>
	///     It runs inside <see cref="MapErrors" />, which turns the refusal into the envelope. The gate is the activation's,
	///     borrowed through <paramref name="binding" />, so the MCP host registers no domain service. The catalog binding
	///     has no gate and never invokes an instance primitive; a gated call without a gate is refused as well.
	/// </remarks>
	/// <param name="next">The rest of the pipeline.</param>
	/// <param name="binding">Where the activation's gate comes from.</param>
	/// <returns>The wrapped handler.</returns>
	internal static McpRequestHandler<CallToolRequestParams, CallToolResult> EnforceFeatures(
		McpRequestHandler<CallToolRequestParams, CallToolResult> next, McpPrimitiveBinding binding)
	{
		ArgumentNullException.ThrowIfNull(next);
		ArgumentNullException.ThrowIfNull(binding);
		return (context, cancellationToken) =>
		{
			if (context.MatchedPrimitive is McpServerTool tool)
			{
				foreach (object metadata in tool.Metadata)
				{
					if (metadata is RequiresFeatureAttribute requirement)
					{
						Require(binding.Gate, requirement.Feature, tool.ProtocolTool.Name);
					}
				}
			}

			return next(context, cancellationToken);
		};
	}

	/// <summary>Repairs stringified arrays and objects, optional nulls and boolean strings before argument binding.</summary>
	/// <param name="next">The rest of the pipeline.</param>
	/// <returns>The wrapped handler.</returns>
	internal static McpRequestHandler<CallToolRequestParams, CallToolResult> NormalizeArguments(
		McpRequestHandler<CallToolRequestParams, CallToolResult> next)
	{
		ArgumentNullException.ThrowIfNull(next);
		return (context, cancellationToken) =>
		{
			if (context.MatchedPrimitive is McpServerTool tool &&
				context.Params is { Arguments: { Count: > 0 } arguments } parameters &&
				ToolArgumentNormalizer.Normalize(tool.ProtocolTool.InputSchema, arguments) is { } normalized)
			{
				parameters.Arguments = normalized;
			}

			return next(context, cancellationToken);
		};
	}

	/// <summary>
	///     Re-checks, before a resource read binds or dispatches anything, every switch the matched resource needs: those
	///     of the tool a live resource projects (<see cref="McpSourceToolAttribute" />) and any
	///     <see cref="RequiresFeatureAttribute" /> on the resource method itself. A disabled switch is refused with
	///     <c>capability_disabled</c> and <c>not_started</c>.
	/// </summary>
	/// <remarks>
	///     It runs inside <see cref="MapResourceErrors" />, which turns the refusal into a JSON-RPC error. The startup
	///     validator already keeps a gated tool out of every live resource; this filter is the defence in depth that keeps
	///     a resource read from ever bypassing a gate the tool call would enforce.
	/// </remarks>
	/// <param name="next">The rest of the pipeline.</param>
	/// <param name="binding">Where the activation's gate comes from.</param>
	/// <returns>The wrapped handler.</returns>
	internal static McpRequestHandler<ReadResourceRequestParams, ReadResourceResult> EnforceResourceFeatures(
		McpRequestHandler<ReadResourceRequestParams, ReadResourceResult> next, McpPrimitiveBinding binding)
	{
		ArgumentNullException.ThrowIfNull(next);
		ArgumentNullException.ThrowIfNull(binding);
		return (context, cancellationToken) =>
		{
			if (context.MatchedPrimitive is McpServerResource resource)
			{
				// The refusal names what is gated: the projected tool, or the resource itself.
				if (McpResourceSource.Of(resource) is { } source)
				{
					foreach (McpFeature feature in source.Requires)
					{
						Require(binding.Gate, feature, source.ToolName);
					}
				}

				foreach (object metadata in resource.Metadata)
				{
					if (metadata is RequiresFeatureAttribute requirement)
					{
						Require(binding.Gate, requirement.Feature, resource.ProtocolResourceTemplate.Name);
					}
				}
			}

			return next(context, cancellationToken);
		};
	}

	/// <summary>
	///     Turns a failed resource read into a JSON-RPC error: <c>-32602</c> for an invalid argument, <c>-32002</c> for a
	///     missing resource (<c>-32602</c> from protocol 2026-07-28), <c>-32603</c> otherwise. <see cref="Exception.Data" />
	///     carries kind, operation, hostEffect, retryable and hint as strings and booleans only, and the errorId of an
	///     <c>internal</c> error.
	/// </summary>
	/// <remarks>
	///     <para>
	///         A request whose own token was cancelled, <see cref="McpProtocolException" /> and
	///         <see cref="InputRequiredException" /> are rethrown unchanged. Every other failure is classified by
	///         <see cref="Classify" />, exactly as <see cref="MapErrors" /> classifies a tool call's, so one fault
	///         reports the same kind whether a tool call or a resource read reached it.
	///     </para>
	///     <para>
	///         Resource methods take only strings (the startup validator refuses anything else), so the SDK's binding
	///         raises no conversion error: a <see cref="FormatException" />, <see cref="OverflowException" /> or
	///         <see cref="JsonException" /> comes from the body and is an internal fault, not the caller's argument,
	///         even though <see cref="Classify" /> reports a tool call's <see cref="JsonException" /> as
	///         <c>invalid_argument</c>. Invalid URI variables are refused by <see cref="McpResourceQuery" /> with
	///         <c>invalid_argument</c> instead. An unexpected exception is logged and reported as <c>internal</c> with
	///         <c>unknown</c> and a generic message.
	///     </para>
	///     <para>
	///         Every <c>internal</c> error is correlated like a tool call's: <c>error.data.errorId</c> is the id that
	///         the log event (3005 for an unexpected exception, 3007 for an internal error the resource reported)
	///         records with the resource name and the exception's type name, never its message.
	///     </para>
	/// </remarks>
	/// <param name="next">The rest of the pipeline.</param>
	/// <returns>The wrapped handler.</returns>
	internal static McpRequestHandler<ReadResourceRequestParams, ReadResourceResult> MapResourceErrors(
		McpRequestHandler<ReadResourceRequestParams, ReadResourceResult> next)
	{
		ArgumentNullException.ThrowIfNull(next);
		return async (context, cancellationToken) =>
		{
			try
			{
				return await next(context, cancellationToken).ConfigureAwait(false);
			}
			catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
			{
				throw;
			}
			catch (McpProtocolException)
			{
				throw;
			}
			catch (InputRequiredException)
			{
				throw;
			}
			catch (Exception exception)
			{
				ToolError error = McpErrorCorrelation.Correlate(
					exception is JsonException ? Unexpected() : Classify(exception), out string? errorId);
				if (errorId is not null)
				{
					string resourceName = (context.MatchedPrimitive as McpServerResource)?.ProtocolResourceTemplate.Name
										  ?? string.Empty;
					if (exception is CheatEngineToolException)
					{
						LogInternalResourceError(CreateLogger(context), resourceName,
							error.Operation ?? UnknownOperation, FailureType(exception), errorId);
					}
					else
					{
						LogUnexpectedResourceFailure(CreateLogger(context), resourceName, FailureType(exception),
							errorId);
					}
				}

				throw McpResourceErrors.Create(error, context.Server.NegotiatedProtocolVersion, exception);
			}
		};
	}

	/// <summary>
	///     Turns an invalid prompt request into a JSON-RPC <c>-32602</c> error with the contract fields in
	///     <see cref="Exception.Data" />: a missing or malformed argument (the SDK's <see cref="ArgumentException" />, a
	///     <see cref="JsonException" /> or a <see cref="CheatEngineToolException" />). Prompts only render text, so nothing
	///     has started.
	/// </summary>
	/// <param name="next">The rest of the pipeline.</param>
	/// <returns>The wrapped handler.</returns>
	internal static McpRequestHandler<GetPromptRequestParams, GetPromptResult> MapPromptErrors(
		McpRequestHandler<GetPromptRequestParams, GetPromptResult> next)
	{
		ArgumentNullException.ThrowIfNull(next);
		return async (context, cancellationToken) =>
		{
			try
			{
				return await next(context, cancellationToken).ConfigureAwait(false);
			}
			catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
			{
				throw;
			}
			catch (Exception exception) when (exception is CheatEngineToolException or ArgumentException
												  or JsonException)
			{
				throw McpResourceErrors.CreateInvalidParams(Classify(exception), exception);
			}
		};
	}

	/// <summary>
	///     Bounds every <c>completion/complete</c> answer to <see cref="McpCompletions.MaximumValues" /> values
	///     (<see cref="McpCompletions.Bound" />). The SDK builds the completion pipeline around the step that appends a
	///     parameter's <c>[AllowedValues]</c> without any bound, so this filter also sees and bounds those values.
	/// </summary>
	/// <param name="next">The rest of the pipeline.</param>
	/// <returns>The wrapped handler.</returns>
	internal static McpRequestHandler<CompleteRequestParams, CompleteResult> BoundCompletions(
		McpRequestHandler<CompleteRequestParams, CompleteResult> next)
	{
		ArgumentNullException.ThrowIfNull(next);
		return async (context, cancellationToken) =>
		{
			CompleteResult result = await next(context, cancellationToken).ConfigureAwait(false);
			McpCompletions.Bound(result.Completion);
			return result;
		};
	}

	/// <summary>Maps an exception that escaped a tool to the contract error.</summary>
	/// <param name="exception">The exception.</param>
	/// <returns>The error to report.</returns>
	internal static ToolError Classify(Exception exception)
	{
		return exception switch
		{
			CheatEngineToolException tool => tool.Error,
			CheatEngineClientException client => ToolFailureMapping.Map(client.Failure, false),
			CheatEngineOperationCanceledException cancelled => ToolFailureMapping.Map(cancelled.Failure, false),
			ArgumentException or JsonException => new ToolError(ToolErrorKind.InvalidArgument, exception.Message, null,
				ToolHostEffect.NotStarted, false),
			_ => Unexpected()
		};
	}

	/// <summary>Selects the JSON-RPC error code of a failed resource read.</summary>
	/// <param name="kind">The error kind.</param>
	/// <param name="negotiatedProtocolVersion">The session's protocol version.</param>
	/// <returns>The error code.</returns>
	internal static McpErrorCode ResourceErrorCode(ToolErrorKind kind, string? negotiatedProtocolVersion)
	{
		return McpResourceErrors.Code(kind, negotiatedProtocolVersion);
	}

	/// <summary>The error of an unexpected fault: <c>internal</c> with <c>unknown</c> and a generic message.</summary>
	private static ToolError Unexpected()
	{
		return new ToolError(ToolErrorKind.Internal, UnexpectedMessage, null, ToolHostEffect.Unknown, false,
			CheatEngineToolException.InternalHint);
	}

	private static void Require(McpFeatureGate? gate, McpFeature feature, string toolName)
	{
		if (gate is null)
		{
			throw new CheatEngineToolException(new ToolError(ToolErrorKind.Internal,
				$"{toolName} requires the {McpFeatureGate.SettingName(feature)} switch, but this server has no feature " +
				"gate; it was not started.", null, ToolHostEffect.NotStarted, false));
		}

		gate.Require(feature, toolName);
	}

	/// <summary>
	///     The type name a correlated log event records: the exception a reported internal error wraps, or the
	///     exception itself. Only the type name is logged, never the message, a path, a token or target data.
	/// </summary>
	/// <param name="exception">The failure.</param>
	/// <returns>A type name such as <c>NullReferenceException</c>.</returns>
	private static string FailureType(Exception exception)
	{
		return exception is CheatEngineToolException { InnerException: { } wrapped }
			? wrapped.GetType().Name
			: exception.GetType().Name;
	}

	private static ILogger CreateLogger<TParams>(RequestContext<TParams> context)
	{
		IServiceProvider? services = context.Services ?? context.Server.Services;
		return services?.GetService<ILoggerFactory>()?.CreateLogger(LoggerCategory) ?? NullLogger.Instance;
	}

	[LoggerMessage(EventId = 3002, Level = LogLevel.Error,
		Message = "MCP tool {ToolName} failed with an unexpected exception ({FailureType}); reported as internal " +
				  "(errorId {ErrorId}).")]
	private static partial void LogUnexpectedToolFailure(ILogger logger, string toolName, string failureType,
		string errorId);

	[LoggerMessage(EventId = 3005, Level = LogLevel.Error,
		Message = "MCP resource {ResourceName} failed with an unexpected exception ({FailureType}); reported as " +
				  "internal (errorId {ErrorId}).")]
	private static partial void LogUnexpectedResourceFailure(ILogger logger, string resourceName, string failureType,
		string errorId);

	[LoggerMessage(EventId = 3006, Level = LogLevel.Error,
		Message = "MCP tool {ToolName} reported an internal error in operation {Operation} ({FailureType}; " +
				  "errorId {ErrorId}).")]
	private static partial void LogInternalToolError(ILogger logger, string toolName, string operation,
		string failureType, string errorId);

	[LoggerMessage(EventId = 3007, Level = LogLevel.Error,
		Message = "MCP resource {ResourceName} reported an internal error in operation {Operation} ({FailureType}; " +
				  "errorId {ErrorId}).")]
	private static partial void LogInternalResourceError(ILogger logger, string resourceName, string operation,
		string failureType, string errorId);
}
