using System.Buffers;

using CheatEngine.Mcp.Core.Contract;
using CheatEngine.Mcp.Core.Features;

namespace CheatEngine.Mcp.Tools.Asm;

/// <summary>
///     Keeps <c>asm_check</c> read-only by refusing, before Cheat Engine sees it, a script whose check would run code
///     or change the target.
/// </summary>
/// <remarks>
///     <para>
///         Cheat Engine checks a script by running its preprocessing and first pass, and stops only at
///         <c>if syntaxcheckonly then exit</c> (<c>autoassembler.pas</c> <c>autoassemble2</c>). Before that point
///         <c>{$lua}</c> blocks run, <c>{$luacode}</c> and <c>loadlibrary</c> inject a library, <c>{$c}</c> compiles C,
///         Lua-registered commands and <c>luacall</c> run Lua, <c>include</c> reads another file whose commands then run,
///         <c>globalalloc</c> allocates target memory and registers its symbol, and address operands are resolved by the
///         symbol handler, which evaluates a <c>$</c> token as Lua (<c>symbolhandler.pas</c> <c>getAddressFromName</c>).
///     </para>
///     <para>
///         Directives are expanded, and the shipped <c>PREPARECHEADER</c> prologue reads every line, before comments are
///         removed, so directives and that prologue's trigger lines are searched in the raw text. Commands are matched
///         only after <c>removecomments</c>, so command names, <c>globalalloc</c> and <c>$</c> tokens are searched in the
///         text with comments blanked. Brace comments are comments only on x86 and x64 targets, so the text is blanked
///         twice, with and without them, and a construct found in either is refused.
///     </para>
/// </remarks>
internal static class AsmCheckScreen
{
	private static readonly SearchValues<char> HexDigits = SearchValues.Create("0123456789ABCDEFabcdef");

	/// <summary>
	///     Refuses a script whose check would run code or change the target, and returns what its check needs.
	/// </summary>
	/// <param name="script">The whole script, exactly as Cheat Engine will receive it.</param>
	/// <returns>The switches the check needs, which are only <see cref="McpFeature.AutoAssembler" />.</returns>
	/// <exception cref="CheatEngineToolException">The script runs code or changes the target while it is checked.</exception>
	internal static McpFeatureRequirements Screen(string script)
	{
		ArgumentNullException.ThrowIfNull(script);
		RefuseGated(AutoAssemblerScriptClassifier.Classify(RawTriggers(script)));
		string code = BlankComments(script, true);
		string armCode = BlankComments(script, false);
		McpFeatureRequirements requirements = AutoAssemblerScriptClassifier.Classify(code);
		RefuseGated(requirements);
		RefuseGated(AutoAssemblerScriptClassifier.Classify(armCode));
		if (ContainsToken(code, "globalalloc") || ContainsToken(armCode, "globalalloc"))
		{
			throw RunsWhileChecked(
				"the script uses globalalloc, which allocates target memory and registers its symbol even during a check");
		}

		if (HasLuaSymbol(code) || HasLuaSymbol(armCode))
		{
			throw RunsWhileChecked(
				"the script writes $ before something other than hexadecimal digits, which Cheat Engine's symbol handler evaluates as Lua");
		}

		return requirements;
	}

	/// <summary>
	///     Blanks comments the way <c>autoassembler.pas</c> <c>removecomments</c> does, keeping every line break and
	///     every other character in place: <c>//</c> ends a line, <c>/*</c> runs to <c>*/</c> and, when
	///     <paramref name="braceComments" /> is set, <c>{</c> runs to <c>}</c>, both across lines; a <c>'</c> toggles a
	///     string that hides comment markers until the line ends.
	/// </summary>
	/// <param name="script">The script.</param>
	/// <param name="braceComments">Whether <c>{</c> starts a comment, as it does for x86 and x64 targets.</param>
	/// <returns>The script with every comment character replaced by a space.</returns>
	internal static string BlankComments(string script, bool braceComments)
	{
		ArgumentNullException.ThrowIfNull(script);
		char[] text = script.ToCharArray();
		bool inComment = false;
		bool brace = false;
		bool inString = false;
		bool lineComment = false;
		for (int index = 0; index < text.Length; index++)
		{
			char character = text[index];
			if (character is '\r' or '\n')
			{
				inString = false;
				lineComment = false;
				continue;
			}

			if (lineComment)
			{
				text[index] = ' ';
				continue;
			}

			if (inComment)
			{
				if (brace ? character == '}' : character == '*' && Next(text, index) == '/')
				{
					inComment = false;
					if (!brace)
					{
						text[index + 1] = ' ';
					}
				}

				text[index] = ' ';
				continue;
			}

			if (character == '\'')
			{
				inString = !inString;
			}

			if (inString)
			{
				continue;
			}

			if (character == '/' && Next(text, index) == '/')
			{
				lineComment = true;
				text[index] = ' ';
			}
			else if ((braceComments && character == '{') || (character == '/' && Next(text, index) == '*'))
			{
				inComment = true;
				brace = character == '{';
				text[index] = ' ';
			}
		}

		return new string(text);
	}

	/// <summary>
	///     Whether <paramref name="code" /> holds a <c>$</c> that Cheat Engine's symbol handler would evaluate as Lua:
	///     one followed by anything but hexadecimal digits, up to the next separator of the symbol handler's tokenizer
	///     (<c>" [ ] + - * ( )</c>), a comma or the line end; trailing spaces and the <c>:</c> of an address line are
	///     ignored. A bare <c>$</c> or <c>$</c> before hexadecimal digits is a number.
	/// </summary>
	/// <param name="code">The script with comments blanked.</param>
	/// <returns><see langword="true" /> when such a token occurs.</returns>
	internal static bool HasLuaSymbol(string code)
	{
		ArgumentNullException.ThrowIfNull(code);
		for (int index = code.IndexOf('$', StringComparison.Ordinal);
			 index >= 0;
			 index = code.IndexOf('$', index + 1))
		{
			int end = index + 1;
			while (end < code.Length && code[end] is not ('"' or '[' or ']' or '+' or '-' or '*' or '(' or ')' or ','
					   or '\r' or '\n'))
			{
				end++;
			}

			ReadOnlySpan<char> run = code.AsSpan(index + 1, end - index - 1).TrimEnd();
			if (!run.IsEmpty && run[^1] == ':')
			{
				run = run[..^1];
			}

			if (run.ContainsAnyExcept(HexDigits))
			{
				return true;
			}
		}

		return false;
	}

	/// <summary>
	///     Whether <paramref name="token" /> occurs in <paramref name="script" /> as a whole Cheat Engine token, ignoring
	///     case: letters, digits, <c>. _ # @</c> and non-ASCII characters continue a token (<c>autoassembler.pas</c>
	///     <c>tokenize</c>).
	/// </summary>
	/// <param name="script">The text searched.</param>
	/// <param name="token">The token, such as <c>globalalloc</c>.</param>
	/// <returns><see langword="true" /> when the token occurs on its own.</returns>
	internal static bool ContainsToken(string script, string token)
	{
		ArgumentException.ThrowIfNullOrEmpty(token);
		for (int index = script.IndexOf(token, StringComparison.OrdinalIgnoreCase);
			 index >= 0;
			 index = script.IndexOf(token, index + 1, StringComparison.OrdinalIgnoreCase))
		{
			int after = index + token.Length;
			if ((index == 0 || !IsTokenCharacter(script[index - 1])) &&
				(after == script.Length || !IsTokenCharacter(script[after])))
			{
				return true;
			}
		}

		return false;
	}

	/// <summary>
	///     What Cheat Engine acts on before it removes comments, one per line so the classifier reports each alone: every
	///     directive opener of the raw script, ending with <c>}</c>, and every raw line that the shipped
	///     <c>preparecheader aa command.lua</c> prologue reacts to (trimmed, <c>USEMONO()</c> or a line starting
	///     <c>PREPARECHEADER(</c>), even inside a comment. Each entry is listed once.
	/// </summary>
	private static string RawTriggers(string script)
	{
		HashSet<string> triggers = new(StringComparer.OrdinalIgnoreCase);
		for (int index = script.IndexOf("{$", StringComparison.Ordinal);
			 index >= 0;
			 index = script.IndexOf("{$", index + 2, StringComparison.Ordinal))
		{
			int end = index + 2;
			while (end < script.Length && char.IsAsciiLetter(script[end]))
			{
				end++;
			}

			if (end > index + 2)
			{
				triggers.Add(string.Concat(script.AsSpan(index, end - index), "}"));
			}
		}

		foreach (Range range in script.AsSpan().SplitAny('\r', '\n'))
		{
			ReadOnlySpan<char> line = script.AsSpan(range).Trim();
			if (line.Equals("USEMONO()", StringComparison.OrdinalIgnoreCase))
			{
				triggers.Add("USEMONO()");
			}
			else if (line.StartsWith("PREPARECHEADER(", StringComparison.OrdinalIgnoreCase))
			{
				triggers.Add("PREPARECHEADER()");
			}
		}

		return string.Join('\n', triggers);
	}

	private static void RefuseGated(McpFeatureRequirements requirements)
	{
		foreach (McpFeature feature in requirements.Features)
		{
			if (feature is not McpFeature.AutoAssembler)
			{
				throw RunsWhileChecked(requirements.ReasonFor(feature)!);
			}
		}
	}

	private static CheatEngineToolException RunsWhileChecked(string reason)
	{
		return new CheatEngineToolException(new ToolError(ToolErrorKind.Unsupported,
			$"asm_check only checks scripts that need no switch beyond auto_assembler, because Cheat Engine runs parts of other scripts while it checks them: {reason}.",
			CheatEngineToolNames.AsmCheck, ToolHostEffect.NotStarted, false,
			"Review the script by reading it, as the review_aa_script prompt describes, and apply it only with the user's consent through asm_apply, which reports Cheat Engine's errors if it rejects the script."));
	}

	private static char Next(char[] text, int index)
	{
		return index + 1 < text.Length ? text[index + 1] : '\0';
	}

	private static bool IsTokenCharacter(char character)
	{
		return char.IsAsciiLetterOrDigit(character) || character is '.' or '_' or '#' or '@' || character > '\x7F';
	}
}
