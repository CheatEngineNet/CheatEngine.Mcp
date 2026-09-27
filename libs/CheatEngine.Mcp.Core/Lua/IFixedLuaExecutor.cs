using System.Text.Json.Serialization.Metadata;

namespace CheatEngine.Mcp.Core.Lua;

/// <summary>Executes one implementation-owned Lua body through the active Client without exposing SDK state to Core.</summary>
/// <remarks>
///     <para>
///         Core owns script construction, result bounds and MCP error mapping.  The Plugin owns the implementation
///         because it is the only composition root permitted to access the SDK's protected Lua state.
///     </para>
/// </remarks>
internal interface IFixedLuaExecutor
{
	/// <summary>Runs a prebuilt fixed Lua source and returns its copied result or declared script failure.</summary>
	/// <typeparam name="T">The source-generated result type.</typeparam>
	/// <param name="operation">The stable MCP operation name.</param>
	/// <param name="source">The complete fixed Lua source.</param>
	/// <param name="resultType">Source-generated metadata for <typeparamref name="T" />.</param>
	/// <param name="buffers">The activation-owned JSON buffer pool.</param>
	/// <param name="opaque">How opaque Lua values are handled while copying.</param>
	/// <param name="cancellationToken">The token observed by Client before Lua admission.</param>
	/// <returns>The copied result or a declared script failure.</returns>
	public LuaJsonResult<T> Execute<T>(string operation, string source, JsonTypeInfo<T> resultType,
		LuaJsonBufferPool buffers, LuaOpaqueValueHandling opaque, CancellationToken cancellationToken);
}

/// <summary>Reports a composition error when a live backend resolves Lua dispatch without its Plugin bridge.</summary>
internal sealed class UnavailableFixedLuaExecutor : IFixedLuaExecutor
{
	internal static UnavailableFixedLuaExecutor Instance
	{
		get;
	} = new();

	public LuaJsonResult<T> Execute<T>(string operation, string source, JsonTypeInfo<T> resultType,
		LuaJsonBufferPool buffers, LuaOpaqueValueHandling opaque, CancellationToken cancellationToken)
	{
		throw new InvalidOperationException(
			"Fixed Lua execution requires the CheatEngine.Mcp.Plugin composition bridge for the active Cheat Engine client.");
	}
}

