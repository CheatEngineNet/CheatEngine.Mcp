using System.Buffers.Binary;

namespace CheatEngine.Mcp.Tools.Aob;

/// <summary>
///     Chooses the bytes of one x86 or x64 instruction that a managed signature wildcards because a rebuild or a
///     relocation is likely to change them. Cheat Engine's typed disassembly gives the exact length and bytes but no
///     operand layout, so the layout is decoded here: legacy prefixes, REX, VEX, EVEX and XOP prefixes, the opcode
///     map, the ModRM byte, the SIB byte and the displacement. Everything after the displacement up to Cheat Engine's
///     length is the immediate, so no immediate-size table is needed.
/// </summary>
/// <remarks>
///     <para>
///         Wildcarded: a RIP-relative displacement and an absolute 32-bit displacement (no base register), the rel16
///         or rel32 of a near call, jump or conditional jump, the address of a <c>moffs</c> move (<c>A0</c> to
///         <c>A3</c>) and of an x86 far call or jump, and any other 4- or 8-byte displacement or immediate whose signed
///         value is at least 0x10000 away from zero, the threshold Cheat Engine's <c>getUniqueAOB</c> uses
///         (<c>frmautoinjectunit.pas</c> <c>TDisassemblyLine.GetMaskFlags</c>). Kept: prefixes, opcodes, ModRM and
///         SIB bytes, 1- and 2-byte displacements and immediates (structure offsets, short branches), and small 4-byte
///         ones.
///     </para>
///     <para>
///         An encoding whose layout does not fit the length Cheat Engine reported keeps every byte, which still matches
///         the current build.
///     </para>
/// </remarks>
internal static class InstructionMask
{
	/// <summary>The smallest distance from zero at which a 4- or 8-byte value is treated as an address.</summary>
	internal const long AddressThreshold = 0x10000;

	/// <summary>
	///     One-byte opcodes followed by a ModRM byte; <c>62</c>, <c>C4</c> and <c>C5</c> count here only when they are
	///     BOUND, LES and LDS rather than an EVEX or VEX prefix.
	/// </summary>
	private static ReadOnlySpan<byte> OneByteModRm =>
	[
		0x00, 0x01, 0x02, 0x03, 0x08, 0x09, 0x0A, 0x0B, 0x10, 0x11, 0x12, 0x13, 0x18, 0x19, 0x1A, 0x1B,
		0x20, 0x21, 0x22, 0x23, 0x28, 0x29, 0x2A, 0x2B, 0x30, 0x31, 0x32, 0x33, 0x38, 0x39, 0x3A, 0x3B,
		0x62, 0x63, 0x69, 0x6B, 0x80, 0x81, 0x82, 0x83, 0x84, 0x85, 0x86, 0x87, 0x88, 0x89, 0x8A, 0x8B,
		0x8C, 0x8D, 0x8E, 0x8F, 0xC0, 0xC1, 0xC4, 0xC5, 0xC6, 0xC7, 0xD0, 0xD1, 0xD2, 0xD3, 0xD8, 0xD9,
		0xDA, 0xDB, 0xDC, 0xDD, 0xDE, 0xDF, 0xF6, 0xF7, 0xFE, 0xFF
	];

	/// <summary>
	///     Two-byte opcodes (after <c>0F</c>) without a ModRM byte: system instructions, <c>emms</c>, the rel32
	///     conditional jumps, the segment pushes and pops, <c>cpuid</c>, <c>rsm</c> and <c>bswap</c>.
	/// </summary>
	private static ReadOnlySpan<byte> TwoByteWithoutModRm =>
	[
		0x04, 0x05, 0x06, 0x07, 0x08, 0x09, 0x0A, 0x0B, 0x0C, 0x0E, 0x30, 0x31, 0x32, 0x33, 0x34, 0x35, 0x36,
		0x37, 0x77, 0x80, 0x81, 0x82, 0x83, 0x84, 0x85, 0x86, 0x87, 0x88, 0x89, 0x8A, 0x8B, 0x8C, 0x8D, 0x8E,
		0x8F, 0xA0, 0xA1, 0xA2, 0xA8, 0xA9, 0xAA, 0xC8, 0xC9, 0xCA, 0xCB, 0xCC, 0xCD, 0xCE, 0xCF
	];

	/// <summary>How the bytes after the displacement are used.</summary>
	private enum Tail
	{
		/// <summary>An immediate operand, or nothing.</summary>
		Immediate,

		/// <summary>The displacement of a near call, jump or conditional jump.</summary>
		Relative,

		/// <summary>An absolute address: a <c>moffs</c> operand or an x86 far pointer.</summary>
		Absolute
	}

	/// <summary>Decides which bytes of one instruction a signature wildcards.</summary>
	/// <param name="instruction">The instruction's exact bytes, as long as Cheat Engine reported its length.</param>
	/// <param name="is64">Whether the target is x64 rather than x86.</param>
	/// <returns>One flag per byte; <see langword="true" /> for a wildcard.</returns>
	internal static bool[] Wildcards(ReadOnlySpan<byte> instruction, bool is64)
	{
		bool[] wildcards = new bool[instruction.Length];
		if (Decode(instruction, is64) is not { } layout)
		{
			return wildcards;
		}

		if (layout.DisplacementLength == 4 &&
			(layout.FixedDisplacement || IsAddress(instruction.Slice(layout.DisplacementStart, 4))))
		{
			wildcards.AsSpan(layout.DisplacementStart, 4).Fill(true);
		}

		int tailLength = instruction.Length - layout.TailStart;
		bool masked = layout.Tail is Tail.Relative or Tail.Absolute
			? tailLength >= 2
			: tailLength is 4 or 8 && IsAddress(instruction[layout.TailStart..]);
		if (masked)
		{
			wildcards.AsSpan(layout.TailStart, tailLength).Fill(true);
		}

		return wildcards;
	}

	/// <summary>
	///     Decodes where the displacement and the tail of one instruction lie, or <see langword="null" /> when the
	///     encoding does not fit the reported length.
	/// </summary>
	private static Layout? Decode(ReadOnlySpan<byte> bytes, bool is64)
	{
		int index = 0;
		bool addressOverride = false;
		while (index < bytes.Length)
		{
			byte prefix = bytes[index];
			if (prefix is 0x66 or 0xF0 or 0xF2 or 0xF3 or 0x2E or 0x36 or 0x3E or 0x26 or 0x64 or 0x65 ||
				(is64 && prefix is >= 0x40 and <= 0x4F))
			{
				index++;
			}
			else if (prefix == 0x67)
			{
				addressOverride = true;
				index++;
			}
			else
			{
				break;
			}
		}

		if (index >= bytes.Length)
		{
			return null;
		}

		byte opcode = bytes[index];
		bool modRm;
		Tail tail = Tail.Immediate;
		if (TryExtendedPrefix(bytes, index, is64, out int prefixLength, out int map))
		{
			index += prefixLength;
			if (index >= bytes.Length)
			{
				return null;
			}

			// VZEROUPPER and VZEROALL (VEX 0F 77) are the only extended-prefix opcodes without a ModRM byte.
			modRm = !(map == 1 && bytes[index] == 0x77);
			index++;
		}
		else if (opcode == 0x0F)
		{
			if (++index >= bytes.Length)
			{
				return null;
			}

			byte second = bytes[index++];
			if (second is 0x38 or 0x3A)
			{
				// Three-byte maps: an opcode byte, then always a ModRM byte.
				if (index++ >= bytes.Length)
				{
					return null;
				}

				modRm = true;
			}
			else
			{
				// 0F 0F (3DNow!) has a ModRM byte and a trailing opcode byte, which the tail keeps.
				modRm = second == 0x0F || !TwoByteWithoutModRm.Contains(second);
				tail = second is >= 0x80 and <= 0x8F ? Tail.Relative : Tail.Immediate;
			}
		}
		else
		{
			index++;
			modRm = OneByteModRm.Contains(opcode);
			tail = opcode switch
			{
				0xE8 or 0xE9 => Tail.Relative,
				>= 0xA0 and <= 0xA3 => Tail.Absolute,
				0x9A or 0xEA when !is64 => Tail.Absolute,
				_ => Tail.Immediate
			};
		}

		int displacementStart = index;
		int displacementLength = 0;
		bool fixedDisplacement = false;
		if (modRm)
		{
			if (index >= bytes.Length)
			{
				return null;
			}

			byte modrm = bytes[index++];
			int mod = modrm >> 6;
			int rm = modrm & 7;
			if (mod != 3)
			{
				if (!is64 && addressOverride)
				{
					// 16-bit addressing: no SIB byte, and [disp16] replaces [bp].
					displacementLength = mod switch
					{
						0 when rm == 6 => 2,
						1 => 1,
						2 => 2,
						_ => 0
					};
				}
				else
				{
					bool noBase = false;
					if (rm == 4)
					{
						if (index >= bytes.Length)
						{
							return null;
						}

						noBase = mod == 0 && (bytes[index] & 7) == 5;
						index++;
					}

					// [rip+disp32] on x64, [disp32] on x86, and [index*scale+disp32] hold a position, not an offset.
					fixedDisplacement = mod == 0 && (rm == 5 || noBase);
					displacementLength = fixedDisplacement
						? 4
						: mod switch
						{
							1 => 1,
							2 => 4,
							_ => 0
						};
				}
			}

			displacementStart = index;
			index += displacementLength;
		}

		return index <= bytes.Length
			? new Layout(displacementStart, displacementLength, fixedDisplacement, index, tail)
			: null;
	}

	/// <summary>
	///     Recognizes a VEX (<c>C4</c>, <c>C5</c>), EVEX (<c>62</c>) or XOP (<c>8F</c>) prefix. Outside x64 the first
	///     three are LES, LDS and BOUND unless the next byte's mod field is 11, and <c>8F</c> is POP unless the map
	///     field is at least 8.
	/// </summary>
	private static bool TryExtendedPrefix(ReadOnlySpan<byte> bytes, int index, bool is64, out int length,
		out int map)
	{
		length = 0;
		map = 0;
		if (index + 1 >= bytes.Length)
		{
			return false;
		}

		byte next = bytes[index + 1];
		bool extended = is64 || (next & 0xC0) == 0xC0;
		switch (bytes[index])
		{
			case 0xC5 when extended:
				(length, map) = (2, 1);
				return true;
			case 0xC4 when extended:
				(length, map) = (3, next & 0x1F);
				return true;
			case 0x62 when extended:
				(length, map) = (4, next & 0x07);
				return true;
			case 0x8F when (next & 0x1F) >= 8:
				(length, map) = (3, next & 0x1F);
				return true;
			default:
				return false;
		}
	}

	/// <summary>Whether a 4- or 8-byte little-endian value is at least <see cref="AddressThreshold" /> from zero.</summary>
	private static bool IsAddress(ReadOnlySpan<byte> value)
	{
		long signed = value.Length == 8
			? BinaryPrimitives.ReadInt64LittleEndian(value)
			: BinaryPrimitives.ReadInt32LittleEndian(value);
		return signed is >= AddressThreshold or <= -AddressThreshold;
	}

	/// <summary>Where the parts of one decoded instruction lie.</summary>
	/// <param name="DisplacementStart">The index of the displacement's first byte.</param>
	/// <param name="DisplacementLength">The displacement's length: 0, 1, 2 or 4.</param>
	/// <param name="FixedDisplacement">Whether the displacement holds a position rather than a register offset.</param>
	/// <param name="TailStart">The index of the first byte after the displacement.</param>
	/// <param name="Tail">How the tail is used.</param>
	private readonly record struct Layout(
		int DisplacementStart,
		int DisplacementLength,
		bool FixedDisplacement,
		int TailStart,
		Tail Tail);
}
