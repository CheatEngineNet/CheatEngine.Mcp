using System.ComponentModel;

using CheatEngine.Mcp.Core.Contract;
using CheatEngine.Mcp.Core.Values;

using ModelContextProtocol.Server;

namespace CheatEngine.Mcp.Tools.Pointer;

/// <summary>Analyzes caller-supplied instruction and capture facts without reading the selected target.</summary>
[McpServerToolType]
public sealed class PointerAccessTools
{
	private const int MaximumInstructionTextLength = 512;
	private const int MaximumMapEntries = 64;
	private readonly ToolDispatch _dispatch;

	/// <summary>Creates the read-only supplied-facts analyser.</summary>
	public PointerAccessTools(ToolDispatch dispatch)
	{
		ArgumentNullException.ThrowIfNull(dispatch);
		_dispatch = dispatch;
	}

	/// <summary>Evaluates one explicit memory operand from supplied disassembly and register facts.</summary>
	[McpServerTool(Name = CheatEngineToolNames.PointerGetAccessInfo, Title = "Analyze pointer access", ReadOnly = true,
		Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
	[McpMeta(McpDispatchClass.MetaKey, McpDispatchClass.Short)]
	[Description("Analyzes one supplied x86 or x64 memory operand and captured registers without resolving live symbols or reading target memory. instructionText is authoritative disassembly; instructionBytes checks length and address-size prefixes, but is not decoded or compared with the text. Indexed operands report a dynamic offset and are not stable pointer-chain claims.")]
	public PointerAccessInfo GetAccessInfo(
		[Description("The authoritative disassembly text, such as mov eax,[rax+10].")]
		string instructionText,
		[Description("The instruction address as uppercase or 0x-prefixed hexadecimal.")]
		string instructionAddress,
		[Description("The supplied instruction length in bytes, 1 through 15.")]
		int instructionLength,
		[Description("x86 or x64.")]
		string architecture,
		[Description("Captured register names and hexadecimal values. A 32-bit alias cannot supply unknown upper bits for a 64-bit operand.")]
		IReadOnlyDictionary<string, string> registers,
		[Description("Optional instruction bytes. Their count must equal instructionLength; they are not decoded by this supplied-text mode.")]
		string? instructionBytes = null,
		[Description("An optional observed accessed address for mismatch reporting.")]
		string? observedAccessAddress = null,
		[Description("Whether registers are from before execution or from a post-execution access/write event.")]
		PointerAccessContextPhase contextPhase = PointerAccessContextPhase.PreExecution,
		[Description("Optional bounded caller-supplied symbol or module-base map. No live symbol resolution occurs.")]
		IReadOnlyDictionary<string, string>? symbols = null,
		CancellationToken cancellationToken = default)
	{
		if (string.IsNullOrWhiteSpace(instructionText) || instructionText.Length > MaximumInstructionTextLength)
		{
			throw CheatEngineToolException.InvalidArgument("instructionText", "must be 1 to 512 characters.");
		}

		if (!HexParse.TryAddress(instructionAddress, out ulong address))
		{
			throw CheatEngineToolException.InvalidArgument("instructionAddress", "must be a hexadecimal address.");
		}

		PointerSupport.Range(instructionLength, 1, 15, "instructionLength");
		if (architecture is null)
		{
			throw CheatEngineToolException.InvalidArgument("architecture", "must be x86 or x64.");
		}

		bool wide = architecture.Equals("x64", StringComparison.OrdinalIgnoreCase);
		if (!wide && !architecture.Equals("x86", StringComparison.OrdinalIgnoreCase))
		{
			throw CheatEngineToolException.InvalidArgument("architecture", "must be x86 or x64.");
		}
		if (!wide && address > uint.MaxValue)
		{
			throw CheatEngineToolException.InvalidArgument("instructionAddress", "must fit the x86 address width.");
		}
		if (!Enum.IsDefined(contextPhase))
		{
			throw CheatEngineToolException.InvalidArgument("contextPhase", "must be pre_execution or post_execution.");
		}
		if (observedAccessAddress is not null)
		{
			if (!HexParse.TryAddress(observedAccessAddress, out ulong observed))
			{
				throw CheatEngineToolException.InvalidArgument("observedAccessAddress", "must be a hexadecimal address.");
			}
			observedAccessAddress = HexFormat.Address(observed);
		}

		byte[]? bytes = instructionBytes is null ? null : HexParse.Bytes(instructionBytes, "instructionBytes", 15);
		if (bytes is not null && bytes.Length != instructionLength)
		{
			return new PointerAccessInfo(PointerAccessStatus.ContradictoryFacts, instructionText.Trim(),
				Uncertainty: "instructionBytes length differs from instructionLength.");
		}

		ArgumentNullException.ThrowIfNull(registers);
		string[][] registerPairs = RegisterPairs(registers, wide);
		string[][] symbolPairs = symbols is null ? [] : Pairs(symbols, "symbols");
		string text = instructionText.Trim();
		string opcode;
		string parameters;
		int split = text.IndexOfAny([' ', '\t']);
		if (split < 0)
		{
			opcode = text;
			parameters = string.Empty;
		}
		else
		{
			opcode = text[..split].Trim();
			parameters = text[(split + 1)..].Trim();
		}

		return _dispatch.RunLua(CheatEngineToolNames.PointerGetAccessInfo, PointerAccessLuaScripts.GetAccessInfo,
			PointerAccessJsonContext.Default.PointerAccessInfo, cancellationToken, opcode, parameters, address,
			instructionLength, wide, bytes ?? [], registerPairs, symbolPairs, observedAccessAddress,
			contextPhase == PointerAccessContextPhase.PostExecution);
	}

	private static string[][] Pairs(IReadOnlyDictionary<string, string> source, string parameter)
	{
		if (source.Count > MaximumMapEntries)
		{
			throw CheatEngineToolException.LimitExceeded(parameter, "accepts at most 64 entries.");
		}

		string[][] result = new string[source.Count][];
		HashSet<string> names = new(StringComparer.OrdinalIgnoreCase);
		int index = 0;
		foreach ((string name, string value) in source)
		{
			if (string.IsNullOrWhiteSpace(name) || name.Length > 64 || !HexParse.TryAddress(value, out ulong parsed))
			{
				throw CheatEngineToolException.InvalidArgument(parameter,
					"entries need a non-empty name and a hexadecimal address or register value.");
			}
			if (!names.Add(name))
			{
				throw CheatEngineToolException.InvalidArgument(parameter, "contains duplicate names ignoring case.");
			}

			result[index++] = [name.ToUpperInvariant(), HexFormat.Address(parsed)];
		}

		return result;
	}

	private static string[][] RegisterPairs(IReadOnlyDictionary<string, string> source, bool wide)
	{
		string[][] pairs = Pairs(source, "registers");
		Dictionary<string, ulong> values = pairs.ToDictionary(static pair => pair[0], static pair =>
		{
			_ = HexParse.TryAddress(pair[1], out ulong value);
			return value;
		}, StringComparer.Ordinal);
		foreach ((string name, ulong value) in values)
		{
			bool narrow = !wide || name is "EAX" or "EBX" or "ECX" or "EDX" or "ESI" or "EDI" or "EBP" or "ESP" or "EIP" ||
				name is "R8D" or "R9D" or "R10D" or "R11D" or "R12D" or "R13D" or "R14D" or "R15D";
			if (narrow && value > uint.MaxValue)
			{
				throw CheatEngineToolException.InvalidArgument("registers", $"{name} must fit its 32-bit register width.");
			}

			string? alias = name switch
			{
				"RAX" => "EAX",
				"RBX" => "EBX",
				"RCX" => "ECX",
				"RDX" => "EDX",
				"RSI" => "ESI",
				"RDI" => "EDI",
				"RBP" => "EBP",
				"RSP" => "ESP",
				"RIP" => "EIP",
				"R8" or "R9" or "R10" or "R11" or "R12" or "R13" or "R14" or "R15" => name + "D",
				_ => null
			};
			if (alias is not null && values.TryGetValue(alias, out ulong low) && (uint) value != low)
			{
				throw CheatEngineToolException.InvalidArgument("registers",
					$"contains conflicting aliases for {name}.");
			}
		}
		return pairs;
	}
}
