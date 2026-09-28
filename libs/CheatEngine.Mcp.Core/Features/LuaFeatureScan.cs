using System.Collections.Frozen;
using System.Collections.Immutable;

namespace CheatEngine.Mcp.Core.Features;

/// <summary>
///     Finds the exposure switches a fixed Lua body needs from the sensitive Cheat Engine APIs it names. It is the
///     defence-in-depth choke point behind the declarative gates: <see cref="Execution.ToolDispatch" /> refuses a body
///     whose
///     switch is off before it builds the script or dispatches it.
/// </summary>
/// <remarks>
///     The scan is lexical and deliberately over-approximates: every identifier counts, including those in comments,
///     strings and field accesses (<c>obj.autoAssemble</c>), so a fixed body can only ask for more switches than it uses,
///     never fewer. Exact names: <c>autoAssemble</c> and <c>autoAssembleCheck</c> need
///     <see cref="McpFeature.AutoAssembler" />; <c>executeMethod</c>,
///     <c>compile</c>, <c>compileCS</c>, <c>LaunchMonoDataCollector</c>, <c>mono_invoke_method</c> and
///     <c>speedhack_setSpeed</c> need <see cref="McpFeature.TargetCodeExecution" />; <c>loadTable</c> needs
///     <see cref="McpFeature.UnsafeLua" />. Prefixes: <c>executeCode…</c> and <c>inject…</c> need
///     <see cref="McpFeature.TargetCodeExecution" />; <c>dbk_…</c> and <c>dbvm_…</c> need
///     <see cref="McpFeature.KernelAccess" />.
/// </remarks>
internal static class LuaFeatureScan
{
	private static readonly FrozenDictionary<string, McpFeature> Names = new Dictionary<string, McpFeature>
	{
		["autoAssemble"] = McpFeature.AutoAssembler,
		// Cheat Engine runs {$lua} blocks while it checks a script, so a check is not free of effects.
		["autoAssembleCheck"] = McpFeature.AutoAssembler,
		["executeMethod"] = McpFeature.TargetCodeExecution,
		["compile"] = McpFeature.TargetCodeExecution,
		["compileCS"] = McpFeature.TargetCodeExecution,
		["LaunchMonoDataCollector"] = McpFeature.TargetCodeExecution,
		["mono_invoke_method"] = McpFeature.TargetCodeExecution,
		["speedhack_setSpeed"] = McpFeature.TargetCodeExecution,
		["loadTable"] = McpFeature.UnsafeLua
	}.ToFrozenDictionary(StringComparer.Ordinal);

	private static readonly ImmutableArray<(string Prefix, McpFeature Feature)> Prefixes =
	[
		("executeCode", McpFeature.TargetCodeExecution),
		("inject", McpFeature.TargetCodeExecution),
		("dbk_", McpFeature.KernelAccess),
		("dbvm_", McpFeature.KernelAccess)
	];

	/// <summary>Scans a fixed Lua body.</summary>
	/// <param name="body">The fixed body, without the runtime prelude or arguments.</param>
	/// <returns>The switches the body needs, each once, in <see cref="McpFeature" /> order.</returns>
	internal static McpFeature[] Scan(string body)
	{
		ArgumentNullException.ThrowIfNull(body);
		bool[] required = new bool[Enum.GetValues<McpFeature>().Length];
		FrozenDictionary<string, McpFeature>.AlternateLookup<ReadOnlySpan<char>> names =
			Names.GetAlternateLookup<ReadOnlySpan<char>>();
		ReadOnlySpan<char> text = body;
		int index = 0;
		while (index < text.Length)
		{
			if (!IsIdentifierStart(text[index]))
			{
				index++;
				continue;
			}

			int start = index;
			while (index < text.Length && IsIdentifierPart(text[index]))
			{
				index++;
			}

			ReadOnlySpan<char> identifier = text[start..index];
			if (names.TryGetValue(identifier, out McpFeature named))
			{
				required[(int) named] = true;
			}

			foreach ((string prefix, McpFeature feature) in Prefixes)
			{
				if (identifier.StartsWith(prefix, StringComparison.Ordinal))
				{
					required[(int) feature] = true;
				}
			}
		}

		List<McpFeature> features = [];
		for (int feature = 0; feature < required.Length; feature++)
		{
			if (required[feature])
			{
				features.Add((McpFeature) feature);
			}
		}

		return [.. features];
	}

	private static bool IsIdentifierStart(char character)
	{
		return char.IsAsciiLetter(character) || character == '_';
	}

	private static bool IsIdentifierPart(char character)
	{
		return char.IsAsciiLetterOrDigit(character) || character == '_';
	}
}
