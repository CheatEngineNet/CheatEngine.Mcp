namespace CheatEngine.Mcp.Core.Lua;

/// <summary>A failure that a fixed Lua script declared through its top-level <c>mcp_error</c> table.</summary>
/// <remarks>
///     The script ran and chose to report this failure instead of a result; nothing of the result is copied. The tool
///     contract's exception type replaces this record when the v2 error envelope lands.
/// </remarks>
/// <param name="Kind">The declared snake_case error kind, such as <c>not_found</c>.</param>
/// <param name="Message">The declared message, bounded to <see cref="LuaToolRuntime.MaximumErrorFieldBytes" />.</param>
/// <param name="HostEffect">The declared snake_case host effect, or <see langword="null" /> when none was stated.</param>
/// <param name="Hint">An optional recovery hint, bounded like the message.</param>
internal sealed record LuaScriptError(string Kind, string Message, string? HostEffect, string? Hint);
