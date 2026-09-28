using System.ComponentModel;

using CheatEngine.Mcp.Core.Jobs;

namespace CheatEngine.Mcp.Tools.Code;

/// <summary>One instruction copied from Cheat Engine's typed instruction client.</summary>
public sealed record CodeInstruction(
	[property: Description("The instruction address, uppercase hexadecimal without 0x.")]
	string Address,
	[property: Description("Cheat Engine's display address column.")]
	string AddressText,
	[property: Description("The mnemonic and operands as Cheat Engine displayed them.")]
	string Opcode,
	[property: Description("Cheat Engine's annotation column; empty when there is none.")]
	string Extra,
	[property: Description("Opcode and annotation as one display line.")]
	string Text,
	[property: Description("The exact target bytes decoded, uppercase hexadecimal without separators.")]
	string Bytes,
	[property: Description("The instruction's positive byte length.")]
	int Size);

/// <summary>A bounded disassembly around one requested address.</summary>
public sealed record CodeDisassembly(
	[property: Description("The resolved address supplied by the caller, uppercase hexadecimal without 0x.")]
	string Address,
	[property: Description("The instructions, from the earliest requested predecessor through the final instruction.")]
	CodeInstruction[] Instructions);

/// <summary>One exact instruction decode.</summary>
public sealed record CodeDecodeResult(
	[property: Description("The decoded instruction.")]
	CodeInstruction Instruction,
	[property: Description("Cheat Engine's exact instruction length, repeated for callers that only need the size.")]
	int Length);

/// <summary>The text Cheat Engine produced while decoding bytes that are not read from target memory.</summary>
public sealed record CodeByteDisassembly(
	[property: Description("The optional origin used for relative operands, uppercase hexadecimal without 0x.")]
	string Origin,
	[property: Description("Cheat Engine's complete disassembly text for the supplied byte sequence.")]
	string Text);

/// <summary>Cheat Engine's estimate of the function containing an address.</summary>
public sealed record CodeFunction(
	[property: Description("The resolved address supplied by the caller, uppercase hexadecimal without 0x.")]
	string Address,
	[property: Description("Whether Cheat Engine returned function boundaries.")]
	bool Found,
	[property:
		Description("The estimated function start, uppercase hexadecimal without 0x; omitted when found is false.")]
	string? StartAddress = null,
	[property:
		Description(
			"The estimated exclusive function end, uppercase hexadecimal without 0x; omitted when found is false.")]
	string? EndAddress = null,
	[property: Description("The estimated function size in bytes; omitted when found is false.")]
	long? Size = null,
	[property: Description("Whether Cheat Engine considers address a jump destination in the requested range.")]
	bool AddressIsJumpDestination = false);

/// <summary>One event retained by a code analysis job.</summary>
public sealed record CodeJobEvent(
	[property:
		Description(
			"dissect when Cheat Engine completed a code-dissection pass, or search for an instruction-text match.")]
	string Kind,
	[property:
		Description(
			"The event's address, uppercase hexadecimal without 0x: the first dissected address for a dissect event, or the matching instruction's address for a search event.")]
	string? Address = null,
	[property: Description("The matching decoded instruction, for a search event.")]
	CodeInstruction? Instruction = null,
	[property: Description("The byte range dissected, for a dissect event.")]
	int? Size = null);

/// <summary>What a code analysis job accepted.</summary>
public sealed record CodeJobStart(
	[property: Description("The job id for code_poll_job and runtime_stop_job.")]
	string JobId,
	[property: Description("The initial job status.")]
	JobStatus Job);

/// <summary>A cursor page from a code analysis job.</summary>
public sealed record CodeJobPage(
	[property: Description("The job status at the time of this poll.")]
	JobStatus Job,
	[property: Description("Events after afterSequence, oldest first.")]
	IReadOnlyList<CodeJobEvent> Items,
	[property: Description("The oldest retained event sequence.")]
	long FirstSequence,
	[property: Description("The cursor to pass as afterSequence on the next poll.")]
	long NextAfterSequence,
	[property: Description("Whether more retained events follow nextAfterSequence.")]
	bool More,
	[property: Description("How many old events were evicted by the job buffer.")]
	long Dropped);

/// <summary>One reference copied from Cheat Engine's current code dissector.</summary>
public sealed record CodeReference(
	[property: Description("The instruction that refers to the target, uppercase hexadecimal without 0x.")]
	string FromAddress,
	[property: Description("The referenced target, uppercase hexadecimal without 0x.")]
	string ToAddress,
	[property: Description("Cheat Engine's reference kind.")]
	string Kind);

/// <summary>A bounded, cursor-like page of code references.</summary>
public sealed record CodeReferencePage(
	[property: Description("The requested target address, uppercase hexadecimal without 0x.")]
	string Address,
	[property: Description("How many references Cheat Engine reported before the bounded copy stopped.")]
	int Total,
	[property: Description("Whether total and the page cover every reference in the current code dissector.")]
	bool Exact,
	[property: Description("The requested zero-based offset.")]
	int Offset,
	[property: Description("The bounded page of references.")]
	CodeReference[] References,
	[property: Description("The next offset, omitted after the final exact page.")]
	int? NextOffset = null);

/// <summary>One string copied from Cheat Engine's current code dissector.</summary>
public sealed record CodeString(
	[property: Description("The address of the referenced string, uppercase hexadecimal without 0x.")]
	string Address,
	[property: Description("The referenced string as Cheat Engine reported it.")]
	string Text);

/// <summary>A bounded page of strings referenced by the current code dissector.</summary>
public sealed record CodeStringPage(
	[property:
		Description(
			"How many matching strings were copied before the bounded enumeration stopped; it is a lower bound when exact is false.")]
	int Total,
	[property: Description("Whether total and the page cover every matching string.")]
	bool Exact,
	[property: Description("The requested zero-based offset.")]
	int Offset,
	[property: Description("The matching strings.")]
	CodeString[] Strings,
	[property: Description("The next offset within the retained page set, omitted after its final page.")]
	int? NextOffset = null);

/// <summary>A bounded page of function addresses referenced by the current code dissector.</summary>
public sealed record CodeFunctionPage(
	[property: Description("The total functions Cheat Engine currently reports.")]
	int Total,
	[property: Description("The requested zero-based offset.")]
	int Offset,
	[property: Description("The function addresses, uppercase hexadecimal without 0x.")]
	string[] Functions,
	[property: Description("The next offset, omitted after the final page.")]
	int? NextOffset = null);

/// <summary>One code-view comment.</summary>
public sealed record CodeComment(
	[property: Description("The resolved address, uppercase hexadecimal without 0x.")]
	string Address,
	[property: Description("The user comment, or null when no comment is set.")]
	string? Comment);

/// <summary>The comments for a bounded batch of addresses.</summary>
public sealed record CodeComments(
	[property: Description("One result for each requested address, in request order.")]
	CodeComment[] Comments);

/// <summary>The result of clearing Cheat Engine's code dissector.</summary>
public sealed record CodeClearResult(
	[property: Description("Whether Cheat Engine completed the clear operation.")]
	bool Cleared);
