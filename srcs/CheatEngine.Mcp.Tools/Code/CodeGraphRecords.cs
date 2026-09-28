using System.ComponentModel;
using System.Text.Json.Serialization;

using CheatEngine.Mcp.Core.Contract;

namespace CheatEngine.Mcp.Tools.Code;

/// <summary>How a basic block of a function graph ends; the wire value is the <c>snake_case</c> member name.</summary>
[JsonConverter(typeof(ContractEnumConverter<CodeBlockTerminator>))]
public enum CodeBlockTerminator
{
	/// <summary>A return instruction.</summary>
	Return,

	/// <summary>An unconditional direct jump to a block inside the byte window.</summary>
	Jump,

	/// <summary>A conditional jump: its target and the next instruction.</summary>
	Conditional,

	/// <summary>No branch: the next instruction starts another block.</summary>
	Fallthrough,

	/// <summary>A jump through a register or memory, which the graph does not follow.</summary>
	Indirect,

	/// <summary>An instruction that faults or halts: int3, int 29h, ud0, ud1, ud2 or hlt.</summary>
	Trap,

	/// <summary>An unconditional direct jump outside the byte window, such as a tail call.</summary>
	External,

	/// <summary>Decoding stopped after the block because maxInstructions or the byte window was reached.</summary>
	Limit
}

/// <summary>One basic block of a function control-flow graph.</summary>
public sealed record CodeBasicBlock(
	[property: Description("The block's first instruction address, uppercase hexadecimal without 0x.")]
	string Start,
	[property: Description("The exclusive end of the block's last instruction, uppercase hexadecimal without 0x.")]
	string End,
	[property: Description("The block's size in bytes.")]
	int Size,
	[property: Description("The number of instructions in the block.")]
	int InstructionCount,
	[property: Description("How the block ends.")]
	CodeBlockTerminator Terminator,
	[property:
		Description(
			"The start addresses of the blocks control can reach next inside the byte window, the taken target first; one without a block could not be decoded or was not reached because of a limit.")]
	string[] Successors,
	[property: Description("Whether the block also continues at a register or memory target that is not followed.")]
	bool HasIndirectSuccessor = false,
	[property:
		Description(
			"Successor addresses outside the byte window, which the graph does not follow, such as a tail call; omitted when there are none.")]
	string[]? ExternalTargets = null,
	[property: Description("The block's instructions in address order; omitted unless includeInstructions is true.")]
	CodeInstruction[]? Instructions = null);

/// <summary>One call instruction found while building a function graph.</summary>
public sealed record CodeCallSite(
	[property: Description("The call instruction's address, uppercase hexadecimal without 0x.")]
	string From,
	[property: Description("Whether the call target comes from a register or memory rather than the instruction.")]
	bool Indirect,
	[property: Description("The call instruction as Cheat Engine displayed it, with its annotation.")]
	string Text,
	[property:
		Description("The direct call target, uppercase hexadecimal without 0x; omitted for an indirect call.")]
	string? Target = null,
	[property: Description("Cheat Engine's symbol name for the direct target; omitted when it has none.")]
	string? Symbol = null,
	[property:
		Description(
			"For an indirect call through one fixed memory slot, such as an import slot, the slot's address; read " +
			"it to find the target. Omitted for a register or computed target and for an FS- or GS-relative slot, " +
			"whose segment base the instruction does not hold.")]
	string? Slot = null);

/// <summary>A bounded control-flow graph of the function that starts at one entry address.</summary>
public sealed record CodeFunctionGraph(
	[property: Description("The resolved entry address, uppercase hexadecimal without 0x.")]
	string Entry,
	[property: Description("The lowest decoded instruction address, uppercase hexadecimal without 0x.")]
	string Start,
	[property: Description("The exclusive end of the highest decoded instruction, uppercase hexadecimal without 0x.")]
	string End,
	[property:
		Description(
			"Whether maxInstructions or the byte window stopped the walk before every reachable instruction was decoded.")]
	bool Truncated,
	[property: Description("The number of distinct instructions decoded.")]
	int InstructionCount,
	[property: Description("The basic blocks in address order.")]
	CodeBasicBlock[] Blocks,
	[property: Description("The call instructions in address order; calls are listed, not entered.")]
	CodeCallSite[] Calls,
	[property:
		Description(
			"Addresses control flow reached that Cheat Engine could not decode, such as unreadable memory; omitted when there are none.")]
	string[]? Undecodable = null);
