using CheatEngine.Mcp.Tools.Code;

namespace CheatEngine.Mcp.Tests.Tools.Code;

/// <summary>The x86 and x64 branch encodings the function graph relies on, decoded from exact instruction bytes.</summary>
public sealed class BranchDecoderTests
{
	[Theory]
	[InlineData("E8 FB 0F 00 00", 0x1000UL, true, 0x2000UL)]
	[InlineData("E9 00 00 00 80", 0x7FF6_0000_1000UL, true, 0x7FF5_8000_1005UL)]
	[InlineData("E9 00 20 00 00", 0xFFFF_F000UL, false, 0x1005UL)]
	[InlineData("E8 00 20 00 00", 0xFFFF_FFFF_FFFF_F000UL, true, 0x1005UL)]
	[InlineData("F2 E9 10 00 00 00", 0x1000UL, true, 0x1016UL)]
	public void Decode_RelativeCallAndJump_WrapToTheAddressWidth(string bytes, ulong address, bool is64,
		ulong target)
	{
		BranchInfo branch = BranchDecoder.Decode(Convert.FromHexString(bytes.Replace(" ", "")), address, is64);

		Assert.Equal(target, branch.Target);
		Assert.False(branch.Indirect);
		Assert.True(branch.Kind is BranchKind.Call or BranchKind.Jump);
	}

	[Theory]
	[InlineData("EB FE", 0x1000UL, nameof(BranchKind.Jump), 0x1000UL)]
	[InlineData("74 F0", 0x1000UL, nameof(BranchKind.Conditional), 0xFF2UL)]
	[InlineData("0F 84 00 01 00 00", 0x1000UL, nameof(BranchKind.Conditional), 0x1106UL)]
	[InlineData("E2 FC", 0x1000UL, nameof(BranchKind.Conditional), 0xFFEUL)]
	[InlineData("E3 10", 0x1000UL, nameof(BranchKind.Conditional), 0x1012UL)]
	[InlineData("67 E3 10", 0x1000UL, nameof(BranchKind.Conditional), 0x1013UL)]
	[InlineData("66 E9 F0 FF", 0x401000UL, nameof(BranchKind.Jump), 0x0FF4UL)]
	public void Decode_ShortAndConditionalForms_ComputeTheTargetFromTheTrailingDisplacement(string bytes,
		ulong address, string kind, ulong target)
	{
		BranchInfo branch = BranchDecoder.Decode(Convert.FromHexString(bytes.Replace(" ", "")), address, false);

		Assert.Equal(new BranchInfo(Enum.Parse<BranchKind>(kind), target), branch);
	}

	[Theory]
	[InlineData("C3", nameof(BranchKind.Return))]
	[InlineData("C2 08 00", nameof(BranchKind.Return))]
	[InlineData("CB", nameof(BranchKind.Return))]
	[InlineData("48 CF", nameof(BranchKind.Return))]
	[InlineData("F3 C3", nameof(BranchKind.Return))]
	[InlineData("CC", nameof(BranchKind.Trap))]
	[InlineData("F4", nameof(BranchKind.Trap))]
	[InlineData("0F 0B", nameof(BranchKind.Trap))]
	[InlineData("CD 29", nameof(BranchKind.Trap))]
	[InlineData("CD 2E", nameof(BranchKind.None))]
	[InlineData("90", nameof(BranchKind.None))]
	[InlineData("48 8B 05 10 00 00 00", nameof(BranchKind.None))]
	[InlineData("0F 05", nameof(BranchKind.None))]
	[InlineData("FF 30", nameof(BranchKind.None))]
	[InlineData("C4 E2 79 18 05 00 00 00 00", nameof(BranchKind.None))]
	public void Decode_ReturnsTrapsAndPlainInstructions_AreClassifiedWithoutATarget(string bytes, string kind)
	{
		BranchInfo branch = BranchDecoder.Decode(Convert.FromHexString(bytes.Replace(" ", "")), 0x1000, true);

		Assert.Equal(new BranchInfo(Enum.Parse<BranchKind>(kind)), branch);
	}

	[Theory]
	[InlineData("FF 15 10 00 00 00", true, nameof(BranchKind.Call), 0x1016UL)]
	[InlineData("FF 25 F0 FF FF FF", true, nameof(BranchKind.Jump), 0xFF6UL)]
	[InlineData("FF 15 00 20 40 00", false, nameof(BranchKind.Call), 0x402000UL)]
	[InlineData("FF 24 25 00 30 40 00", true, nameof(BranchKind.Jump), 0x403000UL)]
	[InlineData("48 FF 25 10 00 00 00", true, nameof(BranchKind.Jump), 0x1017UL)]
	[InlineData("3E FF 15 00 20 40 00", false, nameof(BranchKind.Call), 0x402000UL)]
	[InlineData("2E FF 25 10 00 00 00", true, nameof(BranchKind.Jump), 0x1017UL)]
	public void Decode_IndirectThroughOneMemorySlot_ReportsTheSlotAddress(string bytes, bool is64, string kind,
		ulong slot)
	{
		BranchInfo branch = BranchDecoder.Decode(Convert.FromHexString(bytes.Replace(" ", "")), 0x1000, is64);

		Assert.Equal(new BranchInfo(Enum.Parse<BranchKind>(kind), null, true, slot), branch);
	}

	[Theory]
	[InlineData("FF E0", true, nameof(BranchKind.Jump))]
	[InlineData("48 FF E0", true, nameof(BranchKind.Jump))]
	[InlineData("3E FF E0", true, nameof(BranchKind.Jump))]
	[InlineData("41 FF D3", true, nameof(BranchKind.Call))]
	[InlineData("FF 14 C5 00 10 00 00", true, nameof(BranchKind.Call))]
	[InlineData("42 FF 24 25 00 30 40 00", true, nameof(BranchKind.Jump))]
	[InlineData("67 FF 25 10 00 00 00", true, nameof(BranchKind.Jump))]
	[InlineData("FF 1D 10 00 00 00", true, nameof(BranchKind.Call))]
	[InlineData("FF 2D 10 00 00 00", true, nameof(BranchKind.Jump))]
	[InlineData("9A 00 10 40 00 23 00", false, nameof(BranchKind.Call))]
	[InlineData("EA 00 10 40 00 23 00", false, nameof(BranchKind.Jump))]
	[InlineData("E9 00 00 00", true, nameof(BranchKind.Jump))]
	public void Decode_RegisterFarOrMalformedTransfers_AreIndirectWithoutASlot(string bytes, bool is64,
		string kind)
	{
		BranchInfo branch = BranchDecoder.Decode(Convert.FromHexString(bytes.Replace(" ", "")), 0x1000, is64);

		Assert.Equal(new BranchInfo(Enum.Parse<BranchKind>(kind), null, true), branch);
	}

	[Theory]
	[InlineData("64 FF 15 C0 00 00 00", false, nameof(BranchKind.Call))]
	[InlineData("64 FF 25 00 10 00 00", false, nameof(BranchKind.Jump))]
	[InlineData("65 FF 14 25 30 00 00 00", true, nameof(BranchKind.Call))]
	[InlineData("65 FF 15 10 00 00 00", true, nameof(BranchKind.Call))]
	[InlineData("65 48 FF 25 10 00 00 00", true, nameof(BranchKind.Jump))]
	[InlineData("F2 64 FF 15 C0 00 00 00", false, nameof(BranchKind.Call))]
	public void Decode_FsOrGsRelativeSlot_IsIndirectWithoutASlotAddress(string bytes, bool is64, string kind)
	{
		// call fs:[C0] reads TEB+C0, not address C0: the segment base is not in the instruction.
		BranchInfo branch = BranchDecoder.Decode(Convert.FromHexString(bytes.Replace(" ", "")), 0x1000, is64);

		Assert.Equal(new BranchInfo(Enum.Parse<BranchKind>(kind), null, true), branch);
	}

	[Fact]
	public void Decode_RexBytesOnX86_AreIncAndDecRatherThanPrefixes()
	{
		Assert.Equal(default, BranchDecoder.Decode([0x48], 0x1000, false));
		Assert.Equal(default, BranchDecoder.Decode([0x40], 0x1000, false));
		Assert.Equal(default, BranchDecoder.Decode([0x9A, 0, 0, 0, 0, 0, 0], 0x1000, true));
	}

	[Fact]
	public void Decode_PrefixesOnly_IsNotATransfer()
	{
		Assert.Equal(default, BranchDecoder.Decode([0x66, 0x48], 0x1000, true));
		Assert.Equal(default, BranchDecoder.Decode([0x0F], 0x1000, true));
		Assert.Equal(default, BranchDecoder.Decode([0xFF], 0x1000, true));
		Assert.Equal(default, BranchDecoder.Decode([], 0x1000, true));
	}

	[Theory]
	[InlineData(0x1000UL, 5, true, 0x1005UL)]
	[InlineData(0xFFFF_FFFEUL, 5, false, 0x3UL)]
	[InlineData(0xFFFF_FFFEUL, 5, true, 0x1_0000_0003UL)]
	public void Next_WrapsToTheAddressWidth(ulong address, int length, bool is64, ulong expected)
	{
		Assert.Equal(expected, BranchDecoder.Next(address, length, is64));
	}
}
