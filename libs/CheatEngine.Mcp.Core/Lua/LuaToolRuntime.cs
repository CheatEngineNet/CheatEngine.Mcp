using System.Collections;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

using CheatEngine.Mcp.Core.Contract;
using CheatEngine.Mcp.Core.Execution;

namespace CheatEngine.Mcp.Core.Lua;

/// <summary>Builds bounded fixed Lua source and maps its copied result to the MCP contract.</summary>
/// <remarks>
///     <para>
///         Tools reach the typed path through <see cref="ToolDispatch" />.  Core builds
///         <c>local k = {...}; local a = {...}; local mcp = ...</c> ahead of a fixed body, then asks the Plugin's
///         Client-dispatched bridge to copy the result into a source-generated type.
///     </para>
/// </remarks>
public static class LuaToolRuntime
{
	internal const int MaximumStringBytes = 4 * 1024 * 1024;
	internal const int MaximumItems = 65536;
	internal const int MaximumErrorFieldBytes = 4096;

	/// <summary>The dispatch budget written into <c>k.budgetMs</c> when no configured budget is at hand.</summary>
	internal const int DefaultBudgetMilliseconds = 100;

	internal const string ContractViolationMessage =
		"A fixed Lua script returned a result that breaks its contract; the script has already run.";

	/// <summary>
	///     Obtains one copied fixed-Lua result through the Plugin bridge and applies Core's contract error mapping.
	/// </summary>
	internal static T ExecuteSource<T>(IFixedLuaExecutor executor, string operation, string source,
		JsonTypeInfo<T> resultType, LuaJsonBufferPool buffers, CancellationToken cancellationToken)
	{
		return ExecuteSourceWithCopy(executor, operation, source, resultType, buffers, LuaOpaqueValueHandling.Reject,
			cancellationToken).Value;
	}

	/// <summary>
	///     Obtains one copied fixed-Lua result while retaining the opaque-value count for the caller-Lua second stage.
	/// </summary>
	internal static LuaCopiedResult<T> ExecuteSourceWithCopy<T>(IFixedLuaExecutor executor, string operation,
		string source, JsonTypeInfo<T> resultType, LuaJsonBufferPool buffers, LuaOpaqueValueHandling opaque,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(executor);
		ArgumentException.ThrowIfNullOrWhiteSpace(operation);
		ArgumentNullException.ThrowIfNull(source);
		ArgumentNullException.ThrowIfNull(resultType);
		ArgumentNullException.ThrowIfNull(buffers);
		LuaJsonResult<T> result;
		try
		{
			result = executor.Execute(operation, source, resultType, buffers, opaque, cancellationToken);
		}
		catch (LuaJsonException exception)
		{
			throw exception.Violation is LuaJsonViolation.Limit
				? new CheatEngineToolException(new ToolError(ToolErrorKind.LimitExceeded, exception.Message, operation,
					ToolHostEffect.Completed, false, ToolFailureMapping.LimitHint), exception)
				: new CheatEngineToolException(new ToolError(ToolErrorKind.Internal, ContractViolationMessage,
					operation, ToolHostEffect.Completed, false, CheatEngineToolException.InternalHint), exception);
		}

		return result.IsError
			? throw ScriptError(operation, result.Error)
			: new LuaCopiedResult<T>(result.Value!, result.DroppedOpaqueCount);
	}

	/// <summary>Maps a failure a fixed script declared through <c>mcp_error</c> to the contract.</summary>
	internal static CheatEngineToolException ScriptError(string operation, LuaScriptError error)
	{
		ArgumentNullException.ThrowIfNull(error);
		ToolHostEffect effect = error.HostEffect is not null && TryParseContract(error.HostEffect,
			out ToolHostEffect declared)
			? declared
			: ToolHostEffect.Unknown;
		if (!TryParseContract(error.Kind, out ToolErrorKind kind))
		{
			return new CheatEngineToolException(new ToolError(ToolErrorKind.Internal,
				$"A fixed Lua script declared the unknown error kind {error.Kind}: {error.Message}", operation, effect,
				false, error.Hint ?? CheatEngineToolException.InternalHint));
		}

		return new CheatEngineToolException(new ToolError(kind, error.Message, operation, effect,
			ToolFailureMapping.IsRetryable(kind, effect), error.Hint));
	}

	/// <summary>
	///     Builds a v2 script: <c>local k = { budgetMs = … }; local a = { n = …, … }; local mcp = …</c> on the first line,
	///     then the fixed body.  Call it before Client dispatch, off Cheat Engine's main thread.
	/// </summary>
	internal static string BuildSource(string body, int budgetMilliseconds, ReadOnlySpan<object?> arguments)
	{
		ArgumentNullException.ThrowIfNull(body);
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(budgetMilliseconds);
		StringBuilder source = new("local k = { budgetMs = ");
		source.Append(budgetMilliseconds.ToString(CultureInfo.InvariantCulture)).Append(" }; ");
		AppendArguments(source, arguments);
		return source.Append(" }; ").Append(LuaPrelude.Source).Append('\n').Append(body).ToString();
	}

	internal static string BuildSource(string body, object?[] arguments)
	{
		StringBuilder source = new();
		AppendArguments(source, arguments);
		return source.Append(" };\n").Append(body).ToString();
	}

	private static void AppendArguments(StringBuilder source, ReadOnlySpan<object?> arguments)
	{
		source.Append("local a = { n = ").Append(arguments.Length.ToString(CultureInfo.InvariantCulture));
		int items = 0;
		for (int index = 0; index < arguments.Length; index++)
		{
			source.Append(", [").Append((index + 1).ToString(CultureInfo.InvariantCulture)).Append("] = ");
			AppendValue(source, arguments[index], 0, ref items);
		}
	}

	private static void AppendValue(StringBuilder source, object? value, int depth, ref int items)
	{
		if (depth > 16 || ++items > MaximumItems || source.Length > MaximumStringBytes)
		{
			throw new ArgumentException("Lua arguments exceed the materialization limit.");
		}

		switch (value)
		{
			case null:
				source.Append("nil");
				break;
			case bool boolean:
				source.Append(boolean ? "true" : "false");
				break;
			case string text:
				if (Encoding.UTF8.GetByteCount(text) > MaximumStringBytes / 4)
				{
					throw new ArgumentException("A Lua argument exceeds 1 MiB.");
				}

				source.Append('"');
				foreach (byte character in Encoding.UTF8.GetBytes(text))
				{
					if (character is >= 32 and <= 126 and not (byte) '"' and not (byte) '\\')
					{
						source.Append((char) character);
					}
					else
					{
						source.Append('\\').Append(character.ToString("D3", CultureInfo.InvariantCulture));
					}
				}

				source.Append('"');
				break;
			case byte or sbyte or short or ushort or int or uint or long:
				source.Append(Convert.ToString(value, CultureInfo.InvariantCulture));
				break;
			case ulong number:
				source.Append("0x").Append(number.ToString("X", CultureInfo.InvariantCulture));
				break;
			case double number when double.IsFinite(number):
				source.Append(number.ToString("R", CultureInfo.InvariantCulture));
				break;
			case float number when float.IsFinite(number):
				source.Append(number.ToString("R", CultureInfo.InvariantCulture));
				break;
			case IEnumerable sequence:
				source.Append('{');
				foreach (object? item in sequence)
				{
					AppendValue(source, item, depth + 1, ref items);
					source.Append(',');
				}

				source.Append('}');
				break;
			default:
				throw new ArgumentException("Unsupported Lua argument type or non-finite number.");
		}

		if (source.Length > MaximumStringBytes)
		{
			throw new ArgumentException("Lua arguments exceed 4 MiB.");
		}
	}

	private static bool TryParseContract<TEnum>(string token, out TEnum value) where TEnum : struct, Enum
	{
		foreach (TEnum candidate in Enum.GetValues<TEnum>())
		{
			if (string.Equals(JsonNamingPolicy.SnakeCaseLower.ConvertName(candidate.ToString()), token,
					StringComparison.Ordinal))
			{
				value = candidate;
				return true;
			}
		}

		value = default;
		return false;
	}
}
