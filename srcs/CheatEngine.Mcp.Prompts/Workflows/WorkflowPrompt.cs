using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

using CheatEngine.Mcp.Core.Contract;
using CheatEngine.Mcp.Core.Features;
using CheatEngine.Mcp.Resources.Knowledge;

using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace CheatEngine.Mcp.Prompts.Workflows;

/// <summary>
///     Renders a guided workflow as a prompt: one user message with the fixed preamble, the settings its gated steps
///     need, the checked inputs and the workflow body from the Resources knowledge base, then one resource link per
///     related knowledge document.
/// </summary>
/// <remarks>
///     A body may hold argument-conditioned excerpts: sections headed <c>### name: value</c>, where <c>name</c> is
///     one of the definition's <see cref="WorkflowDefinition.Selectors" />. The renderer keeps only the section of the
///     selected value, or the <c>### name: default</c> section when no section matches, so a body can carry guidance
///     for every choice and still render within <see cref="MaximumRenderedLength" />.
/// </remarks>
internal static partial class WorkflowPrompt
{
	/// <summary>The longest argument value echoed into a prompt.</summary>
	internal const int MaximumValueLength = 256;

	/// <summary>The rendered size every prompt stays under, in characters.</summary>
	internal const int MaximumRenderedLength = 6000;

	/// <summary>
	///     The longest symbol name a <see cref="WorkflowInputKind.Symbol" /> argument accepts by default: the
	///     <c>symbolName</c> limit of <c>asm_generate_injection</c>, which receives it.
	/// </summary>
	internal const int MaximumSymbolLength = 64;

	/// <summary>The value of an excerpt section that applies when no section matches the selected value.</summary>
	internal const string DefaultExcerpt = "default";

	/// <summary>What every workflow prompt says before its body, on every host.</summary>
	internal const string Preamble =
		"This is a CheatEngine.Mcp guided workflow. When the tools take an instanceId (gateway), call " +
		CheatEngineToolNames.InstanceList + " first, confirm the instance with the user when more than one could " +
		"match, and pass its instanceId to every other tool; never switch to another instance. Work only on the " +
		"process the user is entitled to modify, explain every change to the game or to Cheat Engine before making it, " +
		"and ask for consent. The linked documents give the background.";

	/// <summary>The workflow names (kebab case) of the knowledge base, each the body of one prompt.</summary>
	internal static IEnumerable<string> EmbeddedWorkflows => CheatEngineKnowledge.WorkflowNames;

	/// <summary>Returns a workflow body of the knowledge base, with its links rewritten.</summary>
	/// <param name="workflow">The kebab-case workflow name.</param>
	/// <returns>The Markdown.</returns>
	/// <exception cref="KeyNotFoundException">The knowledge base has no such workflow.</exception>
	internal static string Body(string workflow)
	{
		return CheatEngineKnowledge.TryReadWorkflow(workflow, out string? markdown)
			? markdown
			: throw new KeyNotFoundException($"The knowledge base has no workflow named '{workflow}'.");
	}

	/// <summary>
	///     The sentence that names the settings a workflow's gated steps need, such as
	///     <c>Needs Mcp:EnableAutoAssembler for its gated steps.</c>; empty when the workflow cites no gated tool.
	/// </summary>
	/// <param name="gates">The workflow's gates.</param>
	/// <returns>The sentence, or an empty string.</returns>
	internal static string GateLine(IReadOnlyList<McpFeature> gates)
	{
		ArgumentNullException.ThrowIfNull(gates);
		if (gates.Count == 0)
		{
			return string.Empty;
		}

		string[] settings = [.. gates.Select(static gate => "Mcp:" + McpFeatureGate.SettingName(gate))];
		string list = settings.Length == 1
			? settings[0]
			: string.Join(", ", settings[..^1]) + " and " + settings[^1];
		return $"Needs {list} for its gated steps.";
	}

	/// <summary>Checks the inputs and renders the prompt.</summary>
	/// <param name="server">
	///     The serving server, whose resource collection supplies the linked documents' titles, descriptions and sizes;
	///     without it, or without the documents, a link carries only its URI, name and MIME type.
	/// </param>
	/// <param name="definition">The workflow.</param>
	/// <param name="values">The argument values, one per <see cref="WorkflowDefinition.Arguments" /> entry.</param>
	/// <returns>The prompt.</returns>
	/// <exception cref="ArgumentException">The values do not match the definition's arguments.</exception>
	/// <exception cref="CheatEngineToolException">An argument is invalid (<c>invalid_argument</c>).</exception>
	internal static GetPromptResult Render(McpServer? server, WorkflowDefinition definition,
		params ReadOnlySpan<string?> values)
	{
		ArgumentNullException.ThrowIfNull(definition);
		return RenderBody(server, definition, Body(definition.Workflow), values);
	}

	/// <summary>
	///     Checks the inputs and renders the prompt from <paramref name="body" /> instead of the embedded body, so a
	///     body can be measured as written, without a rebuild.
	/// </summary>
	/// <param name="server">The serving server, or <see langword="null" />; see <see cref="Render" />.</param>
	/// <param name="definition">The workflow.</param>
	/// <param name="body">The workflow body, with LF line endings and links rewritten to their served URIs.</param>
	/// <param name="values">The argument values, one per <see cref="WorkflowDefinition.Arguments" /> entry.</param>
	/// <returns>The prompt.</returns>
	/// <exception cref="ArgumentException">The values do not match the definition's arguments.</exception>
	/// <exception cref="CheatEngineToolException">An argument is invalid (<c>invalid_argument</c>).</exception>
	internal static GetPromptResult RenderBody(McpServer? server, WorkflowDefinition definition, string body,
		ReadOnlySpan<string?> values)
	{
		ArgumentNullException.ThrowIfNull(definition);
		ArgumentNullException.ThrowIfNull(body);
		if (values.Length != definition.Arguments.Count)
		{
			throw new ArgumentException(
				$"{definition.Prompt} declares {definition.Arguments.Count} arguments but received {values.Length}.",
				nameof(values));
		}

		// Every input is checked before anything is rendered, so an invalid argument never yields a partial prompt.
		string[] described = new string[values.Length];
		Dictionary<string, string?> selections = new(StringComparer.Ordinal);
		for (int index = 0; index < values.Length; index++)
		{
			WorkflowArgument argument = definition.Arguments[index];
			(described[index], string? selected) = Describe(argument, values[index]);
			if (definition.Selectors.Contains(argument.Name, StringComparer.Ordinal))
			{
				selections[argument.Name] = selected;
			}
		}

		int split = body.IndexOf('\n', StringComparison.Ordinal);
		string heading = body.StartsWith("# ", StringComparison.Ordinal) && split > 0
			? body[..split].TrimEnd()
			: string.Empty;
		string rest = heading.Length > 0 ? body[(split + 1)..].TrimStart('\r', '\n') : body;

		StringBuilder text = new();
		if (heading.Length > 0)
		{
			text.Append(heading).Append("\n\n");
		}

		text.Append(Preamble).Append("\n\n");
		string gates = GateLine(definition.Gates);
		if (gates.Length > 0)
		{
			text.Append(gates).Append("\n\n");
		}

		if (values.Length > 0)
		{
			text.Append("Inputs (each {name} below stands for this value):\n");
			for (int index = 0; index < values.Length; index++)
			{
				text.Append("- {").Append(definition.Arguments[index].Name).Append("}: ").Append(described[index])
					.Append('\n');
			}

			text.Append('\n');
		}

		text.Append(SelectExcerpts(rest, selections));
		List<PromptMessage> messages =
		[
			new() { Role = Role.User, Content = new TextContentBlock { Text = text.ToString() } }
		];
		foreach (string slug in definition.Documents)
		{
			messages.Add(new PromptMessage { Role = Role.User, Content = Link(server, slug) });
		}

		return new GetPromptResult { Description = heading.Length > 0 ? heading[2..] : null, Messages = messages };
	}

	/// <summary>
	///     Keeps, for each selector, only the <c>### name: value</c> section of the selected value, or the
	///     <c>### name: default</c> section when no section has that value (or no value was selected). A section runs
	///     from its heading to the next heading of level 1 to 3; headings inside fenced code blocks are text.
	/// </summary>
	/// <param name="markdown">The body, with LF line endings.</param>
	/// <param name="selections">The selected value of each selector argument, or <see langword="null" /> for none.</param>
	/// <returns>The body with the other sections of each selector removed.</returns>
	internal static string SelectExcerpts(string markdown, IReadOnlyDictionary<string, string?> selections)
	{
		ArgumentNullException.ThrowIfNull(markdown);
		ArgumentNullException.ThrowIfNull(selections);
		if (selections.Count == 0)
		{
			return markdown;
		}

		string[] lines = markdown.Split('\n');
		(string? Selector, string? Value)[] owners = Sections(lines, selections);
		Dictionary<string, string> kept = new(StringComparer.Ordinal);
		foreach ((string selector, string? value) in selections)
		{
			HashSet<string> present = new(owners.Where(owner => owner.Selector == selector)
				.Select(static owner => owner.Value!), StringComparer.OrdinalIgnoreCase);
			if (value is not null && present.Contains(value))
			{
				kept[selector] = value;
			}
			else if (present.Contains(DefaultExcerpt))
			{
				kept[selector] = DefaultExcerpt;
			}
		}

		StringBuilder text = new(markdown.Length);
		for (int index = 0; index < lines.Length; index++)
		{
			(string? selector, string? value) = owners[index];
			if (selector is null ||
				(kept.TryGetValue(selector, out string? keep) &&
				 string.Equals(keep, value, StringComparison.OrdinalIgnoreCase)))
			{
				text.Append(lines[index]);
				if (index < lines.Length - 1)
				{
					text.Append('\n');
				}
			}
		}

		return text.ToString();
	}

	/// <summary>
	///     The resource link to a knowledge document, enriched from the server's own listing when available with its
	///     name, title and size. The description stays in the listing: the body already says why each document is linked,
	///     and the prompt budget then does not depend on how the documents are described.
	/// </summary>
	/// <param name="server">The serving server, or <see langword="null" />.</param>
	/// <param name="slug">The document slug.</param>
	/// <returns>The link.</returns>
	internal static ResourceLinkBlock Link(McpServer? server, string slug)
	{
		string uri = McpResourceUris.Doc(slug);
		ResourceLinkBlock link = new()
		{
			Uri = uri,
			Name = "doc_" + slug.Replace('-', '_'),
			MimeType = McpResourceUris.MarkdownMimeType
		};
		if (server?.ServerOptions.ResourceCollection is { } resources &&
			resources.TryGetPrimitive(uri, out McpServerResource? resource) &&
			resource.ProtocolResource is { } listed)
		{
			link.Name = listed.Name;
			link.Title = listed.Title;
			link.Size = listed.Size;
		}

		return link;
	}

	/// <summary>The selector section of each line, or <see langword="null" /> outside every section.</summary>
	private static (string? Selector, string? Value)[] Sections(string[] lines,
		IReadOnlyDictionary<string, string?> selections)
	{
		(string? Selector, string? Value)[] owners = new (string?, string?)[lines.Length];
		(string? Selector, string? Value) current = (null, null);
		bool fenced = false;
		for (int index = 0; index < lines.Length; index++)
		{
			string line = lines[index];
			if (line.StartsWith("```", StringComparison.Ordinal))
			{
				fenced = !fenced;
			}
			else if (!fenced && SectionHeading().IsMatch(line))
			{
				Match excerpt = ExcerptHeading().Match(line);
				current = excerpt.Success && selections.ContainsKey(excerpt.Groups["name"].Value)
					? (excerpt.Groups["name"].Value, excerpt.Groups["value"].Value.Trim())
					: (null, null);
			}

			owners[index] = current;
		}

		return owners;
	}

	private static (string Description, string? Selected) Describe(WorkflowArgument argument, string? value)
	{
		if (value is not null)
		{
			if (value.Length > MaximumValueLength)
			{
				throw CheatEngineToolException.InvalidArgument(argument.Name,
					$"must be at most {MaximumValueLength} characters.");
			}

			if (value.Any(char.IsControl))
			{
				throw CheatEngineToolException.InvalidArgument(argument.Name,
					"must be one line without control characters.");
			}

			value = value.Trim();
		}

		if (string.IsNullOrEmpty(value))
		{
			if (argument.Required)
			{
				throw CheatEngineToolException.InvalidArgument(argument.Name, "must not be empty.");
			}

			return argument.Default is { } fallback
				? ($"`{fallback}` (default){Note(argument, fallback)}", fallback)
				: ("not given", null);
		}

		if (argument.Allowed is { } allowed)
		{
			string? canonical = allowed.FirstOrDefault(candidate =>
				string.Equals(candidate, value, StringComparison.OrdinalIgnoreCase));
			value = canonical ?? throw CheatEngineToolException.InvalidArgument(argument.Name,
				$"must be one of {string.Join(", ", allowed)}.");
		}

		// A number is rendered in its canonical form: a JSON number the body can pass on as written, which a padded
		// value such as 000016 is not, and no longer than the budget tests measure.
		switch (argument.Kind)
		{
			case WorkflowInputKind.PositiveInteger:
				value = Integer(argument, value, 1, "must be a positive decimal integer.");
				break;
			case WorkflowInputKind.NonNegativeInteger:
				value = Integer(argument, value, 0, "must be a non-negative decimal integer.");
				break;
			case WorkflowInputKind.Speed:
				// The range pattern also refuses NaN, which the parser accepts.
				value = double.TryParse(value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture,
					out double speed) && speed is >= 0.01 and <= 1000
					? speed.ToString(CultureInfo.InvariantCulture)
					: throw CheatEngineToolException.InvalidArgument(argument.Name,
						"must be a number between 0.01 and 1000, such as 0.5 or 2.");
				break;
			case WorkflowInputKind.Symbol when !IsSymbol(value, argument.MaximumLength ?? MaximumSymbolLength):
				throw CheatEngineToolException.InvalidArgument(argument.Name,
					$"must be a symbol name of at most {argument.MaximumLength ?? MaximumSymbolLength} characters: a " +
					"letter or underscore, then letters, digits or underscores.");
			case WorkflowInputKind.ToolName:
				value = CheatEngineToolNames.All.FirstOrDefault(name =>
							string.Equals(name, value, StringComparison.OrdinalIgnoreCase)) ??
						throw CheatEngineToolException.InvalidArgument(argument.Name,
							"must be a tool name of this server, such as " + CheatEngineToolNames.MemoryWrite + ".");
				break;
		}

		return ($"`{value.Replace('`', '\'')}`{Note(argument, value)}", value);
	}

	/// <summary>
	///     The canonical text of a decimal integer from <paramref name="minimum" /> to the argument's
	///     <see cref="WorkflowArgument.Maximum" /> (else <see cref="int.MaxValue" />), without leading zeros.
	/// </summary>
	/// <param name="argument">The argument, named in the error.</param>
	/// <param name="value">The trimmed value.</param>
	/// <param name="minimum">The smallest accepted value.</param>
	/// <param name="requirement">The error message of a refused value when the argument has no maximum.</param>
	/// <returns>The integer's invariant text.</returns>
	/// <exception cref="CheatEngineToolException">The value is no such integer (<c>invalid_argument</c>).</exception>
	private static string Integer(WorkflowArgument argument, string value, int minimum, string requirement)
	{
		// NumberStyles.None refuses signs, spaces and separators, so any parsed value is at least 0.
		int maximum = argument.Maximum ?? int.MaxValue;
		return int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int number) &&
			   number >= minimum && number <= maximum
			? number.ToString(CultureInfo.InvariantCulture)
			: throw CheatEngineToolException.InvalidArgument(argument.Name, argument.Maximum is null
				? requirement
				: $"must be a decimal integer from {minimum} to {maximum}.");
	}

	private static string Note(WorkflowArgument argument, string value)
	{
		return argument.ValueNotes is { } notes && notes.TryGetValue(value, out string? note) ? $" ({note})" : "";
	}

	private static bool IsSymbol(string value, int maximumLength)
	{
		return value.Length <= maximumLength && (char.IsAsciiLetter(value[0]) || value[0] == '_') &&
			   value.All(static character => char.IsAsciiLetterOrDigit(character) || character == '_');
	}

	[GeneratedRegex("^#{1,3} ", RegexOptions.CultureInvariant)]
	private static partial Regex SectionHeading();

	[GeneratedRegex("^### (?<name>[A-Za-z][A-Za-z0-9]*): (?<value>[^\\s].*)$", RegexOptions.CultureInvariant)]
	private static partial Regex ExcerptHeading();
}
