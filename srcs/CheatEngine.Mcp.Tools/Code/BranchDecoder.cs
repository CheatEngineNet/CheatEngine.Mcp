using System.Buffers.Binary;

namespace CheatEngine.Mcp.Tools.Code;

/// <summary>How an x86 or x64 instruction transfers control, as <see cref="BranchDecoder" /> classified it.</summary>
internal enum BranchKind
{
	/// <summary>Execution continues at the next instruction.</summary>
	None,

	/// <summary>A near or far call; execution is assumed to return to the next instruction.</summary>
	Call,

	/// <summary>An unconditional jump.</summary>
	Jump,

	/// <summary>A conditional jump, a loop or a jump on a zero count register: the target or the next instruction.</summary>
	Conditional,

	/// <summary>A near or far return, or an interrupt return.</summary>
	Return,

	/// <summary>An instruction that faults or halts: int3, int 29h (fast fail), ud0, ud1, ud2 or hlt.</summary>
	Trap
}

/// <summary>The control transfer of one decoded instruction.</summary>
/// <param name="Kind">How the instruction transfers control.</param>
/// <param name="Target">The direct target, or <see langword="null" /> for an indirect transfer or none.</param>
/// <param name="Indirect">Whether the target comes from a register, from memory or from an unknown encoding.</param>
/// <param name="Slot">
///     For an indirect near call or jump through one fixed memory slot (RIP-relative on x64, absolute on x86), the
///     address of that slot; otherwise <see langword="null" />, also behind an address-size override or an FS or GS
///     segment override, whose slot is not the plain address the bytes give.
/// </param>
internal readonly record struct BranchInfo(
	BranchKind Kind,
	ulong? Target = null,
	bool Indirect = false,
	ulong? Slot = null);

/// <summary>
///     Classifies the control transfer of one x86 or x64 instruction from its exact bytes. Cheat Engine's typed
///     disassembly gives the length and bytes but no branch facts, so the few branch encodings are decoded here: legacy
///     prefixes and (x64) REX are skipped, relative displacements are the bytes after the opcode, and targets wrap to
///     the target's address width.
/// </summary>
internal static class BranchDecoder
{
	private const ulong AddressMask32 = 0xFFFF_FFFFUL;

	/// <summary>Decodes the control transfer of one instruction.</summary>
	/// <param name="bytes">The instruction's exact bytes, as long as Cheat Engine reported its length.</param>
	/// <param name="address">The instruction's address.</param>
	/// <param name="is64">Whether the target is x64 rather than x86.</param>
	/// <returns>The transfer; <see cref="BranchKind.None" /> for any other instruction.</returns>
	internal static BranchInfo Decode(ReadOnlySpan<byte> bytes, ulong address, bool is64)
	{
		int index = 0;
		// An address-size override (67) truncates a memory operand's address, and an FS or GS override (64, 65) adds
		// a segment base, such as the TEB's, that the bytes do not hold: behind either, a slot is not a plain address.
		bool plainSlot = true;
		bool rexIndex = false;
		while (index < bytes.Length)
		{
			byte prefix = bytes[index];
			if (prefix is 0x66 or 0x67 or 0xF0 or 0xF2 or 0xF3 or 0x2E or 0x36 or 0x3E or 0x26 or 0x64 or 0x65)
			{
				plainSlot &= prefix is not (0x67 or 0x64 or 0x65);
				// A legacy prefix after REX makes that REX void.
				rexIndex = false;
				index++;
			}
			else if (is64 && prefix is >= 0x40 and <= 0x4F)
			{
				rexIndex = (prefix & 0x02) != 0;
				index++;
			}
			else
			{
				break;
			}
		}

		if (index >= bytes.Length)
		{
			return default;
		}

		ulong next = Wrap(unchecked(address + (ulong) bytes.Length), is64);
		byte opcode = bytes[index];
		switch (opcode)
		{
			case 0xC3 or 0xC2 or 0xCB or 0xCA or 0xCF:
				return new BranchInfo(BranchKind.Return);
			case 0xCC or 0xF4:
				return new BranchInfo(BranchKind.Trap);
			case 0xCD:
				return index + 1 < bytes.Length && bytes[index + 1] == 0x29
					? new BranchInfo(BranchKind.Trap)
					: default;
			case 0xE8:
				return Relative(BranchKind.Call, bytes, index + 1, next, is64);
			case 0xE9 or 0xEB:
				return Relative(BranchKind.Jump, bytes, index + 1, next, is64);
			case (>= 0x70 and <= 0x7F) or (>= 0xE0 and <= 0xE3):
				return Relative(BranchKind.Conditional, bytes, index + 1, next, is64);
			case 0x0F:
				return TwoByte(bytes, index + 1, next, is64);
			case 0xFF:
				return Group5(bytes, index + 1, next, is64, plainSlot, rexIndex);
			case 0x9A when !is64:
				return new BranchInfo(BranchKind.Call, Indirect: true);
			case 0xEA when !is64:
				return new BranchInfo(BranchKind.Jump, Indirect: true);
			default:
				return default;
		}
	}

	/// <summary>The address that follows an instruction, wrapped to the target's address width.</summary>
	/// <param name="address">The instruction's address.</param>
	/// <param name="length">The instruction's positive length.</param>
	/// <param name="is64">Whether the target is x64 rather than x86.</param>
	/// <returns>The next address.</returns>
	internal static ulong Next(ulong address, int length, bool is64)
	{
		return Wrap(unchecked(address + (ulong) length), is64);
	}

	private static BranchInfo TwoByte(ReadOnlySpan<byte> bytes, int index, ulong next, bool is64)
	{
		if (index >= bytes.Length)
		{
			return default;
		}

		byte opcode = bytes[index];
		if (opcode is >= 0x80 and <= 0x8F)
		{
			return Relative(BranchKind.Conditional, bytes, index + 1, next, is64);
		}

		return opcode is 0x0B or 0xB9 or 0xFF ? new BranchInfo(BranchKind.Trap) : default;
	}

	private static BranchInfo Group5(ReadOnlySpan<byte> bytes, int index, ulong next, bool is64, bool plainSlot,
		bool rexIndex)
	{
		if (index >= bytes.Length)
		{
			return default;
		}

		byte modrm = bytes[index];
		int operation = (modrm >> 3) & 7;
		BranchKind kind = operation switch
		{
			2 or 3 => BranchKind.Call,
			4 or 5 => BranchKind.Jump,
			_ => BranchKind.None
		};
		if (kind is BranchKind.None)
		{
			return default;
		}

		// Only the near forms (/2, /4) read a plain code pointer from their slot.
		ulong? slot = operation is 2 or 4 && plainSlot
			? Slot(bytes, index + 1, modrm, next, is64, rexIndex)
			: null;
		return new BranchInfo(kind, Indirect: true, Slot: slot);
	}

	private static ulong? Slot(ReadOnlySpan<byte> bytes, int index, byte modrm, ulong next, bool is64,
		bool rexIndex)
	{
		int mode = modrm >> 6;
		int memory = modrm & 7;
		if (mode != 0)
		{
			return null;
		}

		if (memory == 5)
		{
			// disp32: RIP-relative on x64, an absolute address on x86.
			if (index + 4 > bytes.Length)
			{
				return null;
			}

			int displacement = BinaryPrimitives.ReadInt32LittleEndian(bytes[index..]);
			return is64 ? unchecked(next + (ulong) (long) displacement) : (uint) displacement;
		}

		if (memory != 4 || index + 5 > bytes.Length)
		{
			return null;
		}

		// A SIB byte with neither base nor index is an absolute disp32.
		byte sib = bytes[index];
		if ((sib & 7) != 5 || ((sib >> 3) & 7) != 4 || rexIndex)
		{
			return null;
		}

		int absolute = BinaryPrimitives.ReadInt32LittleEndian(bytes[(index + 1)..]);
		return is64 ? unchecked((ulong) (long) absolute) : (uint) absolute;
	}

	private static BranchInfo Relative(BranchKind kind, ReadOnlySpan<byte> bytes, int index, ulong next, bool is64)
	{
		int size = bytes.Length - index;
		long displacement;
		switch (size)
		{
			case 1:
				displacement = unchecked((sbyte) bytes[index]);
				break;
			case 2:
				displacement = BinaryPrimitives.ReadInt16LittleEndian(bytes[index..]);
				break;
			case 4:
				displacement = BinaryPrimitives.ReadInt32LittleEndian(bytes[index..]);
				break;
			default:
				// A length the relative forms never have: report the transfer without guessing a target.
				return new BranchInfo(kind, Indirect: true);
		}

		ulong target = unchecked(next + (ulong) displacement);
		// An operand-size override truncates the instruction pointer to 16 bits.
		target = size == 2 ? target & 0xFFFF : Wrap(target, is64);
		return new BranchInfo(kind, target);
	}

	private static ulong Wrap(ulong value, bool is64)
	{
		return is64 ? value : value & AddressMask32;
	}
}
