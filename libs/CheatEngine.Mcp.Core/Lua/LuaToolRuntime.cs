using System.Collections;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

using CheatEngine.Client;
using CheatEngine.Client.Lua;
using CheatEngine.Client.Results;
using CheatEngine.Mcp.Core.Contract;
using CheatEngine.Mcp.Core.Execution;
using CheatEngine.SDK.Lua.Calls;
using CheatEngine.SDK.Lua.Runtime;
using CheatEngine.SDK.Lua.State;

namespace CheatEngine.Mcp.Core.Lua;

/// <summary>Confines protected Lua access to a Client-dispatched operation and copies bounded results.</summary>
/// <remarks>
///     <para>
///         The v2 path (<see cref="Execute{T}" />, reached by tools through <see cref="ToolDispatch" />) builds
///         <c>local k = {...}; local a = {...}; local mcp = ...</c> ahead of a fixed body, copies the result through
///         <see cref="LuaJsonWriter" /> into a source-generated type, and reports failures as
///         <see cref="CheatEngineToolException" />. The object-returning members serve the legacy tools until they
///         migrate.
///     </para>
/// </remarks>
public static class LuaToolRuntime
{
	internal const int MaximumStringBytes = 4 * 1024 * 1024;
	internal const int MaximumItems = 65536;

	/// <summary>The dispatch budget written into <c>k.budgetMs</c> when no configured budget is at hand.</summary>
	internal const int DefaultBudgetMilliseconds = 100;

	internal const string ContractViolationMessage =
		"A fixed Lua script returned a result that breaks its contract; the script has already run.";

	/// <summary>The only fixed script allowed on the SDK main-thread UI path: create or update the MCP status item.</summary>
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

	/// <summary>Transition only: runs a fixed Lua body through Client dispatch and wraps its bounded result in band.</summary>
	/// <param name="client">The activation's Client.</param>
	/// <param name="operation">A diagnostic operation name.</param>
	/// <param name="body">The fixed Lua body; caller data is passed only through <paramref name="arguments" />.</param>
	/// <param name="arguments">Values encoded into the <c>a</c> table.</param>
	/// <returns>An object with <c>success</c> and <c>result</c>, or a failure result.</returns>
	public static object Invoke(ICheatEngineClient client, string operation, string body, params object?[] arguments)
	{
		return ToolExecution.Run(client, () =>
		{
			object? result = Execute(client, operation, body, arguments);
			return new
			{
				success = true,
				result
			};
		});
	}

	/// <summary>Transition only: runs a fixed Lua body inside the current Client dispatch and returns the bounded copy.</summary>
	/// <param name="client">The activation's Client.</param>
	/// <param name="operation">A diagnostic operation name.</param>
	/// <param name="body">The fixed Lua body; caller data is passed only through <paramref name="arguments" />.</param>
	/// <param name="arguments">Values encoded into the <c>a</c> table.</param>
	/// <returns>The copied Lua result.</returns>
	public static object? Execute(ICheatEngineClient client, string operation, string body, params object?[] arguments)
	{
		LuaToolOperation request = new(operation, BuildSource(body, arguments));
		return client.Lua.Execute<LuaToolOperation, object?>(request, client.Stopping);
	}

	/// <summary>Updates the plugin's status menu item on Cheat Engine's main thread during plugin lifecycle callbacks.</summary>
	/// <remarks>This is the only SDK main-thread UI exception; tools and target work must never use it.</remarks>
	/// <param name="client">The activation's Client, used only to verify the calling thread.</param>
	/// <param name="state">The caption state.</param>
	/// <param name="details">The message shown when the item is clicked.</param>
	/// <param name="existingOnly">Only update an existing item; never create one.</param>
	/// <returns><see langword="true" /> when the item shows the requested caption.</returns>
	public static bool ShowPluginStatus(ICheatEngineClient client, string state, string details, bool existingOnly)
	{
		if (!client.Dispatcher.IsMainThread)
		{
			throw new InvalidOperationException("The MCP status must be updated on Cheat Engine's main thread.");
		}

		// Client 1.0 has no UI API. SDK dispatch opens after OnEnable returns and closes before
		// OnDisable, but protected SDK Lua admission spans both callbacks (SDK 2.0.0).
		// Keep this exception limited to the fixed, main-thread status UI; tools still use Client.
		LuaToolOperation operation = new("mcp_status",
			BuildSource(StatusUpdateSource, ["MCP: " + state, details, existingOnly]));
		if (!operation.TryExecuteInRuntime(out object? result, out CheatEngineFailure failure))
		{
			failure.Throw();
		}

		return result is true;
	}

	/// <summary>
	///     Runs a fixed, implementation-owned body inside the current Client dispatch and deserializes its bounded result.
	///     Tools reach it through <see cref="ToolDispatch" />, which applies the feature choke point first.
	/// </summary>
	/// <remarks>
	///     A result table with a top-level <c>mcp_error</c> becomes <see cref="CheatEngineToolException" /> with the declared
	///     kind, message and hint; its host effect is <c>unknown</c> unless the script states one. A result that breaks the
	///     copy contract is <c>internal</c> and one that exceeds a copy bound is <c>limit_exceeded</c>, both with
	///     <c>completed</c>, because the script has already run. A Lua error is a Client failure (<c>host_refused</c>).
	/// </remarks>
	/// <typeparam name="T">The result type.</typeparam>
	/// <param name="client">The activation's Client.</param>
	/// <param name="operation">A diagnostic operation name, also reported as the error's operation.</param>
	/// <param name="body">The fixed Lua body; caller data is passed only through <paramref name="arguments" />.</param>
	/// <param name="resultType">The source-generated metadata of <typeparamref name="T" />.</param>
	/// <param name="cancellationToken">The token observed before the Lua operation's admission.</param>
	/// <param name="arguments">Values encoded into the <c>a</c> table.</param>
	/// <returns>The deserialized result.</returns>
	internal static T Execute<T>(ICheatEngineClient client, string operation, string body, JsonTypeInfo<T> resultType,
		CancellationToken cancellationToken, params ReadOnlySpan<object?> arguments)
	{
		string source = BuildSource(body, DefaultBudgetMilliseconds, arguments);
		return ExecuteSource(client, operation, source, resultType, new LuaJsonBufferPool(), cancellationToken);
	}

	/// <summary>Runs a script built by <see cref="BuildSource(string, int, ReadOnlySpan{object?})" /> in the current dispatch.</summary>
	/// <typeparam name="T">The result type.</typeparam>
	/// <param name="client">The activation's Client.</param>
	/// <param name="operation">A diagnostic operation name, also reported as the error's operation.</param>
	/// <param name="source">The complete script.</param>
	/// <param name="resultType">The source-generated metadata of <typeparamref name="T" />.</param>
	/// <param name="buffers">The caller's JSON buffer pool.</param>
	/// <param name="cancellationToken">The token observed before the Lua operation's admission.</param>
	/// <returns>The deserialized result.</returns>
	internal static T ExecuteSource<T>(ICheatEngineClient client, string operation, string source,
		JsonTypeInfo<T> resultType, LuaJsonBufferPool buffers, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(client);
		ArgumentException.ThrowIfNullOrWhiteSpace(operation);
		ArgumentNullException.ThrowIfNull(source);
		ArgumentNullException.ThrowIfNull(resultType);
		ArgumentNullException.ThrowIfNull(buffers);
		LuaJsonOperation<T> request = new(operation, source, resultType, buffers);
		LuaJsonResult<T> result;
		try
		{
			result = client.Lua.Execute<LuaJsonOperation<T>, LuaJsonResult<T>>(request, cancellationToken);
		}
		catch (LuaJsonException exception)
		{
			throw exception.Violation is LuaJsonViolation.Limit
				? new CheatEngineToolException(new ToolError(ToolErrorKind.LimitExceeded, exception.Message, operation,
					ToolHostEffect.Completed, false, ToolFailureMapping.LimitHint), exception)
				: new CheatEngineToolException(new ToolError(ToolErrorKind.Internal, ContractViolationMessage,
					operation, ToolHostEffect.Completed, false, CheatEngineToolException.InternalHint), exception);
		}

		return result.IsError ? throw ScriptError(operation, result.Error) : result.Value;
	}

	/// <summary>Maps a failure a fixed script declared through <c>mcp_error</c> to the contract.</summary>
	/// <param name="operation">The Lua operation, reported as the error's operation.</param>
	/// <param name="error">The declared failure.</param>
	/// <returns>The exception to throw.</returns>
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
	///     then the fixed body. Call it before dispatch, off Cheat Engine's main thread.
	/// </summary>
	/// <param name="body">The fixed body.</param>
	/// <param name="budgetMilliseconds">The dispatch budget, exposed as <c>k.budgetMs</c> for <c>mcp.expired()</c>.</param>
	/// <param name="arguments">Values encoded into the <c>a</c> table.</param>
	/// <returns>The script.</returns>
	/// <exception cref="ArgumentException">An argument has an unsupported type or exceeds a materialization bound.</exception>
	internal static string BuildSource(string body, int budgetMilliseconds, ReadOnlySpan<object?> arguments)
	{
		ArgumentNullException.ThrowIfNull(body);
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(budgetMilliseconds);
		StringBuilder source = new("local k = { budgetMs = ");
		source.Append(budgetMilliseconds.ToString(CultureInfo.InvariantCulture)).Append(" }; ");
		AppendArguments(source, arguments);
		return source.Append(" }; ").Append(LuaPrelude.Source).Append('\n').Append(body).ToString();
	}

	internal static object Call(ICheatEngineClient client, string function, params object?[] arguments)
	{
		// Only implementation-owned global names are accepted here, never Lua expressions supplied by callers.
		if (string.IsNullOrEmpty(function) ||
			function.Any(character => !char.IsAsciiLetterOrDigit(character) && character != '_'))
		{
			return ToolExecution.Error("Invalid Lua function name.");
		}

		return Invoke(client, function,
			$"local f = _G['{function}']; assert(type(f) == 'function', 'Required Cheat Engine API is unavailable: {function}'); return table.pack(f(table.unpack(a, 1, a.n)))",
			arguments);
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

	/// <summary>Takes the SDK's protected Lua admission for the current thread.</summary>
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
		LuaJsonBufferPool Buffers) : ILuaOperation<LuaJsonResult<T>>
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

			result = LuaJsonWriter.Read(state, -1, ResultType, Buffers);
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
			if (++_items > MaximumItems || depth > 16)
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
					if (_bytes > MaximumStringBytes)
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
			bool packed = values.TryGetValue("n", out object? count) && count is long length &&
						  length is >= 0 and <= MaximumItems && numericKeys == values.Count - 1;
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
