namespace CheatEngine.Mcp.Core.Lua;

/// <summary>
///     The bounded result of an explicitly enabled caller-authored Lua chunk.  Its value is copied from Lua before the
///     dispatch completes; opaque Lua values are represented as null and counted in <see cref="DroppedOpaqueCount" />.
/// </summary>
/// <typeparam name="T">The source-generated result shape read by the fixed second stage.</typeparam>
/// <param name="Result">The fixed second stage's copied result.</param>
/// <param name="DroppedOpaqueCount">How many functions, userdata, light userdata or threads were dropped while copying.</param>
public readonly record struct LuaUnsafeExecution<T>(T Result, int DroppedOpaqueCount);
