using System.Buffers.Binary;

using CheatEngine.Client;
using CheatEngine.Client.Memory;
using CheatEngine.Mcp.Core.Contract;
using CheatEngine.Mcp.Core.Values;
using CheatEngine.SDK.Engine.Values;

namespace CheatEngine.Mcp.Tools.Memory;

/// <summary>
///     The <c>byteOrder</c> of the memory reads and writes: a managed byte swap around the little-endian typed path.
///     A big-endian value is read and written as the unsigned integer of its width, so its bytes reach the target
///     unchanged, and decoded or encoded here.
/// </summary>
internal static class MemoryByteOrders
{
	/// <summary>The description of every <c>byteOrder</c> parameter and item field.</summary>
	internal const string Description =
		"The byte order of the value in memory: little_endian (default) or big_endian, as in emulated GameCube, " +
		"Wii, Wii U or PS3 memory; " + AppliesTo;

	private const string AppliesTo =
		"big_endian applies only to int16, uint16, int32, uint32, int64, uint64, float and double.";

	/// <summary>Checks a byte order against the value type before any dispatch.</summary>
	/// <param name="type">The checked value type.</param>
	/// <param name="order">The caller's byte order.</param>
	/// <param name="parameter">The parameter that carried it.</param>
	/// <returns>Whether the value's bytes are swapped.</returns>
	internal static bool Require(McpValueType type, MemoryByteOrder order, string parameter)
	{
		return order switch
		{
			MemoryByteOrder.LittleEndian => false,
			MemoryByteOrder.BigEndian when Swappable(type) => true,
			MemoryByteOrder.BigEndian => throw CheatEngineToolException.InvalidArgument(parameter, AppliesTo),
			_ => throw CheatEngineToolException.InvalidArgument(parameter, "must be little_endian or big_endian.")
		};
	}

	/// <summary>Whether a value type has a byte order: a number of 2, 4 or 8 bytes other than a pointer.</summary>
	/// <param name="type">The value type.</param>
	/// <returns><see langword="true" /> for int16 to uint64, float and double.</returns>
	internal static bool Swappable(McpValueType type)
	{
		return type is McpValueType.Int16 or McpValueType.UInt16 or McpValueType.Int32 or McpValueType.UInt32
			or McpValueType.Int64 or McpValueType.UInt64 or McpValueType.Float or McpValueType.Double;
	}

	/// <summary>Reverses encoded little-endian bytes into the big-endian bytes the target holds.</summary>
	/// <param name="bytes">The encoded value; reversed in place.</param>
	/// <returns><paramref name="bytes" />.</returns>
	internal static byte[] Swap(byte[] bytes)
	{
		Array.Reverse(bytes);
		return bytes;
	}

	/// <summary>Decodes one fixed-size value stored in the given byte order as contract text.</summary>
	/// <param name="type">A fixed-size value type; big-endian only for a swappable one.</param>
	/// <param name="bytes">At least the value's size in bytes, as the target holds them.</param>
	/// <param name="pointerBytes">The target pointer size, 4 or 8.</param>
	/// <param name="order">The byte order of <paramref name="bytes" />.</param>
	/// <returns>The value text.</returns>
	internal static string Decode(McpValueType type, ReadOnlySpan<byte> bytes, int pointerBytes,
		MemoryByteOrder order)
	{
		if (order is MemoryByteOrder.LittleEndian)
		{
			return MemoryTargets.Decode(type, bytes, pointerBytes);
		}

		int width = McpValueCodec.FixedSize(type, pointerBytes)!.Value;
		Span<byte> reversed = stackalloc byte[width];
		bytes[..width].CopyTo(reversed);
		reversed.Reverse();
		return MemoryTargets.Decode(type, reversed, pointerBytes);
	}

	/// <summary>
	///     Decodes a big-endian value from the unsigned integer of its width that the typed path read: the integer's
	///     little-endian bytes are the bytes the target holds.
	/// </summary>
	/// <param name="type">A swappable value type.</param>
	/// <param name="raw">The integer read, widened.</param>
	/// <param name="width">The value's size in bytes: 2, 4 or 8.</param>
	/// <returns>The value text.</returns>
	internal static string Format(McpValueType type, ulong raw, int width)
	{
		Span<byte> bytes = stackalloc byte[sizeof(ulong)];
		BinaryPrimitives.WriteUInt64LittleEndian(bytes, raw);
		return Decode(type, bytes[..width], sizeof(ulong), MemoryByteOrder.BigEndian);
	}

	/// <summary>Reads one big-endian value through the typed read of the unsigned integer of its width.</summary>
	/// <param name="client">The activation's Client.</param>
	/// <param name="address">The resolved address.</param>
	/// <param name="type">A swappable value type.</param>
	/// <param name="cancellationToken">The token of the enclosing dispatch body.</param>
	/// <returns>The value text.</returns>
	internal static string Read(ICheatEngineClient client, Address address, McpValueType type,
		CancellationToken cancellationToken)
	{
		int width = McpValueCodec.FixedSize(type, sizeof(ulong))!.Value;
		ulong raw = width switch
		{
			2 => client.Memory.ReadPrimitive<ushort>(address, cancellationToken),
			4 => client.Memory.ReadPrimitive<uint>(address, cancellationToken),
			_ => client.Memory.ReadPrimitive<ulong>(address, cancellationToken)
		};
		return Format(type, raw, width);
	}

	/// <summary>
	///     Writes the swapped bytes of one big-endian value through the typed write of the unsigned integer of their
	///     width.
	/// </summary>
	/// <param name="client">The activation's Client.</param>
	/// <param name="address">The resolved address.</param>
	/// <param name="bytes">The 2, 4 or 8 bytes the target must hold.</param>
	/// <param name="cancellationToken">The token of the enclosing dispatch body.</param>
	internal static void Write(ICheatEngineClient client, Address address, byte[] bytes,
		CancellationToken cancellationToken)
	{
		IMemoryClient memory = client.Memory;
		switch (bytes.Length)
		{
			case 2:
				memory.WritePrimitive(address, BinaryPrimitives.ReadUInt16LittleEndian(bytes), cancellationToken);
				break;
			case 4:
				memory.WritePrimitive(address, BinaryPrimitives.ReadUInt32LittleEndian(bytes), cancellationToken);
				break;
			default:
				memory.WritePrimitive(address, BinaryPrimitives.ReadUInt64LittleEndian(bytes), cancellationToken);
				break;
		}
	}
}
