using CheatEngine.Client.Lua;
using CheatEngine.Client.Results;
using CheatEngine.SDK.Lua.Calls;
using CheatEngine.SDK.Lua.Runtime;
using CheatEngine.SDK.Lua.State;

namespace CheatEngine.Mcp.Plugin.Lua;

/// <summary>Runs the fixed status script and copies its deliberately narrow Boolean result.</summary>
internal sealed record PluginStatusLuaOperation(string Operation, string Source) : ILuaOperation<bool>
{
	public bool TryExecute(ILuaExecutionContext context, out bool result, out CheatEngineFailure failure)
	{
		context.ThrowIfExpired();
		return TryExecuteInRuntime(out result, out failure);
	}

	internal bool TryExecuteInRuntime(out bool result, out CheatEngineFailure failure)
	{
		result = false;
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
			result = state.TypeOf(-1) == LuaType.Boolean && state.ToBoolean(-1);
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
