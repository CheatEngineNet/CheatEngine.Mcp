using System.Text.Json;

using CheatEngine.Mcp.Core.Contract;

using ModelContextProtocol;

namespace CheatEngine.Mcp.Core.Composition;

/// <summary>
///     Builds the JSON-RPC error of a failed resource read or prompt request, the same way on every backend and on the
///     gateway: the contract kind selects the code, and <see cref="Exception.Data" /> (the JSON-RPC <c>error.data</c>)
///     carries <c>kind</c>, <c>operation</c>, <c>hostEffect</c>, <c>retryable</c> and <c>hint</c> as strings and booleans.
/// </summary>
public static class McpResourceErrors
{
	/// <summary>The first protocol version that reports a missing resource as <c>-32602</c> instead of <c>-32002</c>.</summary>
	public const string InvalidParamsForMissingResourceVersion = "2026-07-28";

	/// <summary>Selects the JSON-RPC error code of a failed resource read.</summary>
	/// <param name="kind">The error kind.</param>
	/// <param name="negotiatedProtocolVersion">The session's protocol version.</param>
	/// <returns>
	///     <c>-32602</c> for an invalid argument, <c>-32002</c> for a missing resource (<c>-32602</c> from protocol
	///     2026-07-28), <c>-32603</c> otherwise.
	/// </returns>
	public static McpErrorCode Code(ToolErrorKind kind, string? negotiatedProtocolVersion)
	{
		return kind switch
		{
			ToolErrorKind.InvalidArgument => McpErrorCode.InvalidParams,
			ToolErrorKind.NotFound => negotiatedProtocolVersion is not null &&
									  StringComparer.Ordinal.Compare(negotiatedProtocolVersion,
										  InvalidParamsForMissingResourceVersion) >= 0
				? McpErrorCode.InvalidParams
				: McpErrorCode.ResourceNotFound,
			_ => McpErrorCode.InternalError
		};
	}

	/// <summary>Creates the protocol exception of a failed resource read.</summary>
	/// <param name="error">The contract error.</param>
	/// <param name="negotiatedProtocolVersion">The session's protocol version, which selects the not-found code.</param>
	/// <param name="innerException">The original failure, kept only for local diagnostics.</param>
	/// <returns>The exception to throw from a resource handler or filter.</returns>
	public static McpProtocolException Create(ToolError error, string? negotiatedProtocolVersion,
		Exception? innerException = null)
	{
		ArgumentNullException.ThrowIfNull(error);
		return WithData(new McpProtocolException(error.Message, innerException,
			Code(error.Kind, negotiatedProtocolVersion)), error);
	}

	/// <summary>Creates the protocol exception of an invalid prompt request: always <c>-32602</c>.</summary>
	/// <param name="error">The contract error.</param>
	/// <param name="innerException">The original failure, kept only for local diagnostics.</param>
	/// <returns>The exception to throw from a prompt handler or filter.</returns>
	public static McpProtocolException CreateInvalidParams(ToolError error, Exception? innerException = null)
	{
		ArgumentNullException.ThrowIfNull(error);
		return WithData(new McpProtocolException(error.Message, innerException, McpErrorCode.InvalidParams), error);
	}

	/// <summary>Copies the contract fields into a protocol exception's data.</summary>
	/// <param name="exception">The exception.</param>
	/// <param name="error">The contract error.</param>
	/// <returns>The same exception.</returns>
	public static McpProtocolException WithData(McpProtocolException exception, ToolError error)
	{
		ArgumentNullException.ThrowIfNull(exception);
		ArgumentNullException.ThrowIfNull(error);
		exception.Data["kind"] = ContractName(error.Kind);
		if (error.Operation is not null)
		{
			exception.Data["operation"] = error.Operation;
		}

		exception.Data["hostEffect"] = ContractName(error.HostEffect);
		exception.Data["retryable"] = error.Retryable;
		if (error.Hint is not null)
		{
			exception.Data["hint"] = error.Hint;
		}

		return exception;
	}

	/// <summary>The contract name of an error kind, such as <c>not_found</c>.</summary>
	/// <param name="kind">The kind.</param>
	/// <returns>Its snake_case name.</returns>
	public static string ContractName(ToolErrorKind kind)
	{
		return JsonSerializer.SerializeToElement(kind, CoreJsonContext.Default.ToolErrorKind).GetString()!;
	}

	/// <summary>The contract name of a host effect, such as <c>not_started</c>.</summary>
	/// <param name="effect">The host effect.</param>
	/// <returns>Its snake_case name.</returns>
	public static string ContractName(ToolHostEffect effect)
	{
		return JsonSerializer.SerializeToElement(effect, CoreJsonContext.Default.ToolHostEffect).GetString()!;
	}
}
