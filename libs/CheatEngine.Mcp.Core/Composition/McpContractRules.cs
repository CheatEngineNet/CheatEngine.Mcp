using System.Collections.Frozen;
using System.Text.Json;
using System.Text.Json.Nodes;

using CheatEngine.Mcp.Core.Contract;
using CheatEngine.Mcp.Core.Execution;

using ModelContextProtocol.Protocol;

namespace CheatEngine.Mcp.Core.Composition;

/// <summary>
///     The reviewed naming and metadata rules of the v2 tool, resource and prompt contract. They are pure, so the startup
///     validator and the contract tests share them. Every tool has an object output schema, a frozen v2 name and, for a
///     backend tool, its <see cref="McpDispatchClass" /> in <c>_meta</c>. Resources and prompts follow
///     <see cref="McpResourceUris" /> and the routing rule of <see cref="McpPrimitiveRouting" />; see
///     <c>McpContractRules.Primitives.cs</c>.
/// </summary>
internal static partial class McpContractRules
{
	internal const int MaxToolNameLength = 40;
	internal const int MaxTitleLength = 60;
	internal const int MaxToolDescriptionLength = 1024;
	internal const string RoutingArgument = "instanceId";

	/// <summary>The first name segment of every backend tool.</summary>
	internal static readonly FrozenSet<string> Domains = FrozenSet.Create(StringComparer.Ordinal,
		"runtime", "process", "memory", "scan", "aob", "pointer", "module", "symbol", "speedhack", "util", "code",
		"asm", "record", "table", "structure", "debugger", "exec", "dotnet", "mono", "kernel", "lua");

	/// <summary>The first name segment of the gateway's own tools.</summary>
	internal static readonly FrozenSet<string> GatewayDomains = FrozenSet.Create(StringComparer.Ordinal, "instance");

	/// <summary>The closed, reviewed list of second name segments; additions are contract changes.</summary>
	internal static readonly FrozenSet<string> Verbs = FrozenSet.Create(StringComparer.Ordinal,
		"add", "allocate", "apply", "assemble", "attach", "autoguess", "break", "calculate", "call", "check", "clear",
		"compare", "compile", "continue", "convert", "copy", "create", "decode", "delete", "detach", "disassemble",
		"dump", "enable", "execute", "fill", "find", "first", "free", "generate", "get", "group", "hash", "initialize",
		"inject",
		"invoke", "list", "load", "move", "next", "open", "poll", "read", "register", "release", "reload", "remove",
		"rescan", "reset", "resolve", "run", "save", "select", "set", "start", "step", "stop", "translate",
		"unregister", "update", "write");

	/// <summary>Capitalized words a sentence-case title may contain after its first word.</summary>
	internal static readonly FrozenSet<string> ProperNouns = FrozenSet.Create(
		StringComparer.Ordinal, "Assembler", "Auto", "C", "Cheat", "Engine", "Lua", "Mono", "Unity", "Windows");

	/// <summary>Validates every tool of one listing, including rules across tools such as unique titles.</summary>
	/// <param name="tools">The listing's tools.</param>
	/// <param name="gatewayLocal">Whether the tools are the gateway's own rather than backend tools.</param>
	/// <returns>One message per violation.</returns>
	internal static IEnumerable<string> ValidateTools(IEnumerable<Tool> tools, bool gatewayLocal)
	{
		ArgumentNullException.ThrowIfNull(tools);
		Dictionary<string, string> titles = new(StringComparer.OrdinalIgnoreCase);
		foreach (Tool tool in tools)
		{
			foreach (string failure in ValidateTool(tool, gatewayLocal))
			{
				yield return failure;
			}

			if (!string.IsNullOrWhiteSpace(tool.Title) && !titles.TryAdd(tool.Title, tool.Name))
			{
				yield return $"Tool '{tool.Name}' repeats the title '{tool.Title}' of '{titles[tool.Title]}'.";
			}
		}
	}

	/// <summary>Validates one tool.</summary>
	/// <param name="tool">The tool's protocol metadata.</param>
	/// <param name="gatewayLocal">Whether the tool is the gateway's own rather than a backend tool.</param>
	/// <returns>One message per violation.</returns>
	internal static IEnumerable<string> ValidateTool(Tool tool, bool gatewayLocal)
	{
		ArgumentNullException.ThrowIfNull(tool);
		List<string> failures = [];
		string name = tool.Name ?? string.Empty;
		if (HasRoutingArgument(tool.InputSchema))
		{
			failures.Add(
				$"Tool '{name}' declares '{RoutingArgument}', which only the gateway adds for routing.");
		}

		ValidateName(failures, name, gatewayLocal);
		if (!gatewayLocal)
		{
			ValidateDispatchClass(failures, name, tool.Meta);
		}

		ValidateTitle(failures, name, tool.Title);
		if (string.IsNullOrWhiteSpace(tool.Description))
		{
			failures.Add($"Tool '{name}' has no description.");
		}
		else if (tool.Description.Length > MaxToolDescriptionLength)
		{
			failures.Add($"Tool '{name}' has a description longer than {MaxToolDescriptionLength} characters.");
		}

		ValidateAnnotations(failures, name, tool.Annotations);
		if (tool.OutputSchema is not { } output || output.ValueKind != JsonValueKind.Object ||
			!output.TryGetProperty("type", out JsonElement outputType) || !IsObjectType(outputType))
		{
			failures.Add($"Tool '{name}' must publish an output schema of type object.");
		}

		ValidateInputSchema(failures, name, tool.InputSchema);
		return failures;
	}

	private static void ValidateName(List<string> failures, string name, bool gatewayLocal)
	{
		if (!IsSnakeName(name) || name.Length > MaxToolNameLength)
		{
			failures.Add(
				$"Tool '{name}' must match ^[a-z][a-z0-9]*(_[a-z0-9]+)+$ within {MaxToolNameLength} characters.");
			return;
		}

		string[] segments = name.Split('_');
		FrozenSet<string> domains = gatewayLocal ? GatewayDomains : Domains;
		if (!domains.Contains(segments[0]))
		{
			failures.Add($"Tool '{name}' starts with '{segments[0]}', which is not a reviewed domain.");
		}

		if (!Verbs.Contains(segments[1]))
		{
			failures.Add($"Tool '{name}' uses '{segments[1]}' as its verb, which is not in the reviewed verb list.");
		}

		// The catalog is frozen: a v2 name must be one of the reviewed constants, on the side that serves it.
		bool frozen = gatewayLocal
			? CheatEngineToolNames.All.Contains(name) && !CheatEngineToolNames.Backend.Contains(name)
			: CheatEngineToolNames.Backend.Contains(name);
		if (!frozen)
		{
			failures.Add(
				$"Tool '{name}' is not a {(gatewayLocal ? "gateway" : "backend")} name of the frozen v2 catalog " +
				$"({nameof(CheatEngineToolNames)}).");
		}
	}

	private static void ValidateDispatchClass(List<string> failures, string name, JsonObject? meta)
	{
		// Only a JSON string can carry the class; any other node is as wrong as a missing key.
		string? dispatchClass = meta?[McpDispatchClass.MetaKey] is JsonValue value &&
								value.TryGetValue(out string? text)
			? text
			: null;
		if (!McpDispatchClass.IsDefined(dispatchClass))
		{
			failures.Add(
				$"Tool '{name}' must publish '{McpDispatchClass.MetaKey}' in _meta as short, host_scan, " +
				"blocking_native or may_prompt.");
		}
	}

	private static void ValidateTitle(List<string> failures, string name, string? title)
	{
		if (string.IsNullOrWhiteSpace(title))
		{
			failures.Add($"Tool '{name}' has no title.");
			return;
		}

		if (title.Length > MaxTitleLength)
		{
			failures.Add($"Tool '{name}' has a title longer than {MaxTitleLength} characters.");
		}

		if (!IsSentenceCase(title))
		{
			failures.Add($"Tool '{name}' has the title '{title}', which is not in sentence case.");
		}
	}

	private static void ValidateAnnotations(List<string> failures, string name, ToolAnnotations? annotations)
	{
		if (annotations is not
			{
				ReadOnlyHint: { } readOnly, DestructiveHint: { } destructive, IdempotentHint: { } idempotent,
				OpenWorldHint: not null
			})
		{
			failures.Add(
				$"Tool '{name}' must set ReadOnly, Destructive, Idempotent and OpenWorld explicitly.");
			return;
		}

		if (readOnly && destructive)
		{
			failures.Add($"Tool '{name}' cannot be both read-only and destructive.");
		}

		string verb = name.Split('_') is [_, { } second, ..] ? second : string.Empty;
		// This poll drains DBVM's native log into a bounded ring; retries can consume and evict more events.
		if (name == CheatEngineToolNames.KernelPollWatch)
		{
			if (readOnly || !destructive || idempotent)
			{
				failures.Add($"Tool '{name}' consumes the native log, so it must be destructive, non-read-only and non-idempotent.");
			}
		}
		else if (verb == "poll" && !(readOnly && idempotent))
		{
			failures.Add($"Tool '{name}' polls, so it must be read-only and idempotent.");
		}

		if (verb is "stop" or "release" && !idempotent)
		{
			failures.Add($"Tool '{name}' stops or releases, so it must be idempotent.");
		}
	}

	private static void ValidateInputSchema(List<string> failures, string name, JsonElement schema)
	{
		if (schema.ValueKind != JsonValueKind.Object || !schema.TryGetProperty("type", out JsonElement type) ||
			!IsObjectType(type))
		{
			failures.Add($"Tool '{name}' must publish an input schema of type object.");
			return;
		}

		HashSet<string> declared = new(StringComparer.Ordinal);
		if (schema.TryGetProperty("properties", out JsonElement properties) &&
			properties.ValueKind == JsonValueKind.Object)
		{
			foreach (JsonProperty property in properties.EnumerateObject())
			{
				declared.Add(property.Name);
				if (property.Value.ValueKind != JsonValueKind.Object ||
					!property.Value.EnumerateObject().Any(static keyword => keyword.Name != "description"))
				{
					failures.Add($"Tool '{name}' parameter '{property.Name}' has an empty schema.");
				}

				if (property.Value.ValueKind != JsonValueKind.Object ||
					!property.Value.TryGetProperty("description", out JsonElement description) ||
					description.ValueKind != JsonValueKind.String ||
					string.IsNullOrWhiteSpace(description.GetString()))
				{
					failures.Add($"Tool '{name}' parameter '{property.Name}' has no description.");
				}
			}
		}

		if (schema.TryGetProperty("required", out JsonElement required) && required.ValueKind == JsonValueKind.Array)
		{
			foreach (JsonElement requiredName in required.EnumerateArray())
			{
				if (requiredName.ValueKind != JsonValueKind.String || !declared.Contains(requiredName.GetString()!))
				{
					failures.Add($"Tool '{name}' requires '{requiredName}', which is not a declared parameter.");
				}
			}
		}
	}

	private static bool IsObjectType(JsonElement type)
	{
		return type.ValueKind == JsonValueKind.String && type.ValueEquals("object");
	}

	private static bool HasRoutingArgument(JsonElement schema)
	{
		if (schema.ValueKind != JsonValueKind.Object ||
			!schema.TryGetProperty("properties", out JsonElement properties) ||
			properties.ValueKind != JsonValueKind.Object)
		{
			return false;
		}

		foreach (JsonProperty property in properties.EnumerateObject())
		{
			if (string.Equals(property.Name, RoutingArgument, StringComparison.OrdinalIgnoreCase))
			{
				return true;
			}
		}

		return false;
	}

	/// <summary>Checks <c>^[a-z][a-z0-9]*(_[a-z0-9]+)+$</c> without a regular expression.</summary>
	internal static bool IsSnakeName(string name)
	{
		if (name.Length == 0 || !char.IsAsciiLetterLower(name[0]))
		{
			return false;
		}

		bool separated = false;
		for (int index = 1; index < name.Length; index++)
		{
			char character = name[index];
			if (character == '_')
			{
				if (index == name.Length - 1 || name[index + 1] == '_')
				{
					return false;
				}

				separated = true;
			}
			else if (!char.IsAsciiLetterLower(character) && !char.IsAsciiDigit(character))
			{
				return false;
			}
		}

		return separated;
	}

	/// <summary>
	///     A sentence-case title starts with an uppercase letter; any later capitalized word is an acronym (two or more
	///     capitals or digits, such as AOB, DBVM or PDB, optionally plural such as NOPs), a dotted name such as .NET, or a
	///     reviewed proper noun.
	/// </summary>
	internal static bool IsSentenceCase(string title)
	{
		string[] words = title.Split(' ', StringSplitOptions.RemoveEmptyEntries);
		if (words.Length == 0 || !char.IsUpper(words[0][0]))
		{
			return false;
		}

		for (int index = 1; index < words.Length; index++)
		{
			string word = words[index].Trim('(', ')', ',', ':', ';', '"', '\'');
			if (word.Length == 0 || !char.IsUpper(word[0]))
			{
				continue;
			}

			// A plural acronym such as NOPs keeps its lowercase s.
			string stem = word.Length > 2 && word[^1] == 's' ? word[..^1] : word;
			bool acronym = stem.Length > 1 && stem.All(static character =>
				char.IsUpper(character) || char.IsDigit(character) || character is '-' or '/');
			if (!acronym && !ProperNouns.Contains(word))
			{
				return false;
			}
		}

		return true;
	}
}
