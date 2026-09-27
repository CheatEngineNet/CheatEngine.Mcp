using System.Collections.Frozen;
using System.Globalization;
using System.Text;

using CheatEngine.Mcp.Core.Contract;

using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace CheatEngine.Mcp.Prompts.Workflows;

/// <summary>
///     Renders a guided workflow as a prompt: one user message with the fixed preamble, the checked inputs and the
///     embedded workflow body, then one resource link per related knowledge document.
/// </summary>
internal static class WorkflowPrompt
{
	/// <summary>The longest argument value echoed into a prompt.</summary>
	internal const int MaximumValueLength = 256;

	/// <summary>The rendered size every prompt stays under, in characters.</summary>
	internal const int MaximumRenderedLength = 6000;

	/// <summary>What every workflow prompt says before its body.</summary>
	internal const string Preamble =
		"This is a CheatEngine.Mcp guided workflow. Through the gateway, call " + CheatEngineToolNames.InstanceList +
		" first, confirm the instance with the user when more than one could match, and pass its instanceId to every " +
		"other tool; never switch to another instance. Work only on the process the user is entitled to modify, " +
		"explain every change to the game or to Cheat Engine before making it, and ask for consent. The linked " +
		"documents give the background.";

	private static readonly FrozenDictionary<string, string> Bodies =
		McpKnowledgeText.Load(typeof(WorkflowPrompt).Assembly, McpKnowledgeText.WorkflowsLogicalPrefix);

	/// <summary>The embedded workflow names (kebab case).</summary>
	internal static IEnumerable<string> EmbeddedWorkflows => Bodies.Keys;

	/// <summary>Returns an embedded workflow body with its links rewritten.</summary>
	/// <param name="workflow">The kebab-case workflow name.</param>
	/// <returns>The Markdown.</returns>
	internal static string Body(string workflow)
	{
		return Bodies[workflow];
	}

	/// <summary>Checks the inputs and renders the prompt.</summary>
	/// <param name="server">
	///     The serving server, whose resource collection supplies the linked documents' titles, descriptions and sizes;
	///     without it, or without the documents, a link carries only its URI, name and MIME type.
	/// </param>
	/// <param name="definition">The workflow.</param>
	/// <param name="inputs">The prompt's arguments, in declaration order.</param>
	/// <returns>The prompt.</returns>
	/// <exception cref="CheatEngineToolException">An argument is invalid (<c>invalid_argument</c>).</exception>
	internal static GetPromptResult Render(McpServer? server, WorkflowDefinition definition,
		params ReadOnlySpan<WorkflowInput> inputs)
	{
		string body = Body(definition.Workflow);
		int split = body.IndexOf('\n', StringComparison.Ordinal);
		string heading = body.StartsWith("# ", StringComparison.Ordinal) && split > 0 ? body[..split].TrimEnd() : string.Empty;
		string rest = heading.Length > 0 ? body[(split + 1)..].TrimStart('\r', '\n') : body;

		StringBuilder text = new();
		if (heading.Length > 0)
		{
			text.Append(heading).Append("\n\n");
		}

		text.Append(Preamble).Append("\n\n");
		if (inputs.Length > 0)
		{
			text.Append("Inputs (each {name} below stands for this value):\n");
			foreach (WorkflowInput input in inputs)
			{
				text.Append("- {").Append(input.Name).Append("}: ").Append(Describe(input)).Append('\n');
			}

			text.Append('\n');
		}

		text.Append(rest);
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

	/// <summary>The resource link to a knowledge document, enriched from the server's own listing when available.</summary>
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
			link.Description = listed.Description;
			link.Size = listed.Size;
		}

		return link;
	}

	private static string Describe(WorkflowInput input)
	{
		string? value = input.Value;
		if (value is null)
		{
			if (input.Required)
			{
				throw CheatEngineToolException.InvalidArgument(input.Name, "must not be empty.");
			}

			return input.Default is null ? "not given" : $"`{input.Default}` (default)";
		}

		if (value.Length > MaximumValueLength)
		{
			throw CheatEngineToolException.InvalidArgument(input.Name,
				$"must be at most {MaximumValueLength} characters.");
		}

		if (value.Any(char.IsControl))
		{
			throw CheatEngineToolException.InvalidArgument(input.Name, "must be one line without control characters.");
		}

		value = value.Trim();
		if (value.Length == 0)
		{
			if (input.Required)
			{
				throw CheatEngineToolException.InvalidArgument(input.Name, "must not be empty.");
			}

			return input.Default is null ? "not given" : $"`{input.Default}` (default)";
		}

		if (input.Allowed is { } allowed)
		{
			string? canonical = allowed.FirstOrDefault(candidate =>
				string.Equals(candidate, value, StringComparison.OrdinalIgnoreCase));
			value = canonical ?? throw CheatEngineToolException.InvalidArgument(input.Name,
				$"must be one of {string.Join(", ", allowed)}.");
		}

		switch (input.Kind)
		{
			case WorkflowInputKind.PositiveInteger when !int.TryParse(value, NumberStyles.None,
				CultureInfo.InvariantCulture, out int number) || number <= 0:
				throw CheatEngineToolException.InvalidArgument(input.Name, "must be a positive decimal integer.");
			case WorkflowInputKind.Speed when !double.TryParse(value, NumberStyles.AllowDecimalPoint,
				CultureInfo.InvariantCulture, out double speed) || speed is < 0.01 or > 1000:
				throw CheatEngineToolException.InvalidArgument(input.Name,
					"must be a number between 0.01 and 1000, such as 0.5 or 2.");
			case WorkflowInputKind.Symbol when !IsSymbol(value):
				throw CheatEngineToolException.InvalidArgument(input.Name,
					"must be a symbol name: a letter or underscore, then letters, digits or underscores.");
		}

		return $"`{value.Replace('`', '\'')}`";
	}

	private static bool IsSymbol(string value)
	{
		return value.Length <= 64 && (char.IsAsciiLetter(value[0]) || value[0] == '_') &&
			   value.All(static character => char.IsAsciiLetterOrDigit(character) || character == '_');
	}
}
