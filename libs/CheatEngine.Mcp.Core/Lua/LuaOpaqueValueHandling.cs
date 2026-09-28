namespace CheatEngine.Mcp.Core.Lua;

/// <summary>How <see cref="LuaJsonWriter" /> treats functions, userdata and threads.</summary>
internal enum LuaOpaqueValueHandling
{
	/// <summary>An opaque value breaks the contract: a fixed script must copy the documented fields instead.</summary>
	Reject,

	/// <summary>An opaque value is counted and dropped: <c>null</c> in an array, omitted from an object.</summary>
	/// <remarks>Only for caller-authored Lua, whose results the implementation does not control.</remarks>
	Drop
}
