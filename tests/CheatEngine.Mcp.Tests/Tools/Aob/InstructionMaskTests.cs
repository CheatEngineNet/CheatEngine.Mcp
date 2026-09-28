using CheatEngine.Mcp.Tools.Aob;

namespace CheatEngine.Mcp.Tests.Tools.Aob;

/// <summary>
///     The wildcards of a managed signature: position-dependent displacements, branch targets and address-sized values
///     are masked, while opcodes, structure offsets and small constants stay exact.
/// </summary>
public sealed class InstructionMaskTests
{
	[Theory]
	// mov rax,[rip+302010]: a RIP-relative displacement is always masked.
	[InlineData("48 8B 05 10 20 30 00", true, "48 8B 05 ?? ?? ?? ??")]
	// lea rcx,[rip+100]: even a small RIP-relative displacement depends on the code's position.
	[InlineData("48 8D 0D 00 01 00 00", true, "48 8D 0D ?? ?? ?? ??")]
	// mov rcx,[rax+4C8] keeps its structure offset; mov rcx,[rax+12345678] looks like an address.
	[InlineData("48 8B 88 C8 04 00 00", true, "48 8B 88 C8 04 00 00")]
	[InlineData("48 8B 88 78 56 34 12", true, "48 8B 88 ?? ?? ?? ??")]
	// mov [rbx+10],rax: a 1-byte displacement stays.
	[InlineData("48 89 43 10", true, "48 89 43 10")]
	// call rel32 and jz rel32 lose their targets; jmp rel8 keeps its short, local target.
	[InlineData("E8 10 20 30 40", true, "E8 ?? ?? ?? ??")]
	[InlineData("0F 84 10 20 30 00", true, "0F 84 ?? ?? ?? ??")]
	[InlineData("EB 05", true, "EB 05")]
	// mov eax,100 keeps a small immediate; mov eax,400000 and movabs rax,401000 mask address-sized ones.
	[InlineData("B8 64 00 00 00", true, "B8 64 00 00 00")]
	[InlineData("B8 00 00 40 00", true, "B8 ?? ?? ?? ??")]
	[InlineData("48 B8 00 10 40 00 00 00 00 00", true, "48 B8 ?? ?? ?? ?? ?? ?? ?? ??")]
	// mov dword ptr [rip+302010],1: the displacement goes, the small immediate stays.
	[InlineData("C7 05 10 20 30 00 01 00 00 00", true, "C7 05 ?? ?? ?? ?? 01 00 00 00")]
	// cmp dword ptr [rbx+rcx*4+10],5 and nop dword ptr [rax+rax+0]: nothing position-dependent.
	[InlineData("83 7C 8B 10 05", true, "83 7C 8B 10 05")]
	[InlineData("0F 1F 44 00 00", true, "0F 1F 44 00 00")]
	// mov eax,[rcx*4+401000]: a SIB byte without a base register holds an absolute table address.
	[InlineData("8B 04 8D 00 10 40 00", true, "8B 04 8D ?? ?? ?? ??")]
	// call qword ptr [rip+302010].
	[InlineData("FF 15 10 20 30 00", true, "FF 15 ?? ?? ?? ??")]
	// vmovups xmm0,[rip+x] (VEX) and vmovups zmm0,[rip+x] (EVEX).
	[InlineData("C5 F8 10 05 10 20 30 00", true, "C5 F8 10 05 ?? ?? ?? ??")]
	[InlineData("62 F1 7C 48 10 05 10 20 30 00", true, "62 F1 7C 48 10 05 ?? ?? ?? ??")]
	// palignr xmm0,xmm1,8, vzeroupper, endbr64 and a 3DNow! instruction keep every byte.
	[InlineData("66 0F 3A 0F C1 08", true, "66 0F 3A 0F C1 08")]
	[InlineData("C5 F8 77", true, "C5 F8 77")]
	[InlineData("F3 0F 1E FA", true, "F3 0F 1E FA")]
	[InlineData("0F 0F C1 9E", true, "0F 0F C1 9E")]
	// An address-size override on x64 still leaves an EIP-relative displacement.
	[InlineData("67 8B 05 10 20 30 00", true, "67 8B 05 ?? ?? ?? ??")]
	// x86: mov eax,[401000] (moffs), mov ecx,[402000] (absolute) and a far jump.
	[InlineData("A1 00 10 40 00", false, "A1 ?? ?? ?? ??")]
	[InlineData("8B 0D 00 20 40 00", false, "8B 0D ?? ?? ?? ??")]
	[InlineData("EA 00 10 40 00 08 00", false, "EA ?? ?? ?? ?? ?? ??")]
	// x86 16-bit addressing: mov eax,[bp+10] has a 1-byte displacement.
	[InlineData("67 8B 46 10", false, "67 8B 46 10")]
	// On x86, 40 is inc eax, not a REX prefix.
	[InlineData("40", false, "40")]
	public void Wildcards_MaskPositionDependentBytes(string instruction, bool is64, string expected)
	{
		byte[] bytes = Convert.FromHexString(instruction.Replace(" ", string.Empty, StringComparison.Ordinal));

		bool[] wildcards = InstructionMask.Wildcards(bytes, is64);

		Assert.Equal(expected, SignatureBuilder.Format(bytes, wildcards));
	}

	[Theory]
	// Cheat Engine reported fewer bytes than the ModRM layout needs, or only prefixes.
	[InlineData("48 8B 05 10")]
	[InlineData("48 8B")]
	[InlineData("66 F3")]
	[InlineData("0F")]
	public void Wildcards_LayoutThatDoesNotFit_KeepsEveryByte(string instruction)
	{
		byte[] bytes = Convert.FromHexString(instruction.Replace(" ", string.Empty, StringComparison.Ordinal));

		Assert.All(InstructionMask.Wildcards(bytes, true), static wildcard => Assert.False(wildcard));
	}

	[Fact]
	public void Format_WritesUppercasePairsAndWildcards()
	{
		Assert.Equal("0A ?? FF", SignatureBuilder.Format([0x0A, 0x00, 0xFF], [false, true, false]));
		Assert.Equal(string.Empty, SignatureBuilder.Format([], []));
	}
}
