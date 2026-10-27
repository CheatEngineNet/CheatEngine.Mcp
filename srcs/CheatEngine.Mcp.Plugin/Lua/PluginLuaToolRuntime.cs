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

	internal static bool ShowPluginStatus(ICheatEngineClient client, string state, string details, bool existingOnly,
		PluginLifecycleLuaScope? lifecycleScope = null)
	{
		ArgumentNullException.ThrowIfNull(client);
		if (!client.Dispatcher.IsMainThread)
		{
			throw new InvalidOperationException("The MCP status must be updated on Cheat Engine's main thread.");
		}
		if (state == "Disabled" && lifecycleScope?.IsDeactivationNotification(client) != true)
		{
			throw new InvalidOperationException("The disabled MCP status can only be reported during deactivation notification.");
		}

		PluginStatusLuaOperation operation = new("mcp_status",
			LuaToolRuntime.BuildSource(StatusUpdateSource, ["MCP: " + state, details, existingOnly]));
		if (!operation.TryExecuteInRuntime(out bool result, out CheatEngineFailure failure))
		{
			failure.Throw();
		}

		return result;
	}

	internal static bool IsLifecycleCleanupOperation(string operation)
	{
		return operation is "mcp_job_stop" or "mcp_state_release_resource" or "mcp_state_release"
			or CheatEngineToolNames.MonoDetach;
	}

	internal static LuaJsonResult<T> ExecuteLifecycleCleanup<T>(string operation, string source,
		JsonTypeInfo<T> resultType, LuaJsonBufferPool buffers, LuaOpaqueValueHandling opaque)
	{
		LuaJsonOperation<T> request = new(operation, source, resultType, buffers, opaque);
		if (request.TryExecuteInRuntime(out LuaJsonResult<T> result, out CheatEngineFailure failure))
		{
			return result;
		}

		failure.Throw();
		throw new InvalidOperationException("A failed Lua cleanup did not throw its recorded failure.");
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

	internal static bool TryAcquire(out LuaRuntimeOperation acquired, out CheatEngineFailure failure)
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
	internal static bool TryRun(LuaState state, string operation, string source, ref CheatEngineHostEffect effect,
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
			return TryExecuteInRuntime(out result, out failure);
		}

		internal bool TryExecuteInRuntime(out LuaJsonResult<T> result, out CheatEngineFailure failure)
		{
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

}
