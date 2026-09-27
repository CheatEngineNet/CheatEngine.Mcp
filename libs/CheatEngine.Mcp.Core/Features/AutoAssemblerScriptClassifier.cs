using System.Collections.Frozen;

namespace CheatEngine.Mcp.Core.Features;

/// <summary>
///     Finds the exposure switches a caller-supplied Auto Assembler script needs before Cheat Engine checks or applies
///     it. Every script needs <see cref="McpFeature.AutoAssembler" />; the classifier adds
///     <see cref="McpFeature.UnsafeLua" />, <see cref="McpFeature.TargetCodeExecution" /> and
///     <see cref="McpFeature.KernelAccess" /> for the constructs that reach past target memory patching.
/// </summary>
/// <remarks>
///     <para>
///         The scan is lexical and deliberately over-approximates: it may ask for more switches than a script uses, never
///         fewer. Comments and strings hide nothing, because Cheat Engine expands <c>{$lua}</c>, <c>{$luacode}</c>,
///         <c>{$c}</c> and <c>{$ccode}</c> blocks and runs Lua-registered prologues before it strips comments
///         (<c>autoassembler.pas</c>: <c>luacode</c> and <c>AutoAssemblerCodePass1</c> precede <c>removecomments</c>;
///         the shipped <c>preparecheader aa command.lua</c> prologue reacts to a <c>USEMONO()</c> line anywhere), and a
///         brace comment can end mid-line so that a command follows it. <c>[ENABLE]</c> and <c>[DISABLE]</c> sections are
///         scanned alike, since either may run.
///     </para>
///     <para>
///         Directives: <c>{$lua}</c> needs unsafe Lua; <c>{$luacode}</c> needs unsafe Lua and target code execution (it
///         runs Lua in Cheat Engine from the target and injects <c>luaclient-x86_64.dll</c> through <c>loadlibrary</c>);
///         <c>{$c}</c> and <c>{$ccode}</c> need target code execution (they compile C into the target);
///         <c>{$asm}</c>, <c>{$strict}</c>, <c>{$try}</c>, <c>{$except}</c>, <c>{$noprologue}</c>, <c>{$ifdef}</c>,
///         <c>{$ifndef}</c>, <c>{$endif}</c> and <c>{$define}</c> need nothing more; any other <c>{$name</c> needs both.
///     </para>
///     <para>
///         Commands: every name written before a <c>(</c> (spaces allowed) counts, including through <c>define</c>
///         aliases, because Cheat Engine substitutes defines into a line before it matches the command
///         (<c>define(alloc, loadlibrary)</c> turns <c>alloc(x)</c> into <c>loadlibrary(x)</c>); the alias name counts
///         as well, since a line above its <c>define</c> is not substituted. <c>loadlibrary</c>,
///         <c>createthread</c> and <c>createthreadandwait</c> need target code execution; <c>luacall</c> needs unsafe Lua;
///         <c>include</c> (another file's script) and <c>loadbinary</c> (a host file copied into the target) need both;
///         <c>kalloc</c> needs kernel access. A name that is not a built-in command needs unsafe Lua and target code
///         execution: Lua can register commands (<c>registerAutoAssemblerCommand</c>, celua.txt:287), and Cheat Engine
///         registers <c>USEMONO</c>, <c>FINDMONOMETHOD</c>, <c>GETMONOSTRUCT</c>, <c>USEJAVA</c> and
///         <c>PREPARECHEADER</c> from its autorun scripts, which launch data collectors or compile C.
///     </para>
///     <para>
///         The built-in command list comes from Cheat Engine 7.7.0.10621: the command strings of
///         <c>cheatengine-x86_64.exe</c>, cross-checked with the public <c>autoassembler.pas</c> and the Cheat Engine wiki
///         (<c>wiki.cheatengine.org/index.php?title=Auto_Assembler:Commands</c>); numbered hooks such as <c>hook5</c> are
///         built in, and a plain decimal number before a parenthesis (<c>+123 (note)</c>) is text. <c>sharedalloc</c> is
///         absent from 7.7 and counts as unknown. Out of scope: a Lua prologue (<c>registerAutoAssemblerPrologue</c>) or a
///         native plugin
///         that some earlier Lua installed can rewrite any script; that is Cheat Engine state, not script content.
///     </para>
/// </remarks>
public static class AutoAssemblerScriptClassifier
{
	private const int MaximumReportedName = 64;

	private const string AutoAssemblerReason = "the tool runs a caller-supplied Auto Assembler script";

	// Built-in Auto Assembler commands that only allocate, scan or patch target memory, plus the data pseudo-ops
	// db/dw/dd/dq, which may precede a cast such as "dd (float)1".
	private static readonly FrozenSet<string> BuiltInCommands = new[]
	{
		"alloc", "allocnx", "allocxo", "dealloc", "globalalloc", "label", "define", "registersymbol",
		"unregistersymbol", "assert", "aobscan", "aobscanex", "aobscanmodule", "aobscanregion", "aobscanfunction",
		"fullaccess", "readmem", "reassemble", "errordefine", "hook", "unhook", "align", "struct", "endstruct",
		"db", "dw", "dd", "dq"
	}.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

	private static readonly FrozenSet<string> PlainDirectives = new[]
	{
		"asm", "strict", "try", "except", "noprologue", "ifdef", "ifndef", "endif", "define"
	}.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

	private static readonly FrozenDictionary<string, (McpFeature Feature, string Reason)[]> GatedCommands =
		new Dictionary<string, (McpFeature Feature, string Reason)[]>(StringComparer.OrdinalIgnoreCase)
		{
			["loadlibrary"] =
			[
				(McpFeature.TargetCodeExecution, "calls loadlibrary, which loads a library into the target")
			],
			["createthread"] =
			[
				(McpFeature.TargetCodeExecution, "calls createthread, which starts a thread in the target")
			],
			["createthreadandwait"] =
			[
				(McpFeature.TargetCodeExecution,
					"calls createthreadandwait, which starts a thread in the target")
			],
			["luacall"] = [(McpFeature.UnsafeLua, "calls luacall, which runs Lua in Cheat Engine")],
			["include"] =
			[
				(McpFeature.UnsafeLua, "includes another file, whose content cannot be inspected"),
				(McpFeature.TargetCodeExecution, "includes another file, whose content cannot be inspected")
			],
			["loadbinary"] =
			[
				(McpFeature.UnsafeLua, "calls loadbinary, which copies a host file into the target"),
				(McpFeature.TargetCodeExecution, "calls loadbinary, which copies a host file into the target")
			],
			["kalloc"] =
			[
				(McpFeature.KernelAccess, "calls kalloc, which allocates memory through the kernel driver")
			]
		}.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

	/// <summary>Classifies one script.</summary>
	/// <param name="script">The whole script, both sections, exactly as Cheat Engine will receive it.</param>
	/// <returns>The switches the script needs, always including <see cref="McpFeature.AutoAssembler" />.</returns>
	public static McpFeatureRequirements Classify(string script)
	{
		ArgumentNullException.ThrowIfNull(script);
		McpFeatureRequirementsBuilder requirements = new();
		requirements.Add(McpFeature.AutoAssembler, AutoAssemblerReason);
		AddTo(requirements, script, "the script");
		return requirements.Build();
	}

	/// <summary>
	///     Adds the switches beyond <see cref="McpFeature.AutoAssembler" /> that a script needs, each reason starting with
	///     <paramref name="subject" />.
	/// </summary>
	/// <param name="requirements">The requirements being collected.</param>
	/// <param name="script">The whole script.</param>
	/// <param name="subject">How reasons name the script, such as <c>an Auto Assembler script of the table</c>.</param>
	internal static void AddTo(McpFeatureRequirementsBuilder requirements, string script, string subject)
	{
		AddDirectives(script, subject, requirements);
		List<string> names = FindCommandNames(script, out bool complete);
		foreach (string name in names)
		{
			AddCommand(name, subject, requirements);
		}

		if (!complete)
		{
			string reason = $"{subject} has too many overlapping define commands to follow every alias";
			requirements.Add(McpFeature.UnsafeLua, reason);
			requirements.Add(McpFeature.TargetCodeExecution, reason);
			requirements.Add(McpFeature.KernelAccess, reason);
		}
	}

	/// <summary>
	///     Lists every name that can end up in command position: each token written before a <c>(</c>, then, through
	///     <c>define(name, value)</c>, every token of the value of a define whose name is already listed.
	/// </summary>
	/// <remarks>
	///     The work stays linear in the script length: comma and closing-parenthesis searches only move forward, and the
	///     define values followed are capped at twice the script length (overlapping defines on one line); past the cap
	///     <paramref name="complete" /> is <see langword="false" /> and the caller assumes the worst.
	/// </remarks>
	/// <param name="script">The script.</param>
	/// <param name="complete">Whether every define alias could be followed.</param>
	/// <returns>The names in order of discovery, each once, ignoring case.</returns>
	internal static List<string> FindCommandNames(string script, out bool complete)
	{
		List<string> names = [];
		HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
		Dictionary<string, List<Range>> defines = new(StringComparer.OrdinalIgnoreCase);
		DefineReader reader = new(script);
		long budget = (2L * script.Length) + 4096;
		complete = true;
		for (int index = script.IndexOf('(', StringComparison.Ordinal);
		     index >= 0;
		     index = script.IndexOf('(', index + 1))
		{
			int end = index;
			while (end > 0 && script[end - 1] is ' ' or '\t')
			{
				end--;
			}

			int start = end;
			while (start > 0 && IsTokenCharacter(script[start - 1]))
			{
				start--;
			}

			if (start == end)
			{
				continue;
			}

			string name = script[start..end];
			if (seen.Add(name))
			{
				names.Add(name);
			}

			if (!name.Equals("define", StringComparison.OrdinalIgnoreCase) ||
			    !reader.TryRead(index, out Range defined, out Range value))
			{
				continue;
			}

			budget -= value.GetOffsetAndLength(script.Length).Length;
			if (budget < 0)
			{
				complete = false;
				continue;
			}

			string key = script[defined];
			if (!defines.TryGetValue(key, out List<Range>? values))
			{
				values = [];
				defines.Add(key, values);
			}

			values.Add(value);
		}

		// Follow define aliases from every listed name; each name is expanded once, so this stays linear.
		for (int next = 0; next < names.Count; next++)
		{
			if (!defines.TryGetValue(names[next], out List<Range>? values))
			{
				continue;
			}

			foreach (Range value in values)
			{
				foreach (string token in Tokens(script, value))
				{
					if (seen.Add(token))
					{
						names.Add(token);
					}
				}
			}
		}

		return names;
	}

	private static void AddDirectives(string script, string subject, McpFeatureRequirementsBuilder requirements)
	{
		FrozenSet<string>.AlternateLookup<ReadOnlySpan<char>> plain =
			PlainDirectives.GetAlternateLookup<ReadOnlySpan<char>>();
		for (int index = script.IndexOf("{$", StringComparison.Ordinal);
		     index >= 0;
		     index = script.IndexOf("{$", index + 2, StringComparison.Ordinal))
		{
			int start = index + 2;
			int end = start;
			while (end < script.Length && char.IsAsciiLetter(script[end]))
			{
				end++;
			}

			ReadOnlySpan<char> name = script.AsSpan(start, end - start);
			if (name.IsEmpty || plain.Contains(name))
			{
				continue;
			}

			if (name.Equals("lua", StringComparison.OrdinalIgnoreCase))
			{
				requirements.Add(McpFeature.UnsafeLua,
					$"{subject} has a {{$lua}} block, which runs Lua in Cheat Engine");
			}
			else if (name.Equals("luacode", StringComparison.OrdinalIgnoreCase))
			{
				requirements.Add(McpFeature.UnsafeLua,
					$"{subject} has a {{$luacode}} block, which runs Lua in Cheat Engine");
				requirements.Add(McpFeature.TargetCodeExecution,
					$"{subject} has a {{$luacode}} block, which injects the Lua client library into the target");
			}
			else if (name.Equals("c", StringComparison.OrdinalIgnoreCase) ||
			         name.Equals("ccode", StringComparison.OrdinalIgnoreCase))
			{
				requirements.Add(McpFeature.TargetCodeExecution,
					$"{subject} has a {{$c}} or {{$ccode}} block, which compiles C code into the target");
			}
			else
			{
				string reason = $"{subject} has the unrecognized directive {{${Shorten(name)}}}";
				requirements.Add(McpFeature.UnsafeLua, reason);
				requirements.Add(McpFeature.TargetCodeExecution, reason);
			}
		}
	}

	private static void AddCommand(string name, string subject, McpFeatureRequirementsBuilder requirements)
	{
		if (BuiltInCommands.Contains(name) || IsNumberedHook(name) || IsDecimal(name))
		{
			return;
		}

		if (GatedCommands.TryGetValue(name, out (McpFeature Feature, string Reason)[]? gates))
		{
			foreach ((McpFeature feature, string reason) in gates)
			{
				requirements.Add(feature, $"{subject} {reason}");
			}

			return;
		}

		string unknown =
			$"{subject} uses {Shorten(name)}, which is not a built-in Auto Assembler command and may be registered by Lua";
		requirements.Add(McpFeature.UnsafeLua, unknown);
		requirements.Add(McpFeature.TargetCodeExecution, unknown);
	}

	private static IEnumerable<string> Tokens(string script, Range range)
	{
		(int index, int length) = range.GetOffsetAndLength(script.Length);
		int end = index + length;
		while (index < end)
		{
			if (!IsTokenCharacter(script[index]))
			{
				index++;
				continue;
			}

			int start = index;
			while (index < end && IsTokenCharacter(script[index]))
			{
				index++;
			}

			yield return script[start..index];
		}
	}

	/// <summary>
	///     Cheat Engine's token alphabet (<c>autoassembler.pas</c> <c>tokenize</c>): letters, digits, <c>. _ # @</c> and
	///     non-ASCII.
	/// </summary>
	private static bool IsTokenCharacter(char character)
	{
		return char.IsAsciiLetterOrDigit(character) || character is '.' or '_' or '#' or '@' || character > '\x7F';
	}

	/// <summary>Cheat Engine 7.7's numbered hook commands, such as <c>hook5</c> and <c>hook14</c>.</summary>
	private static bool IsNumberedHook(string name)
	{
		return name.Length > 4 && name.StartsWith("hook", StringComparison.OrdinalIgnoreCase) &&
		       IsDecimal(name.AsSpan(4));
	}

	/// <summary>A plain decimal number before a parenthesis is text, such as <c>+123 (note)</c>, not a command.</summary>
	private static bool IsDecimal(ReadOnlySpan<char> name)
	{
		return !name.IsEmpty && !name.ContainsAnyExceptInRange('0', '9');
	}

	private static string Shorten(ReadOnlySpan<char> name)
	{
		return name.Length <= MaximumReportedName
			? name.ToString()
			: string.Concat(name[..MaximumReportedName], "...");
	}

	/// <summary>
	///     Reads <c>define(name, value)</c> as Cheat Engine does: the name up to the first comma of the line, the value up
	///     to the last <c>)</c> of the line. Its searches only move forward across calls with increasing positions.
	/// </summary>
	/// <param name="script">The script being classified.</param>
	private struct DefineReader(string script)
	{
		private int _lineEnd = -1;
		private int _lastClose = -1;
		private int _nextComma = -1;

		/// <summary>Reads the define whose <c>(</c> is at <paramref name="open" />.</summary>
		/// <returns>
		///     <see langword="false" /> when the line has no comma, or when the name is not one Cheat Engine token and so can
		///     never be substituted.
		/// </returns>
		internal bool TryRead(int open, out Range name, out Range value)
		{
			name = default;
			value = default;
			if (open >= _lineEnd)
			{
				int end = script.AsSpan(open).IndexOfAny('\r', '\n');
				_lineEnd = end < 0 ? script.Length : open + end;
				int close = script.AsSpan(open, _lineEnd - open).LastIndexOf(')');
				_lastClose = close < 0 ? -1 : open + close;
			}

			if (_nextComma <= open)
			{
				int comma = script.IndexOf(',', open + 1);
				_nextComma = comma < 0 ? int.MaxValue : comma;
			}

			if (_nextComma >= _lineEnd)
			{
				return false;
			}

			int first = open + 1;
			while (first < _nextComma && script[first] is ' ' or '\t')
			{
				first++;
			}

			int last = first;
			while (last < _nextComma && IsTokenCharacter(script[last]))
			{
				last++;
			}

			int after = last;
			while (after < _nextComma && script[after] is ' ' or '\t')
			{
				after++;
			}

			if (last == first || after != _nextComma)
			{
				return false;
			}

			name = first..last;
			value = (_nextComma + 1)..(_lastClose > _nextComma ? _lastClose : _lineEnd);
			return true;
		}
	}
}
