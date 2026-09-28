using System.Text.Json;

using CheatEngine.Client.Assembly;
using CheatEngine.Mcp.Core.Contract;
using CheatEngine.Mcp.Tools.Code;
using CheatEngine.SDK.Engine.Runtime;
using CheatEngine.SDK.Engine.Values;

namespace CheatEngine.Mcp.Tests.Tools.Code;

/// <summary>
///     code_get_function_graph over a byte-map disassembler: block splitting, terminators, successors, call sites, the
///     instruction and byte limits, batched dispatches with the selection-epoch recheck, and argument refusals.
/// </summary>
public sealed class CodeFunctionGraphTests
{
	private static CancellationToken Token => TestContext.Current.CancellationToken;

	[Fact]
	public void GetFunctionGraph_LoopBranchAndCalls_SplitsBlocksAtEveryLeader()
	{
		CodeGraphTarget target = new();
		EmitLoopFunction(target);
		target.Names[0x2000] = "game.Update";

		CodeFunctionGraph graph = new CodeGraphTools(target.Dispatch).GetFunctionGraph("1000",
			cancellationToken: Token);

		Assert.Equal(("1000", "1000", "1019", false, 10), (graph.Entry, graph.Start, graph.End, graph.Truncated,
			graph.InstructionCount));
		Assert.Equal(
		[
			new BlockShape("1000", "1003", 2, CodeBlockTerminator.Fallthrough, ["1003"]),
			new BlockShape("1003", "1005", 1, CodeBlockTerminator.Conditional, ["100C", "1005"]),
			new BlockShape("1005", "100C", 2, CodeBlockTerminator.Jump, ["1011"]),
			new BlockShape("100C", "1011", 2, CodeBlockTerminator.Jump, ["1003"]),
			new BlockShape("1011", "1019", 3, CodeBlockTerminator.Return, [])
		], graph.Blocks.Select(Shape));
		Assert.All(graph.Blocks, static block =>
		{
			Assert.Null(block.Instructions);
			Assert.Null(block.ExternalTargets);
			Assert.False(block.HasIndirectSuccessor);
		});
		Assert.Equal(
		[
			new CodeCallSite("1005", false, "call 2000", "2000", "game.Update"),
			new CodeCallSite("1011", true, "call qword ptr [1027]", Slot: "1027")
		], graph.Calls);
		Assert.Null(graph.Undecodable);
		Assert.Equal([0x2000UL], target.Named);
		// One decoding dispatch and one naming dispatch.
		Assert.Equal(2, target.Dispatcher.Calls);
	}

	[Fact]
	public void GetFunctionGraph_IncludeInstructions_ListsEachBlocksInstructionsInOrder()
	{
		CodeGraphTarget target = new();
		EmitLoopFunction(target);

		CodeFunctionGraph graph = new CodeGraphTools(target.Dispatch).GetFunctionGraph("1000",
			includeInstructions: true, cancellationToken: Token);

		CodeBasicBlock last = graph.Blocks[^1];
		Assert.Equal(["1011", "1017", "1018"], last.Instructions!.Select(static instruction => instruction.Address));
		Assert.Equal(new CodeInstruction("1018", "1018", "ret", string.Empty, "ret", "C3", 1),
			last.Instructions![2]);
		Assert.Equal(graph.InstructionCount, graph.Blocks.Sum(static block => block.Instructions!.Length));
		// The unnamed direct target keeps no symbol, because Cheat Engine only restated its address.
		Assert.Null(graph.Calls[0].Symbol);
	}

	[Fact]
	public void GetFunctionGraph_TailJumpOutsideTheWindow_IsExternalAndNotFollowed()
	{
		CodeGraphTarget target = new();
		ulong next = target.Emit(0x1000, "test ecx,ecx", 0x85, 0xC9);
		next = target.Emit(next, "jne 900", 0x0F, 0x85, 0xF8, 0xF8, 0xFF, 0xFF);
		target.Emit(next, "jmp 5004", 0xE9, 0xF7, 0x3F, 0x00, 0x00);

		CodeFunctionGraph graph = new CodeGraphTools(target.Dispatch).GetFunctionGraph("1000", maxBytes: 0x100,
			cancellationToken: Token);

		Assert.False(graph.Truncated);
		Assert.Equal(
		[
			new BlockShape("1000", "1008", 2, CodeBlockTerminator.Conditional, ["1008"]),
			new BlockShape("1008", "100D", 1, CodeBlockTerminator.External, [])
		], graph.Blocks.Select(Shape));
		Assert.Equal(["900"], graph.Blocks[0].ExternalTargets!);
		Assert.Equal(["5004"], graph.Blocks[1].ExternalTargets!);
		Assert.DoesNotContain(0x900UL, target.Decoded);
		Assert.DoesNotContain(0x5004UL, target.Decoded);
	}

	[Fact]
	public void GetFunctionGraph_IndirectJumpAndTrap_AreFlaggedWithoutSuccessors()
	{
		CodeGraphTarget target = new();
		ulong next = target.Emit(0x1000, "cmp eax,3", 0x83, 0xF8, 0x03);
		next = target.Emit(next, "ja 100A", 0x77, 0x03);
		next = target.Emit(next, "jmp rax", 0x48, 0xFF, 0xE0);
		target.Emit(next, "int 3", 0xCC);

		CodeFunctionGraph graph = new CodeGraphTools(target.Dispatch).GetFunctionGraph("1000",
			cancellationToken: Token);

		Assert.Equal(
		[
			new BlockShape("1000", "1005", 2, CodeBlockTerminator.Conditional, ["1008", "1005"]),
			new BlockShape("1005", "1008", 1, CodeBlockTerminator.Indirect, []),
			new BlockShape("1008", "1009", 1, CodeBlockTerminator.Trap, [])
		], graph.Blocks.Select(Shape));
		Assert.True(graph.Blocks[1].HasIndirectSuccessor);
		Assert.Empty(graph.Calls);
	}

	[Fact]
	public void GetFunctionGraph_JumpIntoItsOwnBytes_DecodesTheOverlappingInstructionAsItsOwnBlock()
	{
		CodeGraphTarget target = new();
		// jmp 1001 lands on its own displacement byte: FF C0 is inc eax.
		target.Emit(0x1000, "jmp 1001", 0xEB, 0xFF);
		target.Emit(0x1001, "inc eax", 0xFF, 0xC0);
		target.Emit(0x1003, "ret", 0xC3);

		CodeFunctionGraph graph = new CodeGraphTools(target.Dispatch).GetFunctionGraph("1000",
			cancellationToken: Token);

		Assert.Equal((3, "1004"), (graph.InstructionCount, graph.End));
		Assert.Equal(
		[
			new BlockShape("1000", "1002", 1, CodeBlockTerminator.Jump, ["1001"]),
			new BlockShape("1001", "1004", 2, CodeBlockTerminator.Return, [])
		], graph.Blocks.Select(Shape));
	}

	[Fact]
	public void Walk_OneDecodePerBatch_BuildsTheSameGraphAsOneBatch()
	{
		CodeGraphTarget target = new();
		EmitLoopFunction(target);
		FunctionGraphBuilder whole = new(0x1000, 65536, 1024, true);
		FunctionGraphBuilder stepped = new(0x1000, 65536, 1024, true);

		whole.Walk(address => Decode(target, address), 1024);
		int batches = 0;
		while (!stepped.IsComplete)
		{
			stepped.Walk(address => Decode(target, address), 1);
			batches++;
		}

		Dictionary<ulong, string> symbols = new()
		{
			[0x2000] = "game.Update"
		};
		Assert.True(whole.IsComplete);
		Assert.True(batches >= 10, $"Only {batches} batches ran.");
		Assert.Equal(
			JsonSerializer.Serialize(whole.Build(true, symbols, "probe"), CodeJsonContext.Default.CodeFunctionGraph),
			JsonSerializer.Serialize(stepped.Build(true, symbols, "probe"),
				CodeJsonContext.Default.CodeFunctionGraph));
	}

	[Fact]
	public void GetFunctionGraph_MaxInstructionsReached_IsTruncatedWithALimitBlock()
	{
		CodeGraphTarget target = new();
		ulong next = 0x1000;
		for (int index = 0; index < 8; index++)
		{
			next = target.Emit(next, "nop", 0x90);
		}

		target.Emit(next, "ret", 0xC3);

		CodeFunctionGraph graph = new CodeGraphTools(target.Dispatch).GetFunctionGraph("1000", 3,
			cancellationToken: Token);

		Assert.Equal((true, 3), (graph.Truncated, graph.InstructionCount));
		Assert.Equal([new BlockShape("1000", "1003", 3, CodeBlockTerminator.Limit, [])],
			graph.Blocks.Select(Shape));
		Assert.Equal([0x1000UL, 0x1001UL, 0x1002UL], target.Decoded);
	}

	[Fact]
	public void GetFunctionGraph_MaxInstructionsReachedBeforeAQueuedBranchTarget_EndsTheBlockAsALimit()
	{
		CodeGraphTarget target = new();
		ulong next = target.Emit(0x1000, "je 1004", 0x74, 0x02);
		next = target.Emit(next, "nop", 0x90);
		next = target.Emit(next, "nop", 0x90);
		target.Emit(next, "ret", 0xC3);

		CodeFunctionGraph graph = new CodeGraphTools(target.Dispatch).GetFunctionGraph("1000", 3,
			cancellationToken: Token);

		// 1004 is a queued branch target the walk never decoded: no block starts there, so 1002 cannot fall into it.
		Assert.Equal((true, 3), (graph.Truncated, graph.InstructionCount));
		Assert.Equal(
		[
			new BlockShape("1000", "1002", 1, CodeBlockTerminator.Conditional, ["1004", "1002"]),
			new BlockShape("1002", "1004", 2, CodeBlockTerminator.Limit, [])
		], graph.Blocks.Select(Shape));
		Assert.Null(graph.Undecodable);
		Assert.DoesNotContain(0x1004UL, target.Decoded);
	}

	[Fact]
	public void GetFunctionGraph_LinearFlowLeavesTheByteWindow_IsTruncatedWithoutDecodingBeyondIt()
	{
		CodeGraphTarget target = new();
		ulong next = 0x1000;
		for (int index = 0; index < 8; index++)
		{
			next = target.Emit(next, "nop", 0x90);
		}

		CodeFunctionGraph graph = new CodeGraphTools(target.Dispatch).GetFunctionGraph("1000", maxBytes: 4,
			cancellationToken: Token);

		Assert.Equal((true, 4, "1004"), (graph.Truncated, graph.InstructionCount, graph.End));
		Assert.Equal(CodeBlockTerminator.Limit, Assert.Single(graph.Blocks).Terminator);
		Assert.DoesNotContain(0x1004UL, target.Decoded);
	}

	[Fact]
	public void GetFunctionGraph_UndecodableBranchTarget_IsListedAndStaysASuccessor()
	{
		CodeGraphTarget target = new();
		ulong next = target.Emit(0x1000, "je 1010", 0x74, 0x0E);
		target.Emit(next, "ret", 0xC3);

		CodeFunctionGraph graph = new CodeGraphTools(target.Dispatch).GetFunctionGraph("1000",
			cancellationToken: Token);

		Assert.Equal(["1010"], graph.Undecodable!);
		Assert.Equal(["1010", "1002"], graph.Blocks[0].Successors);
		Assert.False(graph.Truncated);
		Assert.Equal(2, graph.Blocks.Length);
	}

	[Fact]
	public void GetFunctionGraph_UndecodableEntry_FailsWithTheReadFailure()
	{
		CodeGraphTarget target = new();

		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() =>
			new CodeGraphTools(target.Dispatch).GetFunctionGraph("1000", cancellationToken: Token));

		Assert.Equal(ToolErrorKind.MemoryReadFailed, exception.Error.Kind);
		Assert.Equal([0x1000UL], target.Decoded);
	}

	[Fact]
	public void GetFunctionGraph_MoreInstructionsThanOneBatch_DecodesInSeveralDispatches()
	{
		CodeGraphTarget target = new();
		ulong next = 0x10000;
		for (int index = 0; index < CodeGraphTools.DecodesPerDispatch + 10; index++)
		{
			next = target.Emit(next, "nop", 0x90);
		}

		target.Emit(next, "ret", 0xC3);

		CodeFunctionGraph graph = new CodeGraphTools(target.Dispatch).GetFunctionGraph("10000",
			cancellationToken: Token);

		Assert.Equal((false, CodeGraphTools.DecodesPerDispatch + 11), (graph.Truncated, graph.InstructionCount));
		Assert.Equal(CodeBlockTerminator.Return, Assert.Single(graph.Blocks).Terminator);
		Assert.Equal(2, target.Dispatcher.Calls);
	}

	[Fact]
	public void GetFunctionGraph_TargetChangesBetweenBatches_FailsWithTargetChanged()
	{
		CodeGraphTarget target = new();
		ulong next = 0x10000;
		for (int index = 0; index < CodeGraphTools.DecodesPerDispatch + 10; index++)
		{
			next = target.Emit(next, "nop", 0x90);
		}

		target.Emit(next, "ret", 0xC3);
		target.Epochs.Enqueue(1);
		target.Epochs.Enqueue(2);

		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() =>
			new CodeGraphTools(target.Dispatch).GetFunctionGraph("10000", cancellationToken: Token));

		Assert.Equal(ToolErrorKind.TargetChanged, exception.Error.Kind);
		Assert.Equal(CodeGraphTools.DecodesPerDispatch, target.Decoded.Count);
	}

	[Fact]
	public void GetFunctionGraph_X86Target_WrapsBranchTargetsAtFourGigabytes()
	{
		CodeGraphTarget target = new()
		{
			Architecture = CheatEngineArchitecture.X86
		};
		// In x86 0x48 is dec eax, not a REX prefix; the call's target passes 4 GiB and wraps to 1006.
		ulong next = target.Emit(0xFFFFF000, "dec eax", 0x48);
		next = target.Emit(next, "call 1006", 0xE8, 0x00, 0x20, 0x00, 0x00);
		target.Emit(next, "ret", 0xC3);

		CodeFunctionGraph graph = new CodeGraphTools(target.Dispatch).GetFunctionGraph("FFFFF000",
			cancellationToken: Token);

		Assert.Equal((3, "FFFFF007"), (graph.InstructionCount, graph.End));
		Assert.Equal(new CodeCallSite("FFFFF001", false, "call 1006", "1006"), Assert.Single(graph.Calls));
		Assert.Equal(CodeBlockTerminator.Return, Assert.Single(graph.Blocks).Terminator);
	}

	[Fact]
	public void GetFunctionGraph_UnknownArchitectureWithKnownBitness_DecodesAsIntel()
	{
		CodeGraphTarget target = new()
		{
			Architecture = CheatEngineArchitecture.Unknown,
			Bitness = PointerSize.Bit32
		};
		ulong next = target.Emit(0x1000, "dec eax", 0x48);
		target.Emit(next, "ret", 0xC3);

		CodeFunctionGraph graph = new CodeGraphTools(target.Dispatch).GetFunctionGraph("1000",
			cancellationToken: Token);

		Assert.Equal(2, graph.InstructionCount);
		Assert.Equal(CodeBlockTerminator.Return, Assert.Single(graph.Blocks).Terminator);
	}

	[Theory]
	[InlineData(TargetBackend.CEServer)]
	[InlineData(TargetBackend.FileAsProcess)]
	[InlineData(TargetBackend.Unknown)]
	public void GetFunctionGraph_UnknownArchitectureOfANonLocalTarget_IsUnsupportedWhateverItsBitness(
		TargetBackend backend)
	{
		// A CEServer target may be ARM code, whose bytes the x86 rules would misread.
		CodeGraphTarget target = new()
		{
			Architecture = CheatEngineArchitecture.Unknown,
			Bitness = PointerSize.Bit64,
			Backend = backend
		};
		target.Emit(0x1000, "ret", 0xC3);

		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() =>
			new CodeGraphTools(target.Dispatch).GetFunctionGraph("1000", cancellationToken: Token));

		Assert.Equal((ToolErrorKind.Unsupported, ToolHostEffect.NotStarted),
			(exception.Error.Kind, exception.Error.HostEffect));
		Assert.Empty(target.Decoded);
	}

	[Theory]
	[InlineData(CheatEngineArchitecture.Arm64)]
	[InlineData(CheatEngineArchitecture.Arm32)]
	[InlineData(CheatEngineArchitecture.Unknown)]
	public void GetFunctionGraph_NonIntelOrUnknownTarget_IsUnsupportedBeforeDecoding(
		CheatEngineArchitecture architecture)
	{
		CodeGraphTarget target = new()
		{
			Architecture = architecture
		};
		target.Emit(0x1000, "ret", 0xC3);

		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() =>
			new CodeGraphTools(target.Dispatch).GetFunctionGraph("1000", cancellationToken: Token));

		Assert.Equal(ToolErrorKind.Unsupported, exception.Error.Kind);
		Assert.Empty(target.Decoded);
	}

	[Theory]
	[InlineData(0, 65536, "maxInstructions")]
	[InlineData(4097, 65536, "maxInstructions")]
	[InlineData(1024, 0, "maxBytes")]
	[InlineData(1024, 1048577, "maxBytes")]
	public void GetFunctionGraph_OutOfRangeLimit_RefusesBeforeAnyDispatch(int maxInstructions, int maxBytes,
		string parameter)
	{
		CodeGraphTarget target = new();

		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() =>
			new CodeGraphTools(target.Dispatch).GetFunctionGraph("1000", maxInstructions, maxBytes,
				cancellationToken: Token));

		Assert.Equal((ToolErrorKind.InvalidArgument, ToolHostEffect.NotStarted),
			(exception.Error.Kind, exception.Error.HostEffect));
		Assert.Contains(parameter, exception.Error.Message, StringComparison.Ordinal);
		Assert.Equal(0, target.Dispatcher.Calls);
	}

	[Fact]
	public void GetFunctionGraph_X86TargetAddressBeyondFourGigabytes_IsRefusedBeforeDecoding()
	{
		CodeGraphTarget target = new()
		{
			Architecture = CheatEngineArchitecture.X86
		};

		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() =>
			new CodeGraphTools(target.Dispatch).GetFunctionGraph("100000000", cancellationToken: Token));

		Assert.Equal(ToolErrorKind.InvalidArgument, exception.Error.Kind);
		Assert.Empty(target.Decoded);
	}

	[Fact]
	public void GetFunctionGraph_UnresolvedAddress_IsNotFound()
	{
		CodeGraphTarget target = new();

		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() =>
			new CodeGraphTools(target.Dispatch).GetFunctionGraph("missing.exe+10", cancellationToken: Token));

		Assert.Equal(ToolErrorKind.NotFound, exception.Error.Kind);
		Assert.Empty(target.Decoded);
	}

	/// <summary>
	///     Emits a loop with a conditional exit, a direct call, a back edge into the middle of the entry block and an
	///     import-style indirect call:
	///     <code>
	///     1000 push rbp; 1001 test ecx,ecx; 1003 je 100C; 1005 call 2000; 100A jmp 1011;
	///     100C inc rcx; 100F jmp 1003; 1011 call [rip+10]; 1017 pop rbp; 1018 ret
	///     </code>
	/// </summary>
	private static void EmitLoopFunction(CodeGraphTarget target)
	{
		ulong next = target.Emit(0x1000, "push rbp", 0x55);
		next = target.Emit(next, "test ecx,ecx", 0x85, 0xC9);
		next = target.Emit(next, "je 100C", 0x74, 0x07);
		next = target.Emit(next, "call 2000", 0xE8, 0xF6, 0x0F, 0x00, 0x00);
		next = target.Emit(next, "jmp 1011", 0xEB, 0x05);
		next = target.Emit(next, "inc rcx", 0x48, 0xFF, 0xC1);
		next = target.Emit(next, "jmp 1003", 0xEB, 0xF2);
		next = target.Emit(next, "call qword ptr [1027]", 0xFF, 0x15, 0x10, 0x00, 0x00, 0x00);
		next = target.Emit(next, "pop rbp", 0x5D);
		target.Emit(next, "ret", 0xC3);
	}

	private static AssemblyInstructionSnapshot? Decode(CodeGraphTarget target, ulong address)
	{
		return target.Client.Assembly.TryDisassemble(new Address(address), out AssemblyInstructionSnapshot instruction,
			out _, CancellationToken.None)
			? instruction
			: null;
	}

	private static BlockShape Shape(CodeBasicBlock block)
	{
		return new BlockShape(block.Start, block.End, block.InstructionCount, block.Terminator,
			[.. block.Successors]);
	}

	private sealed record BlockShape(
		string Start,
		string End,
		int InstructionCount,
		CodeBlockTerminator Terminator,
		string[] Successors)
	{
		public bool Equals(BlockShape? other)
		{
			return other is not null && Start == other.Start && End == other.End &&
				   InstructionCount == other.InstructionCount && Terminator == other.Terminator &&
				   Successors.SequenceEqual(other.Successors);
		}

		public override int GetHashCode()
		{
			return HashCode.Combine(Start, End, InstructionCount, Terminator, Successors.Length);
		}
	}
}
