using System.Globalization;
using System.Text;
using System.Text.Json.Serialization.Metadata;

using CheatEngine.Client;
using CheatEngine.Client.Lua;
using CheatEngine.Client.Results;
using CheatEngine.Mcp.Core.Contract;
using CheatEngine.SDK.Lua.Calls;
using CheatEngine.SDK.Lua.Runtime;
using CheatEngine.SDK.Lua.State;

namespace CheatEngine.Mcp.Plugin.Lua;

/// <summary>Owns the Plugin's SDK-backed, Client-dispatched fixed Lua execution bridge.</summary>
/// <remarks>
///     <para>
///         The implementation stays in Plugin because it manipulates the SDK's protected Lua state.  Core supplies
///         source construction, copy limits and MCP error mapping through <see cref="IFixedLuaExecutor" />.
///     </para>
/// </remarks>
internal static class PluginLuaToolRuntime
{
	internal const string StatusUpdateSource = """
	                                           local menu = assert(getMainForm().Menu, 'The Cheat Engine main menu is unavailable.')
	                                           local item = menu.findComponentByName('CheatEngineMcpStatus')
	                                           if item == nil then
	                                             if a[3] then return false end
	                                             item = createMenuItem(menu)
	                                             item.Name = 'CheatEngineMcpStatus'
	                                             item.Tag = 0x4D4350
	                                             menu.Items.add(item)
	                                           end
	                                           assert(item.Tag == 0x4D4350, 'The MCP status menu name is already in use.')
	                                           item.Caption = a[1]
	                                           local details = a[2]
	                                           item.OnClick = function() showMessage(details) end
	                                           return item.Caption == a[1]
	                                           """;

	internal static bool ShowPluginStatus(ICheatEngineClient client, string state, string details, bool existingOnly)
	{
		ArgumentNullException.ThrowIfNull(client);
		if (!client.Dispatcher.IsMainThread)
		{
			throw new InvalidOperationException("The MCP status must be updated on Cheat Engine's main thread.");
		}

		LuaToolOperation operation = new("mcp_status",
			LuaToolRuntime.BuildSource(StatusUpdateSource, ["MCP: " + state, details, existingOnly]));
		if (!operation.TryExecuteInRuntime(out object? result, out CheatEngineFailure failure))
		{
			failure.Throw();
		}

		return result is true;
	}

	internal static T Execute<T>(ICheatEngineClient client, string operation, string body, JsonTypeInfo<T> resultType,
		CancellationToken cancellationToken, params ReadOnlySpan<object?> arguments)
	{
		string source = LuaToolRuntime.BuildSource(body, LuaToolRuntime.DefaultBudgetMilliseconds, arguments);
		return ExecuteSource(client, operation, source, resultType, new LuaJsonBufferPool(), cancellationToken);
	}

	internal static T ExecuteSource<T>(ICheatEngineClient client, string operation, string source,
		JsonTypeInfo<T> resultType, LuaJsonBufferPool buffers, CancellationToken cancellationToken)
	{
		return ExecuteSourceWithCopy(client, operation, source, resultType, buffers, LuaOpaqueValueHandling.Reject,
			cancellationToken).Value;
	}

	internal static LuaCopiedResult<T> ExecuteSourceWithCopy<T>(ICheatEngineClient client, string operation,
		string source, JsonTypeInfo<T> resultType, LuaJsonBufferPool buffers, LuaOpaqueValueHandling opaque,
		CancellationToken cancellationToken)
	{
		LuaJsonResult<T> result;
		try
		{
			result = ExecuteRaw(client, operation, source, resultType, buffers, opaque, cancellationToken);
		}
		catch (LuaJsonException exception)
		{
			throw CopyException(operation, exception);
		}

		return result.IsError
			? throw LuaToolRuntime.ScriptError(operation, result.Error)
			: new LuaCopiedResult<T>(result.Value!, result.DroppedOpaqueCount);
	}

	internal static LuaJsonResult<T> ExecuteRaw<T>(ICheatEngineClient client, string operation, string source,
		JsonTypeInfo<T> resultType, LuaJsonBufferPool buffers, LuaOpaqueValueHandling opaque,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(client);
		ArgumentException.ThrowIfNullOrWhiteSpace(operation);
		ArgumentNullException.ThrowIfNull(source);
		ArgumentNullException.ThrowIfNull(resultType);
		ArgumentNullException.ThrowIfNull(buffers);
		LuaJsonOperation<T> request = new(operation, source, resultType, buffers, opaque);
		return client.Lua.Execute<LuaJsonOperation<T>, LuaJsonResult<T>>(request, cancellationToken);
	}

	private static CheatEngineToolException CopyException(string operation, LuaJsonException exception)
	{
		return exception.Violation is LuaJsonViolation.Limit
			? new CheatEngineToolException(new ToolError(ToolErrorKind.LimitExceeded, exception.Message, operation,
				ToolHostEffect.Completed, false, ToolFailureMapping.LimitHint), exception)
			: new CheatEngineToolException(new ToolError(ToolErrorKind.Internal,
				LuaToolRuntime.ContractViolationMessage,
				operation, ToolHostEffect.Completed, false, CheatEngineToolException.InternalHint), exception);
	}

	private static bool TryAcquire(out LuaRuntimeOperation acquired, out CheatEngineFailure failure)
	{
		LuaAdmissionStatus admission = LuaRuntime.TryAcquireOperationWithOutcome(out acquired);
		if (admission == LuaAdmissionStatus.Admitted)
		{
			failure = default;
			return true;
		}

		CheatEngineFailureKind kind = admission switch
		{
			LuaAdmissionStatus.Detached or LuaAdmissionStatus.TransitionInProgress => CheatEngineFailureKind
				.ActivationExpired,
			LuaAdmissionStatus.ExternalStateReset => CheatEngineFailureKind.RuntimeChanged,
			LuaAdmissionStatus.NoStateForThread or LuaAdmissionStatus.ThreadNotAdmitted =>
				CheatEngineFailureKind.InvalidState,
			_ => CheatEngineFailureKind.IndeterminateHostResult
		};
		failure = new CheatEngineFailure(kind, "Lua.Execute", $"Lua runtime admission failed: {admission}.",
			hostEffect: CheatEngineHostEffect.NotStarted);
		return false;
	}

	/// <summary>Compiles and runs one chunk, leaving its single result on the stack.</summary>
	/// <param name="state">The admitted Lua state.</param>
	/// <param name="operation">The operation, used in the chunk name.</param>
	/// <param name="source">The chunk.</param>
	/// <param name="effect">Advanced to <see cref="CheatEngineHostEffect.Started" /> once the chunk compiled.</param>
	/// <param name="failure">The compile or runtime failure.</param>
	/// <returns><see langword="true" /> when the chunk returned normally.</returns>
	private static bool TryRun(LuaState state, string operation, string source, ref CheatEngineHostEffect effect,
		out CheatEngineFailure failure)
	{
		if (!state.TryEnsureStack(64))
		{
			throw new InvalidOperationException("Lua could not reserve stack space for bounded result copying.");
		}

		LuaStatus status = state.TryLoad(Encoding.UTF8.GetBytes(source),
			Encoding.UTF8.GetBytes("=CheatEngine.Mcp/" + operation));
		if (status.IsOk)
		{
			effect = CheatEngineHostEffect.Started;
			status = state.TryCall(0, 1);
		}

		if (!status.IsOk)
		{
			failure = new CheatEngineFailure(CheatEngineFailureKind.LuaError, "Lua.Execute",
				LuaError.FromStack(state, status).Message, hostEffect: effect);
			return false;
		}

		failure = default;
		return true;
	}

	internal sealed record LuaToolOperation(string Operation, string Source) : ILuaOperation<object?>
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
				if (!TryAcquire(out LuaRuntimeOperation acquired, out failure))
				{
					return false;
				}

				using LuaRuntimeOperation operation = acquired;
				LuaState state = operation.State;
				using LuaFrame frame = new(state);
				if (!TryRun(state, Operation, Source, ref effect, out failure))
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
	}

	/// <summary>
	///     One v2 fixed script: its result is copied through <see cref="LuaJsonWriter" /> into <typeparamref name="T" />,
	///     or its declared <c>mcp_error</c> is returned. A copy failure propagates as <see cref="LuaJsonException" />.
	/// </summary>
	/// <typeparam name="T">The result type.</typeparam>
	/// <param name="Operation">A diagnostic operation name.</param>
	/// <param name="Source">The complete script.</param>
	/// <param name="ResultType">The source-generated metadata of <typeparamref name="T" />.</param>
	/// <param name="Buffers">The owner's JSON buffer pool.</param>
	internal sealed record LuaJsonOperation<T>(
		string Operation,
		string Source,
		JsonTypeInfo<T> ResultType,
		LuaJsonBufferPool Buffers,
		LuaOpaqueValueHandling Opaque) : ILuaOperation<LuaJsonResult<T>>
	{
		public bool TryExecute(ILuaExecutionContext context, out LuaJsonResult<T> result,
			out CheatEngineFailure failure)
		{
			ArgumentNullException.ThrowIfNull(context);
			context.ThrowIfExpired();
			result = default;
			if (!TryAcquire(out LuaRuntimeOperation acquired, out failure))
			{
				return false;
			}

			using LuaRuntimeOperation operation = acquired;
			LuaState state = operation.State;
			using LuaFrame frame = new(state);
			CheatEngineHostEffect effect = CheatEngineHostEffect.NotStarted;
			if (!TryRun(state, Operation, Source, ref effect, out failure))
			{
				return false;
			}

			result = PluginLuaJsonWriter.Read(state, -1, ResultType, Buffers, Opaque);
			failure = default;
			return true;
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

/// <summary>Provides Core with fixed Lua execution through the active Plugin Client.</summary>
internal sealed class PluginFixedLuaExecutor(ICheatEngineClient client) : IFixedLuaExecutor
{
	public LuaJsonResult<T> Execute<T>(string operation, string source, JsonTypeInfo<T> resultType,
		LuaJsonBufferPool buffers, LuaOpaqueValueHandling opaque, CancellationToken cancellationToken)
	{
		return PluginLuaToolRuntime.ExecuteRaw(client, operation, source, resultType, buffers, opaque,
			cancellationToken);
	}
}
