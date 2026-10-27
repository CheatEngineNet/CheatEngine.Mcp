using System.Text.Json.Serialization.Metadata;

using CheatEngine.Client;
using CheatEngine.Client.Lua;
using CheatEngine.Client.Results;
using CheatEngine.Mcp.Core.Contract;

namespace CheatEngine.Mcp.Plugin.Lua;

/// <summary>Provides Core with fixed Lua execution through the active Plugin Client.</summary>
internal sealed class PluginFixedLuaExecutor(ICheatEngineClient client,
	PluginLifecycleLuaScope? lifecycleLuaScope = null)
	: IFixedLuaExecutor
{
	public LuaJsonResult<T> Execute<T>(string operation, string source, JsonTypeInfo<T> resultType,
		LuaJsonBufferPool buffers, LuaOpaqueValueHandling opaque, CancellationToken cancellationToken)
	{
		if (!cancellationToken.CanBeCanceled && lifecycleLuaScope?.IsResourceCleanup(client) == true
			&& PluginLuaToolRuntime.IsLifecycleCleanupOperation(operation))
		{
			return PluginLuaToolRuntime.ExecuteLifecycleCleanup(operation, source, resultType, buffers, opaque);
		}

		return PluginLuaToolRuntime.ExecuteRaw(client, operation, source, resultType, buffers, opaque,
			cancellationToken);
	}
}
