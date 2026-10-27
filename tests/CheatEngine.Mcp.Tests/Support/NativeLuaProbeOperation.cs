using System.Globalization;
using System.Text;

using CheatEngine.Client.Lua;
using CheatEngine.Client.Results;
using CheatEngine.Mcp.Plugin.Lua;
using CheatEngine.SDK.Lua.Calls;
using CheatEngine.SDK.Lua.Runtime;
using CheatEngine.SDK.Lua.State;

namespace CheatEngine.Mcp.Tests.Support;

/// <summary>Executes arbitrary Lua only for NativeLua test setup and probes.</summary>
internal sealed record NativeLuaProbeOperation(string Operation, string Source) : ILuaOperation<object?>
{
	public bool TryExecute(ILuaExecutionContext context, out object? result, out CheatEngineFailure failure)
	{
		context.ThrowIfExpired();
		return TryExecuteInRuntime(out result, out failure);
	}

	internal bool TryExecuteInRuntime(out object? result, out CheatEngineFailure failure)
	{
		result = null;
		CheatEngineHostEffect effect = CheatEngineHostEffect.NotStarted;
		try
		{
			if (!PluginLuaToolRuntime.TryAcquire(out LuaRuntimeOperation acquired, out failure))
			{
				return false;
			}

			using LuaRuntimeOperation operation = acquired;
			LuaState state = operation.State;
			using LuaFrame frame = new(state);
			if (!PluginLuaToolRuntime.TryRun(state, Operation, Source, ref effect, out failure))
			{
				return false;
			}

			effect = CheatEngineHostEffect.Completed;
			result = new ResultReader().Read(state, -1, 0);
			failure = default;
			return true;
		}
		catch (Exception exception)
		{
			failure = new CheatEngineFailure(
				exception is InvalidDataException
					? CheatEngineFailureKind.ResultLimitExceeded
					: CheatEngineFailureKind.BindingError,
				"Lua.Execute", exception.Message, exception, effect);
			return false;
		}
	}

	private sealed class ResultReader
	{
		private readonly HashSet<nint> _tables = [];
		private int _bytes;
		private int _items;

		public object? Read(LuaState state, int index, int depth)
		{
			if (++_items > PluginLuaJsonWriter.MaximumItems || depth > 16)
			{
				throw new InvalidDataException(
					"Lua result exceeds the item/depth limit; the operation has already run.");
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
					if (_bytes > PluginLuaJsonWriter.MaximumStringBytes)
					{
						throw new InvalidDataException("Lua result exceeds 4 MiB of strings.");
					}

					return Encoding.UTF8.GetString(bytes);
				case LuaType.Table:
					return ReadTable(state, index, depth);
				default:
					throw new InvalidDataException(
						"Lua returned an opaque object; the tool must copy its documented fields.");
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
					if (integer is > 0 and <= PluginLuaJsonWriter.MaximumItems)
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
			bool packed = values.TryGetValue("n", out object? count) && count is long length &&
						  length is >= 0 and <= PluginLuaJsonWriter.MaximumItems && numericKeys == values.Count - 1;
			int arrayLength = packed ? (int) (long) count! : values.Count;
			// Fixed tool bodies use empty tables as lists. Object results always have named fields.
			if (packed || (numericKeys == values.Count && Enumerable.Range(1, arrayLength)
					.All(i => values.ContainsKey(i.ToString(CultureInfo.InvariantCulture)))))
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
