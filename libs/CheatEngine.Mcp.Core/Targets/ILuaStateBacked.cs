namespace CheatEngine.Mcp.Core.Targets;

/// <summary>A tracked resource whose state lives in the Lua state root under the activation's namespace.</summary>
internal interface ILuaStateBacked
{
	/// <summary>The id of its Lua entry.</summary>
	internal string StateId
	{
		get;
	}

	/// <summary>Updates the handle from a snapshot of the Lua state root.</summary>
	/// <param name="entry">Its Lua entry, or <see langword="null" /> when the entry no longer exists.</param>
	internal void Observe(LuaStateEntry? entry);
}
