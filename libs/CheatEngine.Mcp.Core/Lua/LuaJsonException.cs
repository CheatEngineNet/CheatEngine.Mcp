namespace CheatEngine.Mcp.Core.Lua;

/// <summary>Why a Lua value could not be copied as JSON.</summary>
internal enum LuaJsonViolation
{
	/// <summary>
	///     The value breaks the copy contract: a non-finite number, an opaque value, mixed or non-string keys, a cycle, a
	///     malformed <c>mcp_error</c>, or JSON that does not match the expected result type.
	/// </summary>
	Contract,

	/// <summary>The value exceeds a copy bound: string bytes, values, nested tables or JSON bytes.</summary>
	Limit
}

/// <summary>Reports a Lua value that could not be copied as JSON; the script that produced it has already run.</summary>
internal sealed class LuaJsonException : Exception
{
	/// <summary>Creates the exception.</summary>
	/// <param name="violation">The violated rule.</param>
	/// <param name="message">The diagnostic message.</param>
	/// <param name="innerException">The underlying exception, if any.</param>
	public LuaJsonException(LuaJsonViolation violation, string message, Exception? innerException = null)
		: base(message, innerException)
	{
		Violation = violation;
	}

	/// <summary>Gets the violated rule.</summary>
	public LuaJsonViolation Violation
	{
		get;
	}
}
