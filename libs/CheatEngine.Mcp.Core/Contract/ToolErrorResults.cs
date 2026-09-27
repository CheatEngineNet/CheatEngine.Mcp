using System.Diagnostics.CodeAnalysis;
using System.Text.Json;

using ModelContextProtocol.Protocol;

namespace CheatEngine.Mcp.Core.Contract;

/// <summary>Builds and reads the <c>isError</c> tool result that carries a <see cref="ToolError" />.</summary>
public static class ToolErrorResults
{
	/// <summary>
	///     Creates the failure result: <c>isError:true</c>, one compact text block holding <c>{"error":{...}}</c>, and no
	///     structured content, so clients never validate an error against the tool's output schema.
	/// </summary>
	/// <param name="error">The error to report.</param>
	/// <returns>The tool result.</returns>
	public static CallToolResult Create(ToolError error)
	{
		return new CallToolResult
		{
			IsError = true, Content = [new TextContentBlock { Text = Serialize(error) }], StructuredContent = null
		};
	}

	/// <summary>Serializes the compact <c>{"error":{...}}</c> envelope.</summary>
	/// <param name="error">The error to serialize.</param>
	/// <returns>The envelope JSON.</returns>
	public static string Serialize(ToolError error)
	{
		ArgumentNullException.ThrowIfNull(error);
		return JsonSerializer.Serialize(new ToolErrorEnvelope(error), CoreJsonContext.Default.ToolErrorEnvelope);
	}

	/// <summary>Reads the error of a failure result created by <see cref="Create" />.</summary>
	/// <param name="result">A tool result.</param>
	/// <param name="error">The error when this method returns <see langword="true" />.</param>
	/// <returns>
	///     <see langword="true" /> when <paramref name="result" /> is an error result whose only content is a valid
	///     envelope.
	/// </returns>
	public static bool TryRead(CallToolResult result, [NotNullWhen(true)] out ToolError? error)
	{
		ArgumentNullException.ThrowIfNull(result);
		error = null;
		if (result.IsError is not true || result.Content is not [TextContentBlock { Text: { Length: > 0 } text }])
		{
			return false;
		}

		try
		{
			error = JsonSerializer.Deserialize(text, CoreJsonContext.Default.ToolErrorEnvelope)?.Error;
		}
		catch (JsonException)
		{
			return false;
		}

		return error is { Message: not null };
	}
}
