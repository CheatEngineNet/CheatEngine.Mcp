namespace CheatEngine.Mcp.Core.Lua;

/// <summary>
///     The Core-owned helpers every v2 fixed script can use, emitted by <see cref="LuaToolRuntime" /> as the local table
///     <c>mcp</c> after the literal <c>k</c> (runtime values) and <c>a</c> (arguments) tables. Being local, it leaves no
///     global state behind.
/// </summary>
/// <remarks>
///     <list type="bullet">
///         <item>
///             <c>mcp.hex(v)</c>: an integer as uppercase hexadecimal without <c>0x</c>, the unsigned 64-bit view of a
///             negative one; <c>nil</c> stays <c>nil</c>.
///         </item>
///         <item>
///             <c>mcp.bytes(t, n)</c>: the first <c>n</c> bytes (all by default) of a byte table or string as
///             <c>"48 8B 05"</c>; <c>nil</c> stays <c>nil</c>.
///         </item>
///         <item>
///             <c>mcp.num(x)</c>: a number, with NaN and the infinities as <c>"NaN"</c>, <c>"Infinity"</c>,
///             <c>"-Infinity"</c>.
///         </item>
///         <item>
///             <c>mcp.err(kind, message, hostEffect, hint)</c>: the <c>mcp_error</c> result that declares a failure. An
///             omitted host effect is reported as <c>unknown</c>; a script claims <c>not_started</c> only explicitly,
///             before its first effect.
///         </item>
///         <item>
///             <c>mcp.expired()</c>: whether the script has used its dispatch budget (<c>k.budgetMs</c>), so enumerations
///             can stop cooperatively and report where to resume. It is always <see langword="false" /> where Cheat
///             Engine's <c>getTickCount</c> is missing.
///         </item>
///     </list>
///     <para>The prelude is one line, so a fixed body's line numbers in Lua errors are offset by exactly one.</para>
/// </remarks>
internal static class LuaPrelude
{
	/// <summary>The prelude source: one line, no comments, ending with an empty statement.</summary>
	internal const string Source =
		"local mcp = {} " +
		"function mcp.hex(v) if v == nil then return nil end return string.format('%X', v) end " +
		"function mcp.bytes(t, n) if t == nil then return nil end " +
		"local text = type(t) == 'string' local parts = {} " +
		"for i = 1, n or #t do local b if text then b = string.byte(t, i) else b = t[i] end " +
		"parts[i] = string.format('%02X', b & 255) end return table.concat(parts, ' ') end " +
		"function mcp.num(x) if x ~= x then return 'NaN' elseif x == math.huge then return 'Infinity' " +
		"elseif x == -math.huge then return '-Infinity' end return x end " +
		"function mcp.err(kind, message, hostEffect, hint) " +
		"return {mcp_error = {kind = kind, message = message, hostEffect = hostEffect, hint = hint}} end " +
		"local mcpStarted = type(getTickCount) == 'function' and getTickCount() or nil " +
		"function mcp.expired() if mcpStarted == nil then return false end " +
		"return (getTickCount() - mcpStarted) % 4294967296 >= k.budgetMs end;";
}
