using CheatEngine.Client.Assembly;
using CheatEngine.Mcp.Core.Values;

namespace CheatEngine.Mcp.Tools.Code;

/// <summary>One decoded instruction of a function graph and its control transfer.</summary>
/// <param name="Address">The instruction's address.</param>
/// <param name="Next">The address that follows it, wrapped to the target's address width.</param>
/// <param name="Branch">Its control transfer.</param>
/// <param name="Snapshot">Cheat Engine's copied instruction.</param>
internal sealed record GraphInstruction(ulong Address, ulong Next, BranchInfo Branch,
	AssemblyInstructionSnapshot Snapshot);

/// <summary>
///     Builds a bounded control-flow graph by recursive descent from one entry: each chain decodes linearly until a
///     return, a trap, an unconditional or indirect jump, or code another chain already decoded; direct branch targets
///     inside the byte window start new chains, calls are recorded and stepped over, indirect targets are flagged and
///     never followed. <see cref="Walk" /> decodes a bounded batch, so a caller can spread the walk over several short
///     dispatches; the state lives in this instance only.
/// </summary>
internal sealed class FunctionGraphBuilder
{
	private readonly ulong _entry;
	private readonly Dictionary<ulong, GraphInstruction> _instructions = [];
	private readonly bool _is64;
	private readonly HashSet<ulong> _leaders = [];
	private readonly int _maximumInstructions;
	private readonly Queue<ulong> _pending = new();
	private readonly HashSet<ulong> _queued = [];
	private readonly HashSet<ulong> _undecodable = [];
	private readonly ulong _windowEnd;
	private ulong? _current;
	private bool _truncated;

	/// <summary>Starts a graph at an entry address.</summary>
	/// <param name="entry">The entry address, the first instruction decoded.</param>
	/// <param name="maximumBytes">The positive byte window from the entry that direct branches may reach.</param>
	/// <param name="maximumInstructions">The positive number of distinct instructions to decode at most.</param>
	/// <param name="is64">Whether the target is x64 rather than x86.</param>
	internal FunctionGraphBuilder(ulong entry, int maximumBytes, int maximumInstructions, bool is64)
	{
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumBytes);
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumInstructions);
		_entry = entry;
		_is64 = is64;
		_maximumInstructions = maximumInstructions;
		// The exclusive end of the address space; x64 saturates one byte short of it.
		ulong end = is64 ? ulong.MaxValue : 0x1_0000_0000UL;
		_windowEnd = entry >= end || end - entry < (ulong) maximumBytes ? end : entry + (ulong) maximumBytes;
		Enqueue(entry);
	}

	/// <summary>Whether the walk has ended: nothing is left to decode, or maxInstructions was reached.</summary>
	internal bool IsComplete
	{
		get;
		private set;
	}

	/// <summary>The number of distinct instructions decoded so far.</summary>
	internal int InstructionCount => _instructions.Count;

	/// <summary>Decodes at most <paramref name="budget" /> instructions, continuing where the previous batch stopped.</summary>
	/// <param name="decode">
	///     Decodes the instruction at an address, returning <see langword="null" /> for an address that cannot be
	///     decoded; it throws for a failure that concerns the whole call.
	/// </param>
	/// <param name="budget">The positive number of decode attempts of this batch.</param>
	internal void Walk(Func<ulong, AssemblyInstructionSnapshot?> decode, int budget)
	{
		ArgumentNullException.ThrowIfNull(decode);
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(budget);
		int attempts = 0;
		while (!IsComplete && attempts < budget)
		{
			if (_current is null)
			{
				if (!_pending.TryDequeue(out ulong start))
				{
					IsComplete = true;
					return;
				}

				_current = start;
			}

			ulong address = _current.Value;
			if (_instructions.ContainsKey(address) || _undecodable.Contains(address))
			{
				// Linear flow joined code decoded before: the join starts a block.
				_leaders.Add(address);
				_current = null;
				continue;
			}

			if (!InWindow(address))
			{
				// Linear flow left the byte window: the function continues beyond what was asked for.
				_truncated = true;
				_current = null;
				continue;
			}

			if (_instructions.Count >= _maximumInstructions)
			{
				_truncated = true;
				IsComplete = true;
				return;
			}

			attempts++;
			if (decode(address) is not { } snapshot)
			{
				_undecodable.Add(address);
				_current = null;
				continue;
			}

			GraphInstruction instruction = new(address, BranchDecoder.Next(address, snapshot.Length, _is64),
				BranchDecoder.Decode(snapshot.Bytes.AsSpan(), address, _is64), snapshot);
			_instructions.Add(address, instruction);
			_current = Follow(instruction);
		}
	}

	/// <summary>The distinct direct call targets decoded so far, in address order.</summary>
	/// <returns>The targets.</returns>
	internal ulong[] DirectCallTargets()
	{
		return
		[
			.. _instructions.Values
				.Where(static instruction => instruction.Branch is { Kind: BranchKind.Call, Target: not null })
				.Select(static instruction => instruction.Branch.Target!.Value)
				.Distinct()
				.Order()
		];
	}

	/// <summary>Builds the contract graph from the decoded instructions.</summary>
	/// <param name="includeInstructions">Whether each block lists its instructions.</param>
	/// <param name="symbols">Cheat Engine's names of direct call targets, where it has one.</param>
	/// <param name="operation">The tool name, for a malformed host instruction.</param>
	/// <returns>The graph.</returns>
	internal CodeFunctionGraph Build(bool includeInstructions, IReadOnlyDictionary<ulong, string> symbols,
		string operation)
	{
		ArgumentNullException.ThrowIfNull(symbols);
		GraphInstruction[] ordered = [.. _instructions.Values.OrderBy(static instruction => instruction.Address)];
		CodeBasicBlock[] blocks =
		[
			.. _leaders.Where(_instructions.ContainsKey).Order()
				.Select(start => Block(start, includeInstructions, operation))
		];
		CodeCallSite[] calls =
		[
			.. ordered.Where(static instruction => instruction.Branch.Kind is BranchKind.Call)
				.Select(instruction => Call(instruction, symbols))
		];
		ulong start = ordered.Length == 0 ? _entry : ordered[0].Address;
		ulong end = ordered.Length == 0
			? _entry
			: ordered.Max(static instruction =>
				unchecked(instruction.Address + (ulong) instruction.Snapshot.Length));
		bool truncated = _truncated || blocks.Any(static block => block.Terminator is CodeBlockTerminator.Limit);
		string[]? undecodable = _undecodable.Count == 0 ? null : [.. _undecodable.Order().Select(HexFormat.Address)];
		return new CodeFunctionGraph(HexFormat.Address(_entry), HexFormat.Address(start), HexFormat.Address(end),
			truncated, ordered.Length, blocks, calls, undecodable);
	}

	private CodeBasicBlock Block(ulong start, bool includeInstructions, string operation)
	{
		List<GraphInstruction> members = [];
		GraphInstruction current = _instructions[start];
		while (true)
		{
			members.Add(current);
			if (current.Branch.Kind is not (BranchKind.None or BranchKind.Call) || !InWindow(current.Next) ||
				_leaders.Contains(current.Next) ||
				!_instructions.TryGetValue(current.Next, out GraphInstruction? following))
			{
				break;
			}

			current = following;
		}

		GraphInstruction last = members[^1];
		List<string> successors = [];
		List<string> external = [];
		bool indirect = false;
		CodeBlockTerminator terminator;
		switch (last.Branch.Kind)
		{
			case BranchKind.Return:
				terminator = CodeBlockTerminator.Return;
				break;
			case BranchKind.Trap:
				terminator = CodeBlockTerminator.Trap;
				break;
			case BranchKind.Jump:
				if (last.Branch.Target is { } target)
				{
					terminator = InWindow(target) ? CodeBlockTerminator.Jump : CodeBlockTerminator.External;
					Edge(target, successors, external);
				}
				else
				{
					terminator = CodeBlockTerminator.Indirect;
					indirect = true;
				}

				break;
			case BranchKind.Conditional:
				terminator = CodeBlockTerminator.Conditional;
				if (last.Branch.Target is { } taken)
				{
					Edge(taken, successors, external);
				}
				else
				{
					indirect = true;
				}

				Edge(last.Next, successors, external);
				break;
			default:
				ulong next = last.Next;
				// A next instruction the walk never reached, although a branch may target it, means maxInstructions
				// stopped the walk here: the block is a limit, not a fallthrough into a block that does not exist.
				bool continues = InWindow(next) && (_instructions.ContainsKey(next) || _undecodable.Contains(next));
				terminator = continues ? CodeBlockTerminator.Fallthrough : CodeBlockTerminator.Limit;
				if (continues)
				{
					successors.Add(HexFormat.Address(next));
				}

				break;
		}

		ulong end = unchecked(last.Address + (ulong) last.Snapshot.Length);
		CodeInstruction[]? instructions = includeInstructions
			? [.. members.Select(member => CodeTools.Instruction(member.Snapshot, operation))]
			: null;
		return new CodeBasicBlock(HexFormat.Address(start), HexFormat.Address(end),
			(int) Math.Min(unchecked(end - start), int.MaxValue), members.Count, terminator, [.. successors],
			indirect, external.Count == 0 ? null : [.. external], instructions);
	}

	private static CodeCallSite Call(GraphInstruction instruction, IReadOnlyDictionary<ulong, string> symbols)
	{
		BranchInfo branch = instruction.Branch;
		string? target = branch.Target is { } direct ? HexFormat.Address(direct) : null;
		string? symbol = branch.Target is { } named && symbols.TryGetValue(named, out string? name) ? name : null;
		string? slot = branch.Slot is { } memory ? HexFormat.Address(memory) : null;
		return new CodeCallSite(HexFormat.Address(instruction.Address), branch.Indirect, instruction.Snapshot.Text,
			target, symbol, slot);
	}

	private ulong? Follow(GraphInstruction instruction)
	{
		BranchInfo branch = instruction.Branch;
		switch (branch.Kind)
		{
			case BranchKind.Return or BranchKind.Trap:
				return null;
			case BranchKind.Jump:
				if (branch.Target is { } target && InWindow(target))
				{
					Enqueue(target);
				}

				return null;
			case BranchKind.Conditional:
				if (branch.Target is { } taken && InWindow(taken))
				{
					Enqueue(taken);
				}

				// The fall-through of a conditional branch starts a block of its own.
				_leaders.Add(instruction.Next);
				return instruction.Next;
			default:
				return instruction.Next;
		}
	}

	private void Edge(ulong target, List<string> successors, List<string> external)
	{
		(InWindow(target) ? successors : external).Add(HexFormat.Address(target));
	}

	private void Enqueue(ulong address)
	{
		_leaders.Add(address);
		if (_queued.Add(address))
		{
			_pending.Enqueue(address);
		}
	}

	private bool InWindow(ulong address)
	{
		return address >= _entry && address < _windowEnd;
	}
}
