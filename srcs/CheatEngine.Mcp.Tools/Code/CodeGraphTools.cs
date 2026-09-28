using System.ComponentModel;

using CheatEngine.Client;
using CheatEngine.Client.Assembly;
using CheatEngine.Client.Processes;
using CheatEngine.Client.Results;
using CheatEngine.Mcp.Core.Contract;
using CheatEngine.Mcp.Tools.Memory;
using CheatEngine.SDK.Engine.Runtime;
using CheatEngine.SDK.Engine.Values;

using ModelContextProtocol.Server;

namespace CheatEngine.Mcp.Tools.Code;

/// <summary>The bounded function control-flow graph of the <c>code_*</c> contract, built over typed disassembly.</summary>
[McpServerToolType]
public sealed class CodeGraphTools
{
	/// <summary>The most instructions one graph decodes.</summary>
	internal const int MaximumGraphInstructions = 4096;

	/// <summary>The widest byte window a graph follows direct branches into.</summary>
	internal const int MaximumGraphBytes = 1024 * 1024;

	/// <summary>The decode attempts of one Cheat Engine dispatch.</summary>
	internal const int DecodesPerDispatch = 256;

	/// <summary>The call-target names one Cheat Engine dispatch resolves.</summary>
	internal const int NamesPerDispatch = 256;

	private readonly ToolDispatch _dispatch;

	/// <summary>Creates the graph tool; no Client work occurs during construction.</summary>
	/// <param name="dispatch">The activation's dispatch facade.</param>
	public CodeGraphTools(ToolDispatch dispatch)
	{
		ArgumentNullException.ThrowIfNull(dispatch);
		_dispatch = dispatch;
	}

	/// <summary>Builds a bounded control-flow graph of the function that starts at an address.</summary>
	[McpServerTool(Name = CheatEngineToolNames.CodeGetFunctionGraph, Title = "Get a function control-flow graph",
		ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
	[McpMeta(McpDispatchClass.MetaKey, McpDispatchClass.Short)]
	[Description(
		"Build a bounded control-flow graph of the x86 or x64 function that starts at address from Cheat Engine's typed disassembly: basic blocks with their terminator (return, jump, conditional, fallthrough, indirect, trap, external, limit) and successors, plus every call site with its direct target and symbol. Direct branches inside the byte window from address to address plus maxBytes are followed; targets outside it are listed as external, indirect targets are flagged and never followed, and calls are listed, not entered. Decodes 256 instructions per Cheat Engine dispatch and fails with target_changed if Cheat Engine selects another process in between. truncated is true when maxInstructions or the byte window stopped the walk. Start at a real instruction boundary, such as a call target or a start from code_get_function.")]
	public CodeFunctionGraph GetFunctionGraph(
		[Description("The function entry: an instruction address or Cheat Engine expression, such as game.exe+1C0.")]
		string address,
		[Description("The most distinct instructions decoded, 1 to 4096.")]
		int maxInstructions = 1024,
		[Description(
			"The byte window from address that branches are followed into, 1 to 1048576; direct targets outside it are reported as external.")]
		int maxBytes = 65536,
		[Description("Whether every block lists its decoded instructions.")]
		bool includeInstructions = false,
		CancellationToken cancellationToken = default)
	{
		const string operation = CheatEngineToolNames.CodeGetFunctionGraph;
		string expression = MemoryTargets.RequireExpression(address, "address");
		CodeTools.RequireRange(maxInstructions, "maxInstructions", 1, MaximumGraphInstructions);
		CodeTools.RequireRange(maxBytes, "maxBytes", 1, MaximumGraphBytes);
		(FunctionGraphBuilder graph, long epoch) = _dispatch.Run(operation, token =>
		{
			ICheatEngineClient client = _dispatch.Client;
			ulong entry = MemoryTargets.Resolve(client, expression, "address", token).ToUInt64();
			ProcessSnapshot process = client.Processes.GetCurrentProcess(token);
			bool is64 = Is64Bit(process);
			if (!is64 && entry > uint.MaxValue)
			{
				throw CheatEngineToolException.InvalidArgument("address",
					"resolves beyond the 4 GiB address space of the 32-bit target.");
			}

			FunctionGraphBuilder builder = new(entry, maxBytes, maxInstructions, is64);
			builder.Walk(next => Decode(client, next, entry, token), DecodesPerDispatch);
			return (builder, process.SelectionEpoch);
		}, cancellationToken);
		while (!graph.IsComplete)
		{
			_dispatch.Run(operation, token =>
			{
				ICheatEngineClient client = _dispatch.Client;
				MemoryTargets.RequireSameTarget(client, epoch, operation, token);
				graph.Walk(next => Decode(client, next, null, token), DecodesPerDispatch);
				return graph.InstructionCount;
			}, cancellationToken);
		}

		ulong[] targets = graph.DirectCallTargets();
		Dictionary<ulong, string> symbols = [];
		for (int offset = 0; offset < targets.Length; offset += NamesPerDispatch)
		{
			int first = offset;
			_dispatch.Run(operation, token =>
			{
				ICheatEngineClient client = _dispatch.Client;
				MemoryTargets.RequireSameTarget(client, epoch, operation, token);
				int last = Math.Min(targets.Length, first + NamesPerDispatch);
				for (int index = first; index < last; index++)
				{
					if (Name(client, targets[index], token) is { } name)
					{
						symbols[targets[index]] = name;
					}
				}

				return symbols.Count;
			}, cancellationToken);
		}

		return graph.Build(includeInstructions, symbols, operation);
	}

	/// <summary>
	///     Whether the selected target is x64 rather than x86; any other instruction set is refused, and so is an
	///     unreported one unless the target is a local process of known bitness.
	/// </summary>
	/// <param name="process">The selected process.</param>
	/// <returns><see langword="true" /> for x64.</returns>
	internal static bool Is64Bit(ProcessSnapshot process)
	{
		// Cheat Engine 7.7 decodes Intel code for a local process whose instruction set it did not report. A CEServer,
		// file or unestablished backend may serve another instruction set, which the bitness never tells.
		bool localWithBitness = process.Backend is TargetBackend.LocalProcess &&
								process.Bitness is { IsKnown: true, Bytes: 4 or 8 };
		return process.Architecture switch
		{
			CheatEngineArchitecture.X64 => true,
			CheatEngineArchitecture.X86 => false,
			CheatEngineArchitecture.Unknown when localWithBitness => process.Bitness.Bytes == 8,
			_ => throw CheatEngineToolException.Unsupported(
				"code_get_function_graph decodes x86 and x64 branches only, and Cheat Engine reports another or no instruction set for the target.",
				CheatEngineToolNames.CodeGetFunctionGraph)
		};
	}

	/// <summary>Decodes one instruction for the graph, inside the current dispatch.</summary>
	/// <param name="client">The activation's Client.</param>
	/// <param name="address">The instruction address.</param>
	/// <param name="entry">The graph's entry, whose decode failure fails the call, or <see langword="null" />.</param>
	/// <param name="cancellationToken">The token of the enclosing dispatch body.</param>
	/// <returns>The instruction, or <see langword="null" /> when that address alone cannot be decoded.</returns>
	private static AssemblyInstructionSnapshot? Decode(ICheatEngineClient client, ulong address, ulong? entry,
		CancellationToken cancellationToken)
	{
		if (client.Assembly.TryDisassemble(new Address(address), out AssemblyInstructionSnapshot instruction,
				out CheatEngineFailure failure, cancellationToken))
		{
			return instruction.Length > 0
				? instruction
				: throw CodeTools.NonPositiveLength(CheatEngineToolNames.CodeGetFunctionGraph);
		}

		bool itemFailure = failure.Kind is CheatEngineFailureKind.MemoryReadFailed or CheatEngineFailureKind.NotFound
			or CheatEngineFailureKind.OperationRejected or CheatEngineFailureKind.InvalidHostResult;
		return itemFailure && address != entry
			? null
			: throw CheatEngineToolException.FromFailure(failure, client.Stopping.IsCancellationRequested);
	}

	/// <summary>Cheat Engine's symbol name for a call target, unless it only restates the address.</summary>
	/// <param name="client">The activation's Client.</param>
	/// <param name="address">The call target.</param>
	/// <param name="cancellationToken">The token of the enclosing dispatch body.</param>
	/// <returns>The name, or <see langword="null" />.</returns>
	private static string? Name(ICheatEngineClient client, ulong address, CancellationToken cancellationToken)
	{
		return client.Inspection.TryResolveName(new Address(address), out string? name, out CheatEngineFailure _,
				   cancellationToken) && !string.IsNullOrWhiteSpace(name) && !name.All(char.IsAsciiHexDigit)
			? name
			: null;
	}
}
