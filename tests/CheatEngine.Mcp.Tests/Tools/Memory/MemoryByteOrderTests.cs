using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Reflection;
using System.Text.Json;

using CheatEngine.Client.Memory;
using CheatEngine.Mcp.Core.Contract;
using CheatEngine.Mcp.Core.Values;
using CheatEngine.Mcp.Tools.Memory;
using CheatEngine.SDK.Engine.Values;

using Xunit.Sdk;

namespace CheatEngine.Mcp.Tests.Tools.Memory;

/// <summary>
///     The <c>byteOrder</c> of the memory reads and writes: big-endian values travel as the unsigned integer of their
///     width, swapped in managed code, and round-trip through the target's bytes; other types refuse it.
/// </summary>
public sealed class MemoryByteOrderTests
{
	private const ulong Start = 0x1000;

	private static CancellationToken Token => TestContext.Current.CancellationToken;

	public static TheoryData<McpValueType, string, string> BigEndianValues => new()
	{
		{ McpValueType.Int16, "-300", "FE D4" },
		{ McpValueType.UInt16, "60000", "EA 60" },
		{ McpValueType.Int32, "1337", "00 00 05 39" },
		{ McpValueType.UInt32, "4000000000", "EE 6B 28 00" },
		{ McpValueType.Int64, "-9000000000", "FF FF FF FD E7 8E E6 00" },
		{ McpValueType.UInt64, "18000000000000000000", "F9 CC D8 A1 C5 08 00 00" },
		{ McpValueType.Float, "1.5", "3F C0 00 00" },
		{ McpValueType.Double, "-0.25", "BF D0 00 00 00 00 00 00" }
	};

	[Theory]
	[MemberData(nameof(BigEndianValues))]
	public void Read_BigEndianValue_ReadsTheUnsignedIntegerOfItsWidthAndDecodesTheSwappedBytes(McpValueType type,
		string expected, string stored)
	{
		TargetDouble target = new();
		ByteMemory memory = new();
		memory.Store(Start, stored);
		target.Memory = memory.Serve;

		MemoryReadResult result = new MemoryReadTools(target.Dispatch).Read("1000", type,
			byteOrder: MemoryByteOrder.BigEndian, cancellationToken: Token);

		Assert.Equal(new MemoryReadResult("1000", type, expected), result);
		Assert.Equal([$"Memory.ReadPrimitive<{Unsigned(stored).Name}>"], target.CallsTo("Memory"));
	}

	[Fact]
	public void Read_BigEndianCount_ReadsOneRangeAndSwapsEachValue()
	{
		TargetDouble target = new();
		ByteMemory memory = new();
		memory.Store(Start, "00 01 FF FF 00 2A");
		target.Memory = memory.Serve;

		MemoryReadResult result = new MemoryReadTools(target.Dispatch).Read("1000", McpValueType.Int16, 3,
			byteOrder: MemoryByteOrder.BigEndian, cancellationToken: Token);

		Assert.Equal(["1", "-1", "42"], result.Values!);
		Assert.Equal(["Memory.ReadBytes"], target.CallsTo("Memory"));
	}

	[Theory]
	[InlineData(McpValueType.Int8)]
	[InlineData(McpValueType.UInt8)]
	[InlineData(McpValueType.Pointer)]
	[InlineData(McpValueType.String)]
	[InlineData(McpValueType.WString)]
	[InlineData(McpValueType.Bytes)]
	public void Read_BigEndianOfAnotherType_RefusesBeforeAnyDispatch(McpValueType type)
	{
		TargetDouble target = new();

		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() =>
			new MemoryReadTools(target.Dispatch).Read("1000", type, size: type is McpValueType.Bytes ? 4 : null,
				byteOrder: MemoryByteOrder.BigEndian, cancellationToken: Token));

		AssertRefused(exception, "byteOrder");
		Assert.Equal(0, target.Dispatcher.Calls);
	}

	[Fact]
	public void Read_UndefinedByteOrder_RefusesBeforeAnyDispatch()
	{
		TargetDouble target = new();

		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() =>
			new MemoryReadTools(target.Dispatch).Read("1000", McpValueType.Int32, byteOrder: (MemoryByteOrder) 7,
				cancellationToken: Token));

		AssertRefused(exception, "byteOrder");
		Assert.Contains("little_endian or big_endian", exception.Error.Message, StringComparison.Ordinal);
		Assert.Equal(0, target.Dispatcher.Calls);
	}

	[Fact]
	public void Write_BigEndianInt32_WritesTheSwappedBytesAsATypedUInt32AndVerifiesThem()
	{
		TargetDouble target = new();
		ByteMemory memory = new();
		memory.Store(Start, "11 22 33 44");
		target.Memory = memory.Serve;

		MemoryWriteResult result = new MemoryWriteTools(target.Dispatch).Write("1000", McpValueType.Int32, "100",
			byteOrder: MemoryByteOrder.BigEndian, cancellationToken: Token);

		Assert.Equal(new MemoryWriteResult("1000", 4, "11 22 33 44", true), result);
		Assert.Equal("00 00 00 64", memory.Hex(Start, 4));
		Assert.Equal(["Memory.ReadBytes", "Memory.WritePrimitive<UInt32>", "Memory.ReadBytesDetailed"],
			target.CallsTo("Memory"));
	}

	[Theory]
	[MemberData(nameof(BigEndianValues))]
	public void WriteThenRead_BigEndian_RoundTripsThroughTheTargetBytes(McpValueType type, string value,
		string stored)
	{
		TargetDouble target = new();
		ByteMemory memory = new();
		target.Memory = memory.Serve;
		MemoryWriteTools writes = new(target.Dispatch);
		MemoryReadTools reads = new(target.Dispatch);

		MemoryWriteResult written = writes.Write("1000", type, value, byteOrder: MemoryByteOrder.BigEndian,
			cancellationToken: Token);
		MemoryReadResult big = reads.Read("1000", type, byteOrder: MemoryByteOrder.BigEndian,
			cancellationToken: Token);
		MemoryReadResult little = reads.Read("1000", type, cancellationToken: Token);
		MemoryReadResult bytes = reads.Read("1000", McpValueType.Bytes, size: written.BytesWritten,
			cancellationToken: Token);

		Assert.True(written.Verified);
		Assert.Equal(stored, memory.Hex(Start, written.BytesWritten));
		Assert.Equal(stored, bytes.Value);
		Assert.Equal(value, big.Value);
		Assert.Equal(MemoryTargets.Decode(type, memory.Bytes(Start, written.BytesWritten), 8), little.Value);
	}

	[Fact]
	public void WriteBatchThenReadBatch_MixedByteOrders_RoundTripAndKeepTheirTypedRuns()
	{
		TargetDouble target = new();
		ByteMemory memory = new();
		target.Memory = memory.Serve;

		MemoryWriteBatchResult written = new MemoryWriteTools(target.Dispatch).WriteBatch(
		[
			new MemoryWriteItem("1000", McpValueType.Int32, "100"),
			new MemoryWriteItem("1010", McpValueType.Int32, "100", MemoryByteOrder.BigEndian),
			new MemoryWriteItem("1020", McpValueType.Float, "1.5", MemoryByteOrder.BigEndian),
			new MemoryWriteItem("1030", McpValueType.UInt8, "7"),
			new MemoryWriteItem("1040", McpValueType.Double, "-0.25", MemoryByteOrder.BigEndian),
			new MemoryWriteItem("1050", McpValueType.Int16, "-2", MemoryByteOrder.BigEndian)
		], true, Token);
		string[] writeCalls = target.CallsTo("Memory");
		MemoryReadBatchResult read = new MemoryReadTools(target.Dispatch).ReadBatch(
		[
			new MemoryReadItem("1000", McpValueType.Int32),
			new MemoryReadItem("1010", McpValueType.Int32, ByteOrder: MemoryByteOrder.BigEndian),
			new MemoryReadItem("1020", McpValueType.Float, ByteOrder: MemoryByteOrder.BigEndian),
			new MemoryReadItem("1030", McpValueType.UInt8),
			new MemoryReadItem("1040", McpValueType.Double, ByteOrder: MemoryByteOrder.BigEndian),
			new MemoryReadItem("1050", McpValueType.Int16, ByteOrder: MemoryByteOrder.BigEndian),
			new MemoryReadItem("1010", McpValueType.Int32)
		], Token);

		Assert.Equal((6, true), (written.Written, written.Verified));
		Assert.Equal("64 00 00 00", memory.Hex(0x1000, 4));
		Assert.Equal("00 00 00 64", memory.Hex(0x1010, 4));
		Assert.Equal("3F C0 00 00", memory.Hex(0x1020, 4));
		Assert.Equal("BF D0 00 00 00 00 00 00", memory.Hex(0x1040, 8));
		Assert.Equal("FF FE", memory.Hex(0x1050, 2));
		Assert.Equal([
			"Memory.WritePrimitiveBatchDetailed<Int32>", "Memory.WritePrimitiveBatchDetailed<UInt32>",
			"Memory.WritePrimitiveBatchDetailed<UInt32>", "Memory.WritePrimitiveBatchDetailed<Byte>",
			"Memory.WritePrimitiveBatchDetailed<UInt64>", "Memory.WritePrimitiveBatchDetailed<UInt16>"
		], writeCalls[..6]);
		Assert.Equal(["100", "100", "1.5", "7", "-0.25", "-2", "1677721600"],
			read.Items.Select(static item => item.Value));
		Assert.Equal(0, read.Failed);
	}

	[Fact]
	public void WriteBatch_ConsecutiveBigEndianItemsOfOneType_ShareOneTypedRun()
	{
		TargetDouble target = new();
		ByteMemory memory = new();
		target.Memory = memory.Serve;

		new MemoryWriteTools(target.Dispatch).WriteBatch(
		[
			new MemoryWriteItem("1000", McpValueType.Int32, "1", MemoryByteOrder.BigEndian),
			new MemoryWriteItem("1004", McpValueType.Int32, "2", MemoryByteOrder.BigEndian),
			new MemoryWriteItem("1008", McpValueType.Int32, "3")
		], cancellationToken: Token);

		Assert.Equal(["Memory.WritePrimitiveBatchDetailed<UInt32>", "Memory.WritePrimitiveBatchDetailed<Int32>"],
			target.CallsTo("Memory"));
		Assert.Equal("00 00 00 01 00 00 00 02 03 00 00 00", memory.Hex(Start, 12));
	}

	[Theory]
	[InlineData(McpValueType.Pointer, "401000")]
	[InlineData(McpValueType.Int8, "1")]
	[InlineData(McpValueType.WString, "Hi")]
	[InlineData(McpValueType.Bytes, "90 90")]
	public void WriteAndWriteBatch_BigEndianOfAnotherType_RefuseBeforeAnyDispatch(McpValueType type, string value)
	{
		TargetDouble target = new();
		MemoryWriteTools tools = new(target.Dispatch);

		CheatEngineToolException single = Assert.Throws<CheatEngineToolException>(() => tools.Write("1000", type,
			value, byteOrder: MemoryByteOrder.BigEndian, cancellationToken: Token));
		CheatEngineToolException batch = Assert.Throws<CheatEngineToolException>(() => tools.WriteBatch(
		[
			new MemoryWriteItem("1000", McpValueType.Int32, "1"),
			new MemoryWriteItem("2000", type, value, MemoryByteOrder.BigEndian)
		], cancellationToken: Token));

		AssertRefused(single, "byteOrder");
		AssertRefused(batch, "items[1].byteOrder");
		Assert.Equal(0, target.Dispatcher.Calls);
	}

	[Fact]
	public void ReadBatch_BigEndianPointer_RefusesTheWholeBatchBeforeAnyDispatch()
	{
		TargetDouble target = new();

		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() =>
			new MemoryReadTools(target.Dispatch).ReadBatch(
			[
				new MemoryReadItem("1000", McpValueType.Int32, ByteOrder: MemoryByteOrder.BigEndian),
				new MemoryReadItem("2000", McpValueType.Pointer, ByteOrder: MemoryByteOrder.BigEndian)
			], Token));

		AssertRefused(exception, "items[1].byteOrder");
		Assert.Equal(0, target.Dispatcher.Calls);
	}

	[Fact]
	public void ReadSamples_BigEndian_ReadsTheUnsignedIntegerBatchAndSwapsEverySample()
	{
		TargetDouble target = new();
		ByteMemory memory = new();
		memory.Store(Start, "00 00 00 64");
		memory.Store(0x1004, "42 C8 00 00");
		target.Memory = memory.Serve;
		MemorySampleTools tools = new(target.Dispatch, new SteppingClock());

		MemorySampleResult integers = tools.ReadSamples(["1000"], FixedValueType.Int32, 1000, 100,
			MemoryByteOrder.BigEndian, Token);
		MemorySampleResult floats = tools.ReadSamples(["1004"], FixedValueType.Float, 1000, 100,
			MemoryByteOrder.BigEndian, Token);

		Assert.Equal("100", integers.Series[0].First);
		Assert.Equal("100", floats.Series[0].First);
		Assert.Equal(["Memory.ReadPrimitiveBatchDetailed<UInt32>", "Memory.ReadPrimitiveBatchDetailed<UInt32>"],
			target.CallsTo("Memory"));
	}

	[Theory]
	[InlineData(FixedValueType.Int8)]
	[InlineData(FixedValueType.UInt8)]
	[InlineData(FixedValueType.Pointer)]
	public void ReadSamples_BigEndianOfAnotherType_RefusesBeforeAnyDispatch(FixedValueType type)
	{
		TargetDouble target = new();

		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() =>
			new MemorySampleTools(target.Dispatch, new SteppingClock()).ReadSamples(["1000"], type, 100, 200,
				MemoryByteOrder.BigEndian, Token));

		AssertRefused(exception, "byteOrder");
		Assert.Equal(0, target.Dispatcher.Calls);
	}

	[Fact]
	public void MemoryByteOrder_WireNames_AreSnakeCase()
	{
		Assert.Equal("\"little_endian\"",
			JsonSerializer.Serialize(MemoryByteOrder.LittleEndian, MemoryJsonContext.Default.MemoryByteOrder));
		Assert.Equal(MemoryByteOrder.BigEndian,
			JsonSerializer.Deserialize("\"big_endian\"", MemoryJsonContext.Default.MemoryByteOrder));
		Assert.Throws<JsonException>(() =>
			JsonSerializer.Deserialize("\"network\"", MemoryJsonContext.Default.MemoryByteOrder));
	}

	[Fact]
	public void MemoryItems_WithoutByteOrder_DefaultToLittleEndian()
	{
		MemoryReadItem[] reads = JsonSerializer.Deserialize("""[{"address":"1000","valueType":"int32"}]""",
			MemoryJsonContext.Default.MemoryReadItemArray)!;
		MemoryWriteItem[] writes = JsonSerializer.Deserialize(
			"""
			[
				{"address":"1000","valueType":"int32","value":"1"},
				{"address":"1004","valueType":"float","value":"1","byteOrder":"big_endian"}
			]
			""",
			MemoryJsonContext.Default.MemoryWriteItemArray)!;

		Assert.Equal(MemoryByteOrder.LittleEndian, reads[0].ByteOrder);
		Assert.Equal([MemoryByteOrder.LittleEndian, MemoryByteOrder.BigEndian],
			writes.Select(static item => item.ByteOrder));
	}

	private static void AssertRefused(CheatEngineToolException exception, string parameter)
	{
		Assert.Equal((ToolErrorKind.InvalidArgument, ToolHostEffect.NotStarted),
			(exception.Error.Kind, exception.Error.HostEffect));
		Assert.StartsWith(parameter, exception.Error.Message, StringComparison.Ordinal);
	}

	/// <summary>The unsigned integer of the width of spaced hexadecimal bytes.</summary>
	private static Type Unsigned(string stored)
	{
		int width = (stored.Length + 1) / 3;
		return width switch
		{
			2 => typeof(ushort),
			4 => typeof(uint),
			_ => typeof(ulong)
		};
	}

	/// <summary>
	///     A small flat target memory at <see cref="Start" />: every typed read and write, batched or not, sees the
	///     same little-endian bytes the target would hold.
	/// </summary>
	private sealed class ByteMemory
	{
		private readonly byte[] _bytes = new byte[0x100];

		internal void Store(ulong address, string hex)
		{
			Convert.FromHexString(hex.Replace(" ", "", StringComparison.Ordinal)).CopyTo(_bytes, Offset(address));
		}

		internal string Hex(ulong address, int length)
		{
			return HexFormat.Bytes(Bytes(address, length));
		}

		internal ReadOnlySpan<byte> Bytes(ulong address, int length)
		{
			return _bytes.AsSpan(Offset(address), length);
		}

		internal object? Serve(MethodInfo method, object?[] arguments)
		{
			Type? type = method.IsGenericMethod ? method.GetGenericArguments()[0] : null;
			switch (method.Name)
			{
				case nameof(IMemoryClient.ReadPrimitive):
					return Decode(type!, _bytes.AsSpan(Offset(((Address) arguments[0]!).ToUInt64())));
				case nameof(IMemoryClient.WritePrimitive):
					Encode(arguments[1]!).CopyTo(_bytes, Offset(((Address) arguments[0]!).ToUInt64()));
					return null;
				case nameof(IMemoryClient.ReadBytes):
					MemoryBytesReadRequest read = (MemoryBytesReadRequest) arguments[0]!;
					return _bytes.AsSpan(Offset(read.Address.ToUInt64()), read.Length).ToImmutableArray();
				case nameof(IMemoryClient.ReadBytesDetailed):
					MemoryBytesReadRequest detailed = (MemoryBytesReadRequest) arguments[0]!;
					return new MemoryBytesReadOutcome(detailed.Length,
						_bytes.AsSpan(Offset(detailed.Address.ToUInt64()), detailed.Length).ToImmutableArray(), null);
				case nameof(IMemoryClient.ReadPrimitiveBatchDetailed):
					return ReadBatch(type!, arguments[0]!);
				case nameof(IMemoryClient.WritePrimitiveBatchDetailed):
					return WriteBatch(type!, arguments[0]!);
				default:
					throw new XunitException($"Unexpected {method.Name}.");
			}
		}

		private static int Offset(ulong address)
		{
			return checked((int) (address - Start));
		}

		private static object Decode(Type type, ReadOnlySpan<byte> bytes)
		{
			return type switch
			{
				_ when type == typeof(byte) => (object) bytes[0],
				_ when type == typeof(ushort) => (object) BinaryPrimitives.ReadUInt16LittleEndian(bytes),
				_ when type == typeof(uint) => (object) BinaryPrimitives.ReadUInt32LittleEndian(bytes),
				_ when type == typeof(ulong) => (object) BinaryPrimitives.ReadUInt64LittleEndian(bytes),
				_ when type == typeof(short) => (object) BinaryPrimitives.ReadInt16LittleEndian(bytes),
				_ when type == typeof(int) => (object) BinaryPrimitives.ReadInt32LittleEndian(bytes),
				_ when type == typeof(long) => (object) BinaryPrimitives.ReadInt64LittleEndian(bytes),
				_ when type == typeof(float) => (object) BinaryPrimitives.ReadSingleLittleEndian(bytes),
				_ when type == typeof(double) => (object) BinaryPrimitives.ReadDoubleLittleEndian(bytes),
				_ => throw new XunitException($"Unexpected primitive {type.Name}.")
			};
		}

		private static byte[] Encode(object value)
		{
			byte[] bytes = new byte[8];
			switch (value)
			{
				case byte v:
					return [v];
				case ushort v:
					BinaryPrimitives.WriteUInt16LittleEndian(bytes, v);
					return bytes[..2];
				case uint v:
					BinaryPrimitives.WriteUInt32LittleEndian(bytes, v);
					return bytes[..4];
				case ulong v:
					BinaryPrimitives.WriteUInt64LittleEndian(bytes, v);
					return bytes;
				case int v:
					BinaryPrimitives.WriteInt32LittleEndian(bytes, v);
					return bytes[..4];
				default:
					throw new XunitException($"Unexpected primitive {value.GetType().Name}.");
			}
		}

		private object ReadBatch(Type type, object request)
		{
			return type switch
			{
				_ when type == typeof(byte) => ReadBatch((MemoryPrimitiveBatchReadRequest<byte>) request),
				_ when type == typeof(ushort) => ReadBatch((MemoryPrimitiveBatchReadRequest<ushort>) request),
				_ when type == typeof(uint) => ReadBatch((MemoryPrimitiveBatchReadRequest<uint>) request),
				_ when type == typeof(ulong) => ReadBatch((MemoryPrimitiveBatchReadRequest<ulong>) request),
				_ when type == typeof(int) => ReadBatch((MemoryPrimitiveBatchReadRequest<int>) request),
				_ => throw new XunitException($"Unexpected batch read of {type.Name}.")
			};
		}

		private MemoryPrimitiveBatchReadOutcome<T> ReadBatch<T>(MemoryPrimitiveBatchReadRequest<T> request)
			where T : unmanaged
		{
			T[] values = [.. request.Addresses.Select(address =>
				(T) Decode(typeof(T), _bytes.AsSpan(Offset(address.ToUInt64()))))];
			return new MemoryPrimitiveBatchReadOutcome<T>(values.Length, values, null, null);
		}

		private MemoryPrimitiveBatchWriteOutcome WriteBatch(Type type, object request)
		{
			return type switch
			{
				_ when type == typeof(byte) => WriteBatch((MemoryPrimitiveBatchWriteRequest<byte>) request),
				_ when type == typeof(ushort) => WriteBatch((MemoryPrimitiveBatchWriteRequest<ushort>) request),
				_ when type == typeof(uint) => WriteBatch((MemoryPrimitiveBatchWriteRequest<uint>) request),
				_ when type == typeof(ulong) => WriteBatch((MemoryPrimitiveBatchWriteRequest<ulong>) request),
				_ when type == typeof(int) => WriteBatch((MemoryPrimitiveBatchWriteRequest<int>) request),
				_ => throw new XunitException($"Unexpected batch write of {type.Name}.")
			};
		}

		private MemoryPrimitiveBatchWriteOutcome WriteBatch<T>(MemoryPrimitiveBatchWriteRequest<T> request)
			where T : unmanaged
		{
			foreach (MemoryAddressValue<T> value in request.Values)
			{
				Encode(value.Value).CopyTo(_bytes, Offset(value.Address.ToUInt64()));
			}

			return new MemoryPrimitiveBatchWriteOutcome(request.Values.Length, request.Values.Length, null, null,
				MemoryBatchWriteEffectState.Completed);
		}
	}
}
