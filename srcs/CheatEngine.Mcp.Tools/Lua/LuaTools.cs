using System.ComponentModel;
using System.Text;

using CheatEngine.Client.Lua;
using CheatEngine.Mcp.Core.Contract;
using CheatEngine.Mcp.Core.Features;

using ModelContextProtocol.Server;

namespace CheatEngine.Mcp.Tools.Lua;

/// <summary>The <c>lua_*</c> tools: bounded unsafe execution and lookup in the installed Lua API reference.</summary>
[McpServerToolType]
public sealed class LuaTools
{
	/// <summary>The most UTF-8 bytes accepted in one API-reference query.</summary>
	internal const int MaximumQueryBytes = 256;

	/// <summary>The most matches one API-reference page returns.</summary>
	internal const int MaximumApiPageSize = 100;

	/// <summary>The furthest matching-line page offset accepted by the fixed script.</summary>
	internal const int MaximumApiOffset = LuaScripts.MaximumReferenceLines;

	private static readonly UTF8Encoding StrictUtf8 = new(false, true);
	private readonly ToolDispatch _dispatch;
	private readonly IUnsafeLuaClient? _unsafeLua;

	/// <summary>Creates the Lua tools; the constructor does no Cheat Engine work.</summary>
	/// <param name="dispatch">The activation's dispatch facade.</param>
	/// <param name="unsafeLua">The Client capability that runs caller-authored Lua when the feature switch enables it.</param>
	public LuaTools(ToolDispatch dispatch, IUnsafeLuaClient? unsafeLua = null)
	{
		ArgumentNullException.ThrowIfNull(dispatch);
		_dispatch = dispatch;
		_unsafeLua = unsafeLua;
	}

	/// <summary>Runs one caller-authored Lua chunk and copies its bounded return values.</summary>
	[McpServerTool(Name = CheatEngineToolNames.LuaExecute, Title = "Execute Lua", ReadOnly = false,
		Destructive = true, Idempotent = false, OpenWorld = true, UseStructuredContent = true)]
	[McpMeta(McpDispatchClass.MetaKey, McpDispatchClass.MayPrompt)]
	[RequiresFeature(McpFeature.UnsafeLua)]
	[Description(
		"Execute caller-authored Lua through Cheat Engine's unsafe Lua capability. source is compiled as text and runs synchronously on Cheat Engine's main thread, so it can freeze Cheat Engine, change the target or host, access files and network, create state that MCP cannot track, or show a dialog that waits for a person. Returned values are copied with bounds: functions, userdata and CE objects are dropped. Use a dedicated tool whenever one exists; this tool requires Mcp:EnableUnsafeLua.")]
	public LuaExecuteResult Execute(
		[Description("Lua 5.3 source, up to 1048576 UTF-8 bytes. It runs with Cheat Engine's full Lua API.")]
		string source,
		[Description("An optional non-empty Lua chunk name for diagnostics, up to 256 UTF-8 bytes.")]
		string? chunkName = null,
		CancellationToken cancellationToken = default)
	{
		if (_unsafeLua is null)
		{
			_dispatch.Features.Require(McpFeature.UnsafeLua, CheatEngineToolNames.LuaExecute);
			throw CheatEngineToolException.Unsupported(
				"The active Cheat Engine Client does not expose unsafe Lua execution.",
				CheatEngineToolNames.LuaExecute);
		}

		LuaUnsafeExecution<LuaExecuteOutcome> execution = _dispatch.RunUnsafeLua(CheatEngineToolNames.LuaExecute,
			_unsafeLua, source, chunkName, LuaUnsafeJsonContext.Default.LuaExecuteOutcome, cancellationToken);
		LuaExecuteOutcome outcome = execution.Result;
		ToolHostEffect effect = outcome.Ok
			? ToolHostEffect.Completed
			: outcome.Phase is "compile"
				? ToolHostEffect.NotApplied
				: ToolHostEffect.Unknown;
		return new LuaExecuteResult(outcome.Ok, outcome.Phase, outcome.Error, effect, outcome.ReturnValues,
			execution.DroppedOpaqueCount);
	}

	/// <summary>Searches the Lua API reference installed with the current Cheat Engine instance.</summary>
	[McpServerTool(Name = CheatEngineToolNames.LuaFindApi, Title = "Find Lua API", ReadOnly = true,
		Destructive = false, Idempotent = true, OpenWorld = true, UseStructuredContent = true)]
	[McpMeta(McpDispatchClass.MetaKey, McpDispatchClass.Short)]
	[Description(
		"Search the celua.txt Lua API reference in this Cheat Engine installation, case-insensitively. Returns matching lines with their source line numbers, at most 100 per page. The file is read only from Cheat Engine's own installation directory, is never returned in full and is bounded to 4 MiB and 65536 lines; a source-file bound returns the bounded prefix with truncated true and no nextOffset. Use a specific function or class name and page with nextOffset.")]
	public LuaApiSearchResult FindApi(
		[Description(
			"A case-insensitive Lua function, class or API term of 1 to 256 UTF-8 bytes, such as getAddressSafe.")]
		string query,
		[Description("The number of matching lines to return, from 1 to 100.")]
		int limit = 20,
		[Description("The zero-based offset among matching lines from a previous nextOffset.")]
		int offset = 0,
		CancellationToken cancellationToken = default)
	{
		ValidateQuery(query);
		if (limit is < 1 or > MaximumApiPageSize)
		{
			throw CheatEngineToolException.LimitExceeded("limit",
				$"must be from 1 to {MaximumApiPageSize}.");
		}

		if (offset is < 0 or > MaximumApiOffset)
		{
			throw CheatEngineToolException.LimitExceeded("offset",
				$"must be from 0 to {MaximumApiOffset}.");
		}

		return _dispatch.RunLua(CheatEngineToolNames.LuaFindApi, LuaScripts.FindApi,
			LuaJsonContext.Default.LuaApiSearchResult, cancellationToken, query, limit, offset);
	}

	private static void ValidateQuery(string? query)
	{
		if (string.IsNullOrWhiteSpace(query) || query.Any(char.IsControl))
		{
			throw CheatEngineToolException.InvalidArgument("query",
				$"must be 1 to {MaximumQueryBytes} UTF-8 bytes without control characters.");
		}

		try
		{
			if (StrictUtf8.GetByteCount(query) > MaximumQueryBytes)
			{
				throw CheatEngineToolException.LimitExceeded("query",
					$"must be at most {MaximumQueryBytes} UTF-8 bytes.");
			}
		}
		catch (EncoderFallbackException)
		{
			throw CheatEngineToolException.InvalidArgument("query",
				"must not contain an unpaired UTF-16 surrogate.", "Use valid UTF-8 text.");
		}
	}
}
