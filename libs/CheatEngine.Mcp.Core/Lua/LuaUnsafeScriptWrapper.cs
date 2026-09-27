using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace CheatEngine.Mcp.Core.Lua;

/// <summary>Builds the two stages of <c>lua_execute</c> so that caller source is only ever data passed to <c>load</c>.</summary>
/// <remarks>
///     <para>
///         Stage A runs through the unsafe Client, because it runs caller-authored Lua. The caller source appears in it
///         only
///         inside a long-bracket string whose level the source cannot close, and the chunk name only as a separately
///         escaped
///         string literal. Stage A loads that string in text mode, runs it under <c>pcall</c> and stores the packed
///         outcome,
///         tagged with a per-call token, in the private global <see cref="ResultGlobal" />.
///     </para>
///     <para>
///         Stage B is the fixed protected body <see cref="ReadResult" />, run in the same dispatch with the token as its
///         only
///         argument. It takes the global and clears it before anything else, so no path leaves it behind, then reshapes
///         the
///         outcome for <see cref="LuaJsonWriter" /> under <see cref="LuaOpaqueValueHandling.Drop" />. The tool must run
///         stage
///         B even when stage A fails.
///     </para>
/// </remarks>
internal static class LuaUnsafeScriptWrapper
{
	internal const string ResultGlobal = "__cheatengine_mcp_lua_result";
	internal const string DefaultChunkName = "=lua_execute";
	internal const int MaximumSourceBytes = 1024 * 1024;
	internal const int MaximumChunkNameBytes = 256;

	/// <summary>
	///     The fixed stage-B body. Argument: <c>a[1]</c>, the token of the stage-A script. It returns
	///     <c>{ok=true, returnValues=table.pack(...)}</c>, <c>{ok=false, phase='compile'|'runtime', error}</c>, or an
	///     <c>mcp_error</c> when no result of this call exists. Only raw access touches caller values.
	/// </summary>
	internal const string ReadResult = """
	                                   local result = rawget(_ENV, '__cheatengine_mcp_lua_result')
	                                   rawset(_ENV, '__cheatengine_mcp_lua_result', nil)
	                                   if type(result) ~= 'table' or rawget(result, 'token') ~= a[1] then
	                                   	return {mcp_error = {kind = 'internal', hostEffect = 'unknown',
	                                   		message = 'The Lua execution left no result for this request; its effects are unknown.'}}
	                                   end
	                                   if rawget(result, 'phase') == 'compile' then
	                                   	return {ok = false, phase = 'compile', error = rawget(result, 'error')}
	                                   end
	                                   local outcome = rawget(result, 'outcome')
	                                   local count = rawget(outcome, 'n')
	                                   if rawget(outcome, 1) ~= true then
	                                   	local failure = rawget(outcome, 2)
	                                   	return {ok = false, phase = 'runtime',
	                                   		error = type(failure) == 'string' and failure or ('Lua raised a ' .. type(failure) .. ' error value.')}
	                                   end
	                                   local values = {n = count - 1}
	                                   for index = 2, count do values[index - 1] = rawget(outcome, index) end
	                                   return {ok = true, returnValues = values}
	                                   """;

	private static readonly UTF8Encoding StrictUtf8 = new(false, true);

	/// <summary>Builds the stage-A script for caller source.</summary>
	/// <param name="source">The caller's Lua source; it is never interpolated as code.</param>
	/// <param name="chunkName">
	///     The chunk name for Lua diagnostics, following Lua's rules (<c>=name</c> verbatim, <c>@file</c>, or a quoted
	///     string); <see cref="DefaultChunkName" /> when <see langword="null" />.
	/// </param>
	/// <returns>The stage-A source and the token that stage B must receive as <c>a[1]</c>.</returns>
	/// <exception cref="ArgumentException">The source or chunk name is not valid UTF-16 text or exceeds its bound.</exception>
	internal static WrappedScript Build(string source, string? chunkName)
	{
		ArgumentNullException.ThrowIfNull(source);
		int sourceBytes = CountUtf8(source, nameof(source));
		if (sourceBytes > MaximumSourceBytes)
		{
			throw new ArgumentException($"Lua source exceeds {MaximumSourceBytes} UTF-8 bytes.", nameof(source));
		}

		string name = chunkName ?? DefaultChunkName;
		if (name.Length == 0 || CountUtf8(name, nameof(chunkName)) > MaximumChunkNameBytes)
		{
			throw new ArgumentException($"A chunk name must be 1 to {MaximumChunkNameBytes} UTF-8 bytes.",
				nameof(chunkName));
		}

		string token = RandomNumberGenerator.GetHexString(32, true);
		string level = new('=', SelectLevel(source));
		// Lua skips one line break directly after the opening bracket, so the source keeps its first line. The break
		// repeats the source's first character when that is '\r': Lua reads "\n\r" (not "\r\r") as one break.
		char opening = source.StartsWith('\r') ? '\r' : '\n';
		StringBuilder script = new(sourceBytes + 1024);
		script.Append("local __rawset, __pack, __pcall, __load, __env = rawset, table.pack, pcall, load, _ENV\n")
			.Append("__rawset(__env, '").Append(ResultGlobal).Append("', nil)\n")
			.Append("local __chunk, __error = __load([").Append(level).Append('[').Append(opening)
			.Append(source)
			.Append(']').Append(level).Append("], ");
		AppendStringLiteral(script, name);
		script.Append(", 't')\n")
			.Append("if __chunk == nil then\n")
			.Append("\t__rawset(__env, '").Append(ResultGlobal).Append("', {token = '").Append(token)
			.Append("', phase = 'compile', error = __error})\n")
			.Append("else\n")
			.Append("\t__rawset(__env, '").Append(ResultGlobal).Append("', {token = '").Append(token)
			.Append("', phase = 'runtime', outcome = __pack(__pcall(__chunk))})\n")
			.Append("end\n");
		return new WrappedScript(script.ToString(), token);
	}

	/// <summary>Finds the smallest long-bracket level whose closing bracket the source cannot produce.</summary>
	/// <remarks>
	///     Level <c>k</c> is unusable when <c>]</c>, <c>k</c> equals signs and <c>]</c> occur in the source, and also when
	///     the source ends with <c>]</c> and <c>k</c> equals signs: the closing bracket's own <c>]</c> would complete it.
	/// </remarks>
	/// <param name="source">The caller's source.</param>
	/// <returns>The number of equals signs to use.</returns>
	internal static int SelectLevel(string source)
	{
		ArgumentNullException.ThrowIfNull(source);
		HashSet<int> unusable = [];
		for (int index = source.IndexOf(']'); index >= 0; index = source.IndexOf(']', index + 1))
		{
			int end = index + 1;
			while (end < source.Length && source[end] == '=')
			{
				end++;
			}

			if (end == source.Length || source[end] == ']')
			{
				unusable.Add(end - index - 1);
			}
		}

		int level = 0;
		while (unusable.Contains(level))
		{
			level++;
		}

		return level;
	}

	private static int CountUtf8(string text, string parameterName)
	{
		try
		{
			return StrictUtf8.GetByteCount(text);
		}
		catch (EncoderFallbackException exception)
		{
			throw new ArgumentException("The text contains an unpaired UTF-16 surrogate.", parameterName, exception);
		}
	}

	// Same encoding as LuaToolRuntime.BuildSource: printable ASCII except quote and backslash stays literal; every
	// other UTF-8 byte becomes a decimal escape, so no byte of the text can end the literal or the line.
	private static void AppendStringLiteral(StringBuilder script, string text)
	{
		script.Append('"');
		foreach (byte character in Encoding.UTF8.GetBytes(text))
		{
			if (character is >= 32 and <= 126 and not (byte) '"' and not (byte) '\\')
			{
				script.Append((char) character);
			}
			else
			{
				script.Append('\\').Append(character.ToString("D3", CultureInfo.InvariantCulture));
			}
		}

		script.Append('"');
	}

	/// <summary>A stage-A script and the token of the result it stores.</summary>
	/// <param name="Source">The stage-A Lua source for the unsafe Client.</param>
	/// <param name="Token">The per-call token that stage B receives as <c>a[1]</c>.</param>
	internal readonly record struct WrappedScript(string Source, string Token);
}
