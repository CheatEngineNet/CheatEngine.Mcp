using System.ComponentModel.DataAnnotations;
using System.Reflection;
using System.Text.Json.Nodes;

using CheatEngine.Mcp.Core.Contract;
using CheatEngine.Mcp.Core.Execution;
using CheatEngine.Mcp.Core.Features;

using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace CheatEngine.Mcp.Core.Composition;

/// <summary>The resource and prompt rules of the contract.</summary>
internal static partial class McpContractRules
{
	internal const int MaxPromptNameLength = 40;

	/// <summary>
	///     Validates the resources of one server: URI space and grammar, routing, MIME type, metadata, annotations,
	///     parameters, overlap between templates and, for a live resource, the tool it projects.
	/// </summary>
	/// <param name="resources">The server's resources and resource templates.</param>
	/// <param name="toolNames">
	///     The tools the same server serves; when there are any, a live resource's source tool must be one of them.
	/// </param>
	/// <returns>One message per violation.</returns>
	internal static IEnumerable<string> ValidateResources(IEnumerable<McpServerResource> resources,
		IEnumerable<string> toolNames)
	{
		ArgumentNullException.ThrowIfNull(resources);
		ArgumentNullException.ThrowIfNull(toolNames);
		McpServerResource[] all = resources.ToArray();
		HashSet<string> served = new(toolNames, StringComparer.Ordinal);
		List<string> failures = [];
		Dictionary<string, string> names = new(StringComparer.OrdinalIgnoreCase);
		foreach (McpServerResource resource in all)
		{
			ResourceTemplate template = resource.ProtocolResourceTemplate;
			ValidateResource(failures, resource, served);
			if (!string.IsNullOrEmpty(template.Name) && !names.TryAdd(template.Name, template.UriTemplate))
			{
				failures.Add($"Resource '{template.UriTemplate}' repeats the name '{template.Name}' of " +
							 $"'{names[template.Name]}'.");
			}
		}

		// Two primitives that match the same URI make the SDK serve whichever it tries first.
		for (int first = 0; first < all.Length; first++)
		{
			for (int second = first + 1; second < all.Length; second++)
			{
				if (Overlaps(all[first], all[second]) || Overlaps(all[second], all[first]))
				{
					failures.Add($"Resources '{all[first].ProtocolResourceTemplate.UriTemplate}' and " +
								 $"'{all[second].ProtocolResourceTemplate.UriTemplate}' match the same URI.");
				}
			}
		}

		return failures;
	}

	/// <summary>
	///     Validates the prompts of one server: names, titles, descriptions, arguments (each with a description and a
	///     title) and routing. A prompt must be a static method, because no gateway argument selects the instance a
	///     prompt would run on.
	/// </summary>
	/// <param name="prompts">The server's prompts.</param>
	/// <param name="toolNames">The server's tool names, which a prompt name must not repeat.</param>
	/// <returns>One message per violation.</returns>
	internal static IEnumerable<string> ValidatePrompts(IEnumerable<McpServerPrompt> prompts,
		IEnumerable<string> toolNames)
	{
		ArgumentNullException.ThrowIfNull(prompts);
		ArgumentNullException.ThrowIfNull(toolNames);
		HashSet<string> tools = new(toolNames, StringComparer.Ordinal);
		Dictionary<string, string> titles = new(StringComparer.OrdinalIgnoreCase);
		List<string> failures = [];
		foreach (McpServerPrompt prompt in prompts)
		{
			Prompt protocol = prompt.ProtocolPrompt;
			string name = protocol.Name ?? string.Empty;
			if (!IsPromptName(name))
			{
				failures.Add(
					$"Prompt '{name}' must match ^[a-z][a-z0-9]*(_[a-z0-9]+)*$ within {MaxPromptNameLength} characters.");
			}

			if (tools.Contains(name))
			{
				failures.Add($"Prompt '{name}' has the name of a tool.");
			}

			if (string.IsNullOrWhiteSpace(protocol.Title))
			{
				failures.Add($"Prompt '{name}' has no title.");
			}
			else
			{
				if (protocol.Title.Length > MaxTitleLength || !IsSentenceCase(protocol.Title))
				{
					failures.Add(
						$"Prompt '{name}' needs a sentence-case title of at most {MaxTitleLength} characters.");
				}

				if (!titles.TryAdd(protocol.Title, name))
				{
					failures.Add(
						$"Prompt '{name}' repeats the title '{protocol.Title}' of '{titles[protocol.Title]}'.");
				}
			}

			if (string.IsNullOrWhiteSpace(protocol.Description))
			{
				failures.Add($"Prompt '{name}' has no description.");
			}

			Dictionary<string, string> argumentTitles = new(StringComparer.OrdinalIgnoreCase);
			foreach (PromptArgument argument in protocol.Arguments ?? [])
			{
				if (string.Equals(argument.Name, RoutingArgument, StringComparison.OrdinalIgnoreCase))
				{
					failures.Add(
						$"Prompt '{name}' declares '{RoutingArgument}', which only the gateway uses for routing.");
				}

				if (string.IsNullOrWhiteSpace(argument.Description))
				{
					failures.Add($"Prompt '{name}' argument '{argument.Name}' has no description.");
				}

				ValidateArgumentTitle(failures, name, argument, argumentTitles);
			}

			MethodInfo? method = McpPrimitiveOrigin.MethodOf(prompt);
			if (method is not null)
			{
				if (!method.IsStatic)
				{
					failures.Add($"Prompt '{name}' must be a static method: a prompt is served locally, never routed.");
				}

				ValidateParameters(failures, $"Prompt '{name}'", method,
					typeof(RequestContext<GetPromptRequestParams>));
			}
		}

		return failures;
	}

	/// <summary>
	///     A prompt argument needs a title, the label a client shows instead of its name: the
	///     <c>[Display(Name = ...)]</c> of its parameter, in sentence case, at most <see cref="MaxTitleLength" />
	///     characters and distinct from the other titles of its prompt.
	/// </summary>
	private static void ValidateArgumentTitle(List<string> failures, string prompt, PromptArgument argument,
		Dictionary<string, string> titles)
	{
		string subject = $"Prompt '{prompt}' argument '{argument.Name}'";
		if (string.IsNullOrWhiteSpace(argument.Title))
		{
			failures.Add($"{subject} has no title; declare [Display(Name = ...)] on its parameter.");
		}
		else if (argument.Title.Length > MaxTitleLength || !IsSentenceCase(argument.Title))
		{
			failures.Add($"{subject} needs a sentence-case title of at most {MaxTitleLength} characters.");
		}
		else if (!titles.TryAdd(argument.Title, argument.Name))
		{
			failures.Add($"{subject} repeats the title '{argument.Title}' of '{titles[argument.Title]}'.");
		}
	}

	/// <summary>Checks a prompt or resource name: lowercase snake case, one word allowed, at most 40 characters.</summary>
	/// <param name="name">The name.</param>
	/// <returns><see langword="true" /> for a valid name.</returns>
	internal static bool IsPromptName(string name)
	{
		return name.Length <= MaxPromptNameLength && (IsSnakeName(name) || (name.Length > 0 &&
																			char.IsAsciiLetterLower(name[0]) &&
																			name.All(static character =>
																				char.IsAsciiLetterLower(character) ||
																				char.IsAsciiDigit(character))));
	}

	/// <summary>
	///     Parses a resource URI template into its literal path segments, path variables and trailing query variables.
	///     The grammar is <c>cheatengine://{authority}/{segment}(/{segment})*[{?var(,var)*}]</c>, where a segment is either
	///     a kebab-case literal or exactly one <c>{camelCaseVariable}</c>.
	/// </summary>
	/// <param name="uriTemplate">The template.</param>
	/// <param name="variables">Every variable, path variables first.</param>
	/// <param name="error">Why the template is invalid.</param>
	/// <returns><see langword="true" /> for a template in the grammar.</returns>
	internal static bool TryParseTemplate(string uriTemplate, out IReadOnlyList<string> variables, out string? error)
	{
		List<string> found = [];
		variables = found;
		error = null;
		const string schemePrefix = McpResourceUris.Scheme + "://";
		if (!uriTemplate.StartsWith(schemePrefix, StringComparison.Ordinal))
		{
			error = $"must start with {schemePrefix}";
			return false;
		}

		string rest = uriTemplate[schemePrefix.Length..];
		int query = rest.IndexOf("{?", StringComparison.Ordinal);
		string path = query < 0 ? rest : rest[..query];
		List<string> queryVariables = [];
		if (query >= 0)
		{
			string expression = rest[query..];
			if (!expression.EndsWith('}') || expression.IndexOf('}', StringComparison.Ordinal) != expression.Length - 1)
			{
				error = "may end with one {?a,b} query expression only";
				return false;
			}

			foreach (string variable in expression[2..^1].Split(','))
			{
				if (!IsCamelCase(variable))
				{
					error = $"has the query variable '{variable}', which is not camelCase";
					return false;
				}

				queryVariables.Add(variable);
			}
		}

		string[] segments = path.Split('/');
		if (segments.Length < 2)
		{
			error = "needs an authority and at least one path segment";
			return false;
		}

		for (int index = 0; index < segments.Length; index++)
		{
			string segment = segments[index];
			if (segment.StartsWith('{') && segment.EndsWith('}') && index > 0)
			{
				string variable = segment[1..^1];
				if (!IsCamelCase(variable))
				{
					error = $"has the variable '{variable}', which is not camelCase";
					return false;
				}

				found.Add(variable);
			}
			else if (!IsKebabCase(segment))
			{
				error = $"has the segment '{segment}', which is neither kebab-case nor one {{variable}}";
				return false;
			}
		}

		found.AddRange(queryVariables);
		if (found.Distinct(StringComparer.Ordinal).Count() != found.Count)
		{
			error = "repeats a variable";
			return false;
		}

		return true;
	}

	/// <summary>Expands a template with a sample value per variable and no query, for the overlap check.</summary>
	/// <param name="uriTemplate">A template in the grammar.</param>
	/// <returns>A URI the template matches.</returns>
	internal static string Sample(string uriTemplate)
	{
		int query = uriTemplate.IndexOf("{?", StringComparison.Ordinal);
		string path = query < 0 ? uriTemplate : uriTemplate[..query];
		return string.Join('/', path.Split('/')
			.Select(static segment => segment.StartsWith('{') && segment.EndsWith('}') ? "sample-1" : segment));
	}

	private static void ValidateResource(List<string> failures, McpServerResource resource,
		IReadOnlySet<string> servedTools)
	{
		ResourceTemplate template = resource.ProtocolResourceTemplate;
		string uri = template.UriTemplate ?? string.Empty;
		string subject = $"Resource '{uri}'";
		McpPrimitiveRouting routing = McpPrimitiveOrigin.Of(resource)?.Routing ??
									  (McpPrimitiveOrigin.MethodOf(resource) is { } declared
										  ? McpPrimitiveOrigin.RoutingOf(declared)
										  : McpPrimitiveRouting.Instance);
		if (McpResourceUris.IsGatewayReserved(uri))
		{
			failures.Add($"{subject} uses {McpResourceUris.GatewayInstances}, which the gateway reserves.");
		}
		else if (routing is McpPrimitiveRouting.Local && !McpResourceUris.IsDocs(uri))
		{
			failures.Add(
				$"{subject} is static, so it is served locally and must be under {McpResourceUris.DocsPrefix}.");
		}
		else if (routing is McpPrimitiveRouting.Instance && !McpResourceUris.IsInstance(uri))
		{
			failures.Add(
				$"{subject} is an instance method, so the gateway routes it and it must be under " +
				$"{McpResourceUris.InstancePrefix}.");
		}

		if (!TryParseTemplate(uri, out IReadOnlyList<string> variables, out string? error))
		{
			failures.Add($"{subject} {error}.");
		}
		else if (variables.Contains(RoutingArgument, StringComparer.OrdinalIgnoreCase))
		{
			failures.Add($"{subject} declares '{RoutingArgument}', which only the gateway uses for routing.");
		}

		string expectedMime = routing is McpPrimitiveRouting.Local
			? McpResourceUris.MarkdownMimeType
			: McpResourceUris.JsonMimeType;
		if (!string.Equals(template.MimeType, expectedMime, StringComparison.Ordinal))
		{
			failures.Add($"{subject} must declare the MIME type {expectedMime} explicitly.");
		}

		if (string.IsNullOrWhiteSpace(template.Name) || !IsPromptName(template.Name))
		{
			failures.Add($"{subject} needs a snake_case name of at most {MaxPromptNameLength} characters.");
		}

		if (string.IsNullOrWhiteSpace(template.Title))
		{
			failures.Add($"{subject} has no title.");
		}
		else if (template.Title.Length > MaxTitleLength)
		{
			failures.Add($"{subject} has a title longer than {MaxTitleLength} characters.");
		}

		if (string.IsNullOrWhiteSpace(template.Description))
		{
			failures.Add($"{subject} has no description.");
		}

		foreach (McpResourceAnnotationsAttribute annotations in
				 resource.Metadata.OfType<McpResourceAnnotationsAttribute>())
		{
			failures.AddRange(annotations.Validate().Select(reason => $"{subject} {reason}."));
		}

		ValidateSource(failures, subject, resource, routing, servedTools);
		if (McpPrimitiveOrigin.MethodOf(resource) is { } method)
		{
			string[] parameters = ValidateParameters(failures, subject, method,
				typeof(RequestContext<ReadResourceRequestParams>));
			foreach (string parameter in parameters.Except(variables, StringComparer.Ordinal))
			{
				failures.Add($"{subject} has the parameter '{parameter}', which no template variable binds.");
			}

			foreach (string variable in variables.Except(parameters, StringComparer.Ordinal))
			{
				failures.Add($"{subject} has the variable '{variable}', which no string parameter receives.");
			}

			ValidateCompletions(failures, subject, uri, method, routing);
		}
	}

	/// <summary>
	///     A variable marked with <see cref="McpCompletionAttribute" /> must be a path variable of a live template
	///     whose container implements <see cref="IMcpCompletionSource" />, and must not also declare
	///     <c>[AllowedValues]</c>, which the SDK completes by itself: the gateway forwards only marked variables and
	///     answers allowed values from its own catalog.
	/// </summary>
	private static void ValidateCompletions(List<string> failures, string subject, string uriTemplate,
		MethodInfo method, McpPrimitiveRouting routing)
	{
		int query = uriTemplate.IndexOf("{?", StringComparison.Ordinal);
		string[] segments = (query < 0 ? uriTemplate : uriTemplate[..query]).Split('/');
		foreach (ParameterInfo parameter in method.GetParameters())
		{
			if (parameter.GetCustomAttribute<McpCompletionAttribute>() is null)
			{
				continue;
			}

			string name = parameter.Name ?? string.Empty;
			if (routing is not McpPrimitiveRouting.Instance)
			{
				failures.Add($"{subject} is served locally, so it must not mark '{name}' with [McpCompletion]; " +
							 "declare [AllowedValues] instead.");
				continue;
			}

			if (!segments.Contains("{" + name + "}", StringComparer.Ordinal))
			{
				failures.Add($"{subject} marks '{name}' with [McpCompletion], which only a path variable may carry.");
			}

			if (parameter.GetCustomAttribute<AllowedValuesAttribute>() is not null)
			{
				failures.Add($"{subject} marks '{name}' with both [AllowedValues] and [McpCompletion]; the SDK " +
							 "already completes allowed values.");
			}

			Type? container = method.ReflectedType ?? method.DeclaringType;
			if (container is null || !typeof(IMcpCompletionSource).IsAssignableFrom(container))
			{
				string owner = container?.Name ?? "its container";
				failures.Add($"{subject} marks '{name}' with [McpCompletion], but {owner} does not implement " +
							 $"{nameof(IMcpCompletionSource)}.");
			}
		}
	}

	/// <summary>
	///     A live resource must name, with <see cref="McpSourceToolAttribute" />, the tool whose structured result it
	///     returns, and publish that name in <c>_meta</c>. A client may read or prefetch a resource without any tool call,
	///     so the tool must be a frozen backend tool that is read-only, closed-world, ungated and <c>short</c>; when the
	///     server serves tools, it must serve that one. A Local document projects no tool.
	/// </summary>
	private static void ValidateSource(List<string> failures, string subject, McpServerResource resource,
		McpPrimitiveRouting routing, IReadOnlySet<string> servedTools)
	{
		McpResourceSource? source = McpResourceSource.Of(resource);
		if (routing is not McpPrimitiveRouting.Instance)
		{
			if (source is not null)
			{
				failures.Add($"{subject} is served locally, so it must not declare a source tool.");
			}

			// The gateway serves a Local resource without any activation, so no gate could refuse the read.
			if (resource.Metadata.OfType<RequiresFeatureAttribute>().Any())
			{
				failures.Add($"{subject} is served locally, where no feature gate exists, so it must not require a " +
							 "feature switch.");
			}

			return;
		}

		if (source is null)
		{
			failures.Add($"{subject} is a live resource, so it must name the read-only tool it projects with " +
						 $"[McpSourceTool], published in _meta under {McpSourceToolAttribute.MetaKey}.");
			return;
		}

		string tool = source.ToolName;
		JsonNode? published = resource.ProtocolResourceTemplate.Meta?[McpSourceToolAttribute.MetaKey];
		if (published is not JsonValue value || !value.TryGetValue(out string? text) ||
			!string.Equals(text, tool, StringComparison.Ordinal))
		{
			failures.Add($"{subject} must publish '{tool}' in _meta under {McpSourceToolAttribute.MetaKey}.");
		}

		if (source.Method is null)
		{
			failures.Add($"{subject} names the source tool '{tool}', which {source.ToolType.Name} does not declare " +
						 "exactly once.");
			return;
		}

		if (!CheatEngineToolNames.Backend.Contains(tool))
		{
			failures.Add($"{subject} projects '{tool}', which is not a backend name of the frozen v2 catalog.");
		}
		else if (servedTools.Count > 0 && !servedTools.Contains(tool))
		{
			failures.Add($"{subject} projects '{tool}', which this server does not serve.");
		}

		if (!source.ReadOnly || source.OpenWorld)
		{
			failures.Add($"{subject} projects '{tool}', which must be read-only and closed-world.");
		}

		if (source.Requires.Count > 0)
		{
			failures.Add($"{subject} projects '{tool}', which requires " +
						 $"{string.Join(", ", source.Requires.Select(McpFeatureGate.ContractName))}; a resource read " +
						 "must never need a feature switch.");
		}

		if (!string.Equals(source.DispatchClass, McpDispatchClass.Short, StringComparison.Ordinal))
		{
			failures.Add($"{subject} projects '{tool}', whose dispatch class is {source.DispatchClass ?? "missing"}; " +
						 $"only a {McpDispatchClass.Short} tool may back a resource a client can prefetch.");
		}
	}

	/// <summary>
	///     Accepts only <see cref="string" /> arguments plus request services; a service would need the transport
	///     container, which holds none.
	/// </summary>
	/// <returns>The names of the string parameters.</returns>
	private static string[] ValidateParameters(List<string> failures, string subject, MethodInfo method,
		Type requestContext)
	{
		List<string> arguments = [];
		foreach (ParameterInfo parameter in method.GetParameters())
		{
			Type type = parameter.ParameterType;
			if (type == typeof(string))
			{
				arguments.Add(parameter.Name ?? string.Empty);
			}
			else if (type != typeof(CancellationToken) && type != typeof(McpServer) && type != requestContext)
			{
				failures.Add($"{subject} has the parameter '{parameter.Name}' of type {type.Name}; only strings are " +
							 "allowed.");
			}
		}

		return [.. arguments];
	}

	private static bool Overlaps(McpServerResource subject, McpServerResource other)
	{
		string uri = subject.ProtocolResourceTemplate.UriTemplate ?? string.Empty;
		return other.IsMatch(subject.IsTemplated ? Sample(uri) : uri);
	}

	private static bool IsCamelCase(string value)
	{
		return value.Length > 0 && char.IsAsciiLetterLower(value[0]) &&
			   value.All(static character => char.IsAsciiLetterOrDigit(character));
	}

	private static bool IsKebabCase(string value)
	{
		if (value.Length == 0 || value[0] == '-' || value[^1] == '-' ||
			value.Contains("--", StringComparison.Ordinal))
		{
			return false;
		}

		return value.All(static character =>
			char.IsAsciiLetterLower(character) || char.IsAsciiDigit(character) || character == '-');
	}
}
