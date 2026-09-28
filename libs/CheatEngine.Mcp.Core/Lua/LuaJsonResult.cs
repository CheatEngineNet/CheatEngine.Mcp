using System.Diagnostics.CodeAnalysis;

namespace CheatEngine.Mcp.Core.Lua;

/// <summary>A Lua result copied as JSON and deserialized, or the failure its script declared.</summary>
/// <typeparam name="T">The result type.</typeparam>
/// <param name="Value">The deserialized result; <see langword="default" /> when the script declared an error.</param>
/// <param name="Error">The declared <c>mcp_error</c>, or <see langword="null" /> for a result.</param>
/// <param name="DroppedOpaqueCount">The opaque values dropped under <see cref="LuaOpaqueValueHandling.Drop" />.</param>
internal readonly record struct LuaJsonResult<T>(T? Value, LuaScriptError? Error, int DroppedOpaqueCount)
{
	/// <summary>Gets whether the script declared an error instead of returning a result.</summary>
	[MemberNotNullWhen(true, nameof(Error))]
	[MemberNotNullWhen(false, nameof(Value))]
	public bool IsError => Error is not null;
}

/// <summary>A successfully copied fixed Lua value with its opaque-value accounting.</summary>
/// <typeparam name="T">The copied result type.</typeparam>
/// <param name="Value">The non-null copied result.</param>
/// <param name="DroppedOpaqueCount">Opaque values omitted while the caller explicitly requested dropping.</param>
internal readonly record struct LuaCopiedResult<T>(T Value, int DroppedOpaqueCount);
