using System.Buffers;
using System.Security.Cryptography;
using System.Text.Json;

using CheatEngine.Mcp.Core.Contract;

namespace CheatEngine.Mcp.Core.Composition;

/// <summary>
///     Correlates an <c>internal</c> failure with the log event that records it. The error carries a random
///     <c>errorId</c> (in the tool envelope's <c>details</c>, and in a resource error's <c>data</c>) and the host logs
///     the same id with the tool or resource name and the exception's type name, so the user can find the entry in the
///     plugin log.
/// </summary>
/// <remarks>
///     The id is random: it carries no exception text, path, token or target data, and it identifies nothing outside
///     one failure. The log event records the exception's type name only, never its message or stack trace.
/// </remarks>
internal static class McpErrorCorrelation
{
	/// <summary>The property of a tool error's <c>details</c>, and the key of a resource error's <c>data</c>.</summary>
	internal const string Key = "errorId";

	private const int IdLength = 16;

	/// <summary>
	///     Correlates an <c>internal</c> error: it keeps an id the error already carries, or adds a new one to its
	///     <c>details</c> object, which it creates when there is none. Non-object details are retained under
	///     <c>value</c> in that object.
	/// </summary>
	/// <param name="error">The error to report.</param>
	/// <param name="errorId">
	///     The id to log; <see langword="null" /> for any other kind.
	/// </param>
	/// <returns>The error to report, correlated for an internal failure.</returns>
	internal static ToolError Correlate(ToolError error, out string? errorId)
	{
		ArgumentNullException.ThrowIfNull(error);
		errorId = null;
		if (error.Kind is not ToolErrorKind.Internal)
		{
			return error;
		}

		errorId = Read(error);
		if (errorId is not null)
		{
			return error;
		}

		errorId = RandomNumberGenerator.GetHexString(IdLength, true);
		return error with
		{
			Details = WithId(error.Details, errorId)
		};
	}

	/// <summary>Reads the id of a correlated error.</summary>
	/// <param name="error">An error.</param>
	/// <returns>The id in its <c>details</c>; <see langword="null" /> when it carries none.</returns>
	internal static string? Read(ToolError error)
	{
		ArgumentNullException.ThrowIfNull(error);
		return error.Details is { ValueKind: JsonValueKind.Object } details &&
			   details.TryGetProperty(Key, out JsonElement id) && id.ValueKind == JsonValueKind.String
			? id.GetString()
			: null;
	}

	// Keeps structured details and appends the id, wrapping any non-object value without discarding it.
	private static JsonElement WithId(JsonElement? details, string errorId)
	{
		ArrayBufferWriter<byte> buffer = new();
		using (Utf8JsonWriter writer = new(buffer))
		{
			writer.WriteStartObject();
			if (details is { ValueKind: JsonValueKind.Object } existing)
			{
				foreach (JsonProperty property in existing.EnumerateObject())
				{
					if (!property.NameEquals(Key))
					{
						property.WriteTo(writer);
					}
				}
			}
			else if (details is { ValueKind: not JsonValueKind.Undefined } value)
			{
				writer.WritePropertyName("value");
				value.WriteTo(writer);
			}

			writer.WriteString(Key, errorId);
			writer.WriteEndObject();
		}

		using JsonDocument document = JsonDocument.Parse(buffer.WrittenMemory);
		return document.RootElement.Clone();
	}
}
