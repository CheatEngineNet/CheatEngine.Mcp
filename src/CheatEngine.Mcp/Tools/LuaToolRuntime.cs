using System.Collections;
using System.Globalization;
using System.Text;

using CheatEngine.Client;
using CheatEngine.Client.Lua;
using CheatEngine.Client.Results;
using CheatEngine.SDK.Lua.Calls;
using CheatEngine.SDK.Lua.Runtime;
using CheatEngine.SDK.Lua.State;

namespace CheatEngine.Mcp.Tools;

/// <summary>Confines protected Lua access to a Client-dispatched operation and copies bounded results.</summary>
internal static class LuaToolRuntime
{
	internal const int MaximumStringBytes = 4 * 1024 * 1024;
	internal const int MaximumItems = 65536;

	public static object Invoke(ICheatEngineClient client, string operation, string body, params object?[] arguments) =>
		ToolExecution.Run(client, () =>
		{
			object? result = Execute(client, operation, body, arguments);
			return new
			{
				success = true,
				result
			};
		});

	internal static object? Execute(ICheatEngineClient client, string operation, string body, params object?[] arguments)
	{
		LuaToolOperation request = new(operation, BuildSource(body, arguments));
		return client.Lua.Execute<LuaToolOperation, object?>(request, client.Stopping);
	}

	public static object Call(ICheatEngineClient client, string function, params object?[] arguments)
	{
		// Only implementation-owned global names are accepted here, never Lua expressions supplied by callers.
		if (string.IsNullOrEmpty(function) || function.Any(character => !char.IsAsciiLetterOrDigit(character) && character != '_'))
		{
			return ToolExecution.Error("Invalid Lua function name.");
		}
		return Invoke(client, function,
			$"local f = _G['{function}']; assert(type(f) == 'function', 'Required Cheat Engine API is unavailable: {function}'); return table.pack(f(table.unpack(a, 1, a.n)))",
			arguments);
	}

	internal static string BuildSource(string body, object?[] arguments)
	{
		StringBuilder source = new("local a = { n = ");
		source.Append(arguments.Length.ToString(CultureInfo.InvariantCulture));
		int items = 0;
		for (int index = 0; index < arguments.Length; index++)
		{
			source.Append(", [").Append((index + 1).ToString(CultureInfo.InvariantCulture)).Append("] = ");
			AppendValue(source, arguments[index], 0, ref items);
		}
		return source.Append(" };\n").Append(body).ToString();
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
				// Hex literals retain all pointer bits in Lua 5.3's signed 64-bit integer representation.
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

	internal sealed record LuaToolOperation(string Operation, string Source) : ILuaOperation<object?>
	{
		public bool TryExecute(ILuaExecutionContext context, out object? result, out CheatEngineFailure failure)
		{
			context.ThrowIfExpired();
			result = null;
			CheatEngineHostEffect effect = CheatEngineHostEffect.NotStarted;
			try
			{
				LuaAdmissionStatus admission = LuaRuntime.TryAcquireOperationWithOutcome(out LuaRuntimeOperation acquired);
				if (admission != LuaAdmissionStatus.Admitted)
				{
					CheatEngineFailureKind kind = admission switch
					{
						LuaAdmissionStatus.Detached or LuaAdmissionStatus.TransitionInProgress => CheatEngineFailureKind.ActivationExpired,
						LuaAdmissionStatus.ExternalStateReset => CheatEngineFailureKind.RuntimeChanged,
						LuaAdmissionStatus.NoStateForThread or LuaAdmissionStatus.ThreadNotAdmitted => CheatEngineFailureKind.InvalidState,
						_ => CheatEngineFailureKind.IndeterminateHostResult
					};
					failure = new(kind, "Lua.Execute", $"Lua runtime admission failed: {admission}.", hostEffect: effect);
					return false;
				}
				using LuaRuntimeOperation operation = acquired;
				LuaState state = operation.State;
				using LuaFrame frame = new(state);
				if (!state.TryEnsureStack(64))
				{
					throw new InvalidOperationException("Lua could not reserve stack space for bounded result copying.");
				}

				LuaStatus status = state.TryLoad(Encoding.UTF8.GetBytes(Source), Encoding.UTF8.GetBytes("=CheatEngine.Mcp/" + Operation));
				if (status.IsOk)
				{
					effect = CheatEngineHostEffect.Started;
					status = state.TryCall(0, 1);
				}
				if (!status.IsOk)
				{
					failure = new(CheatEngineFailureKind.LuaError, "Lua.Execute", LuaError.FromStack(state, status).Message, hostEffect: effect);
					return false;
				}
				effect = CheatEngineHostEffect.Completed;
				result = new ResultReader().Read(state, -1, 0);
				failure = default;
				return true;
			}
			catch (Exception exception)
			{
				failure = new(exception is InvalidDataException ? CheatEngineFailureKind.ResultLimitExceeded : CheatEngineFailureKind.BindingError,
					"Lua.Execute", exception.Message, exception, effect);
				return false;
			}
		}
	}

	private sealed class ResultReader
	{
		private int _items;
		private int _bytes;
		private readonly HashSet<nint> _tables = [];

		public object? Read(LuaState state, int index, int depth)
		{
			if (++_items > MaximumItems || depth > 16)
			{
				throw new InvalidDataException("Lua result exceeds the item/depth limit; the operation has already run.");
			}

			switch (state.TypeOf(index))
			{
				case LuaType.Nil:
					return null;
				case LuaType.Boolean:
					return state.ToBoolean(index);
				case LuaType.Number:
					if (state.IsInteger(index) && state.TryReadInteger(index, out long integer))
					{
						return integer;
					}

					if (state.TryReadNumber(index, out double number) && double.IsFinite(number))
					{
						return number;
					}

					throw new InvalidDataException("Lua returned a non-finite number.");
				case LuaType.String:
					if (!state.TryReadUtf8(index, out ReadOnlySpan<byte> bytes))
					{
						throw new InvalidDataException("Lua string could not be read.");
					}

					_bytes = checked(_bytes + bytes.Length);
					if (_bytes > MaximumStringBytes)
					{
						throw new InvalidDataException("Lua result exceeds 4 MiB of strings.");
					}

					return Encoding.UTF8.GetString(bytes);
				case LuaType.Table:
					return ReadTable(state, index, depth);
				default:
					throw new InvalidDataException("Lua returned an opaque object; the tool must copy its documented fields.");
			}
		}

		private object ReadTable(LuaState state, int index, int depth)
		{
			index = state.AbsoluteIndex(index);
			nint identity = state.ToPointer(index);
			if (!_tables.Add(identity))
			{
				throw new InvalidDataException("Lua returned a cyclic table.");
			}

			Dictionary<string, object?> values = new(StringComparer.Ordinal);
			int numericKeys = 0;
			state.PushNil();
			while (true)
			{
				LuaStatus status = state.TryNext(index, out bool hasNext);
				if (!status.IsOk)
				{
					throw new InvalidDataException(LuaError.FromStack(state, status).Message);
				}

				if (!hasNext)
				{
					break;
				}

				string key;
				if (state.IsInteger(-2) && state.TryReadInteger(-2, out long integer))
				{
					key = integer.ToString(CultureInfo.InvariantCulture);
					if (integer is > 0 and <= MaximumItems)
					{
						numericKeys++;
					}
				}
				else if (state.TypeOf(-2) == LuaType.String)
				{
					key = (string) Read(state, -2, depth + 1)!;
				}
				else
				{
					throw new InvalidDataException("Lua table keys must be strings or integers.");
				}

				if (!values.TryAdd(key, Read(state, -1, depth + 1)))
				{
					throw new InvalidDataException("Lua table keys collide after JSON conversion.");
				}

				state.Pop(1);
			}
			_tables.Remove(identity);
			bool packed = values.TryGetValue("n", out object? count) && count is long length && length is >= 0 and <= MaximumItems && numericKeys == values.Count - 1;
			int arrayLength = packed ? (int) (long) count! : values.Count;
			// Fixed tool bodies use empty tables as lists. Object results always have named fields.
			if (packed || (numericKeys == values.Count && Enumerable.Range(1, arrayLength).All(i => values.ContainsKey(i.ToString(CultureInfo.InvariantCulture)))))
			{
				object?[] array = new object?[arrayLength];
				for (int i = 0; i < array.Length; i++)
				{
					values.TryGetValue((i + 1).ToString(CultureInfo.InvariantCulture), out array[i]);
				}

				return array;
			}
			return values;
		}
	}
}
