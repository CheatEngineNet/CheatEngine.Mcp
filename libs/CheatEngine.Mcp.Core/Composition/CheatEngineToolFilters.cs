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
				ToolError error = Classify(exception);
				if (error.Kind is ToolErrorKind.Internal && exception is not CheatEngineToolException)
				{
					LogUnexpectedToolFailure(CreateLogger(context), context.Params?.Name ?? string.Empty, exception);
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
	///     Turns a failed resource read into a JSON-RPC error: <c>-32602</c> for an invalid argument, <c>-32002</c> for a
	///     missing resource (<c>-32602</c> from protocol 2026-07-28), <c>-32603</c> otherwise. <see cref="Exception.Data" />
	///     carries kind, operation, hostEffect, retryable and hint as strings and booleans only.
	/// </summary>
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
			catch (Exception exception) when (exception is CheatEngineToolException or CheatEngineClientException
												  or CheatEngineOperationCanceledException or ArgumentException
												  or FormatException)
			{
				// The SDK binds template variables before the body runs, so its ArgumentException and a conversion's
				// FormatException are invalid arguments; a resource body is read-only, so nothing has started.
				ToolError error = exception is FormatException
					? new ToolError(ToolErrorKind.InvalidArgument, exception.Message, null, ToolHostEffect.NotStarted,
						false)
					: Classify(exception);
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
			_ => new ToolError(ToolErrorKind.Internal, UnexpectedMessage, null, ToolHostEffect.Unknown, false,
				CheatEngineToolException.InternalHint)
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

	private static ILogger CreateLogger(RequestContext<CallToolRequestParams> context)
	{
		IServiceProvider? services = context.Services ?? context.Server.Services;
		return services?.GetService<ILoggerFactory>()?.CreateLogger(LoggerCategory) ?? NullLogger.Instance;
	}

	[LoggerMessage(EventId = 3002, Level = LogLevel.Error,
		Message = "MCP tool {ToolName} failed with an unexpected exception; reported as internal.")]
	private static partial void LogUnexpectedToolFailure(ILogger logger, string toolName, Exception exception);
}
