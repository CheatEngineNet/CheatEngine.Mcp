using System.Buffers.Binary;
using System.ComponentModel;
using System.Globalization;
using System.Text.Json;

using CheatEngine.Client;
using CheatEngine.Client.Memory;
using CheatEngine.Client.Results;
using CheatEngine.Mcp.Core.Contract;
using CheatEngine.Mcp.Core.Values;
using CheatEngine.SDK.Engine.Values;

using ModelContextProtocol.Server;

namespace CheatEngine.Mcp.Tools.Memory;

/// <summary>The <c>memory_*</c> tools that change target memory: writes, batched writes, copies and protection changes.</summary>
[McpServerToolType]
public sealed class MemoryWriteTools
{
	/// <summary>The most times <c>memory_write</c> repeats a byte pattern.</summary>
	internal const int MaximumRepeat = 65536;

	/// <summary>The largest total of one write call, the Client's write bound.</summary>
	internal const int MaximumWriteBytes = MemoryResourceLimits.DefaultMaximumWriteBytes;

	/// <summary>The most bytes <c>memory_write</c> reports as <c>previous</c>.</summary>
	internal const int PreviousBytes = 64;

	/// <summary>The most items of <c>memory_write_batch</c>.</summary>
	internal const int MaximumBatchItems = 1024;

	/// <summary>The largest range of <c>memory_copy</c>.</summary>
	internal const int MaximumCopyBytes = 1024 * 1024;

	/// <summary>The largest range of <c>memory_set_protection</c>.</summary>
	internal const long MaximumProtectionBytes = 16 * 1024 * 1024;

	private readonly ToolDispatch _dispatch;

	/// <summary>Creates the tools; the constructor does no Cheat Engine work.</summary>
	/// <param name="dispatch">The activation's dispatch facade.</param>
	public MemoryWriteTools(ToolDispatch dispatch)
	{
		ArgumentNullException.ThrowIfNull(dispatch);
		_dispatch = dispatch;
	}

	/// <summary>Writes one typed value, a string or bytes, keeping the previous bytes.</summary>
	[McpServerTool(Name = CheatEngineToolNames.MemoryWrite, Title = "Write memory", ReadOnly = false,
		Destructive = true, Idempotent = false, OpenWorld = false, UseStructuredContent = true)]
	[McpMeta(McpDispatchClass.MetaKey, McpDispatchClass.Short)]
	[Description(
		"Write one typed value, a string or bytes to target memory. The first 64 bytes of the range are read first and returned as previous, so the write can be undone with valueType bytes; with verify the written bytes are read back. A wrong write can crash the target: write only addresses you identified, one at a time.")]
	public MemoryWriteResult Write(
		[Description("An address or Cheat Engine address expression, such as game.exe+1C or 7FF6A1B2C3D0.")]
		string address,
		[Description("The value type to write.")]
		McpValueType valueType,
		[Description(
			"The value as text: decimal or 0x hexadecimal integers, invariant floats (NaN, Infinity), a hexadecimal pointer, the text of a string or spaced hexadecimal bytes such as 90 90.")]
		string value,
		[Description("How many times to write the bytes back to back, 1 to 65536 (1 MiB in total); only for bytes.")]
		int repeat = 1,
		[Description("Whether to append a terminating zero character; only for string and wstring.")]
		bool nullTerminate = false,
		[Description("Whether to read the range back and compare it with the written bytes.")]
		bool verify = true,
		[Description(MemoryByteOrders.Description)]
		MemoryByteOrder byteOrder = MemoryByteOrder.LittleEndian,
		CancellationToken cancellationToken = default)
	{
		string expression = MemoryTargets.RequireExpression(address, "address");
		MemoryTargets.RequireType(valueType, "valueType");
		bool swapped = MemoryByteOrders.Require(valueType, byteOrder, "byteOrder");
		RequireValue(value, "value");
		MemoryTargets.RequireRange(repeat, "repeat", 1, MaximumRepeat);
		if (repeat != 1 && valueType is not McpValueType.Bytes)
		{
			throw CheatEngineToolException.InvalidArgument("repeat", "applies only to bytes.");
		}

		bool text = valueType is McpValueType.String or McpValueType.WString;
		if (nullTerminate && !text)
		{
			throw CheatEngineToolException.InvalidArgument("nullTerminate", "applies only to string and wstring.");
		}

		// A pointer is checked here as 64-bit and sized once the target's pointer size is known.
		byte[] encoded = McpValueCodec.Encode(valueType, value, 8, "value");
		if (swapped)
		{
			MemoryByteOrders.Swap(encoded);
		}

		if (valueType is McpValueType.Bytes && (long) encoded.Length * repeat > MaximumWriteBytes)
		{
			throw CheatEngineToolException.LimitExceeded("repeat",
				$"writes {((long) encoded.Length * repeat).ToString(CultureInfo.InvariantCulture)} bytes; the limit is {MaximumWriteBytes}.");
		}

		if (text && nullTerminate)
		{
			encoded = [.. encoded, .. new byte[valueType is McpValueType.WString ? 2 : 1]];
		}
		else if (text && encoded.Length == 0)
		{
			throw CheatEngineToolException.InvalidArgument("value",
				"must not be empty; set nullTerminate to write an empty string.");
		}

		byte[] bytes = valueType is McpValueType.Bytes ? Repeat(encoded, repeat) : encoded;
		bool typedWrite = !(valueType is McpValueType.Bytes || nullTerminate);
		return _dispatch.Run(CheatEngineToolNames.MemoryWrite, token =>
		{
			ICheatEngineClient client = _dispatch.Client;
			Address target = MemoryTargets.Resolve(client, expression, "address", token);
			byte[] written = valueType is McpValueType.Pointer
				? McpValueCodec.Encode(valueType, value, MemoryTargets.PointerBytes(client, token), "value")
				: bytes;
			string previous = HexFormat.Bytes(MemoryReadTools.ReadArray(client, target,
				Math.Min(written.Length, PreviousBytes), token));
			if (typedWrite && swapped)
			{
				MemoryByteOrders.Write(client, target, written, token);
			}
			else if (typedWrite)
			{
				McpValueCodec.Write(client, target, valueType, value, token);
			}
			else
			{
				client.Memory.WriteBytes(new MemoryBytesWriteRequest(target, written), token);
			}

			// The write happened: only the activation's stopping token may interrupt what follows.
			bool verified = verify && Matches(client, target, written, client.Stopping);
			return new MemoryWriteResult(HexFormat.Address(target), written.Length, previous, verified);
		}, cancellationToken);
	}

	/// <summary>Writes up to 1024 typed values in order; a failure reports the completed prefix.</summary>
	[McpServerTool(Name = CheatEngineToolNames.MemoryWriteBatch, Title = "Write many addresses", ReadOnly = false,
		Destructive = true, Idempotent = false, OpenWorld = false, UseStructuredContent = true)]
	[McpMeta(McpDispatchClass.MetaKey, McpDispatchClass.Short)]
	[Description(
		"Write up to 1024 addresses in request order, each with its own value type (1 MiB in total). Every address is resolved before the first write. The writes are not atomic: a failure stops the batch and reports partial_effect with completed, failedIndex and effectState, and the completed items stay written.")]
	public MemoryWriteBatchResult WriteBatch(
		[Description("The writes, 1 to 1024, each with its address, value type and value.")]
		MemoryWriteItem[] items,
		[Description("Whether to read every item back after the batch and list the items that differ.")]
		bool verify = false,
		CancellationToken cancellationToken = default)
	{
		PreparedWrite[] writes = CheckBatch(items);
		return _dispatch.Run(CheatEngineToolNames.MemoryWriteBatch,
			token => WriteBatch(_dispatch.Client, writes, verify, token), cancellationToken);
	}

	/// <summary>Copies a range inside the target.</summary>
	[McpServerTool(Name = CheatEngineToolNames.MemoryCopy, Title = "Copy memory", ReadOnly = false,
		Destructive = true, Idempotent = false, OpenWorld = false, UseStructuredContent = true)]
	[McpMeta(McpDispatchClass.MetaKey, McpDispatchClass.Short)]
	[Description(
		"Copy up to 1 MiB from one target address to another: the whole source is read first, so overlapping ranges copy correctly. The destination is overwritten; allocate it with memory_allocate when it must be new memory.")]
	public MemoryCopyResult Copy(
		[Description("The source address or Cheat Engine address expression.")]
		string source,
		[Description("The destination address or Cheat Engine address expression.")]
		string destination,
		[Description("The number of bytes to copy, 1 to 1048576.")]
		int size,
		CancellationToken cancellationToken = default)
	{
		string from = MemoryTargets.RequireExpression(source, "source");
		string to = MemoryTargets.RequireExpression(destination, "destination");
		MemoryTargets.RequireRange(size, "size", 1, MaximumCopyBytes);
		return _dispatch.Run(CheatEngineToolNames.MemoryCopy, token =>
		{
			ICheatEngineClient client = _dispatch.Client;
			Address sourceAddress = MemoryTargets.Resolve(client, from, "source", token);
			Address destinationAddress = MemoryTargets.Resolve(client, to, "destination", token);
			byte[] bytes = MemoryReadTools.ReadArray(client, sourceAddress, size, token);
			client.Memory.WriteBytes(new MemoryBytesWriteRequest(destinationAddress, bytes), token);
			return new MemoryCopyResult(HexFormat.Address(sourceAddress), HexFormat.Address(destinationAddress), size);
		}, cancellationToken);
	}

	/// <summary>Changes the read, write and execute protection of a range.</summary>
	[McpServerTool(Name = CheatEngineToolNames.MemorySetProtection, Title = "Set memory protection",
		ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = false, UseStructuredContent = true)]
	[McpMeta(McpDispatchClass.MetaKey, McpDispatchClass.Short)]
	[Description(
		"Change the page protection of up to 16 MiB of target memory (Cheat Engine's setMemoryProtection, or fullAccess when read, write and execute are all set). The entire range must fit in one committed memory region, so its returned previous access can restore the range. A change Cheat Engine refuses, such as write and execute together where the system forbids them, fails with host_refused. Change protection only with consent and restore previous when done.")]
	public ProtectionChange SetProtection(
		[Description("An address or Cheat Engine address expression.")]
		string address,
		[Description(
			"The number of bytes whose pages change, 1 to 16777216; all must fit in one committed memory region.")]
		long size,
		[Description(
			"Whether the pages can be read. Write or execute always includes read; with all three false the pages become inaccessible and any access by the target faults.")]
		bool read = true,
		[Description("Whether the pages can be written.")]
		bool write = false,
		[Description("Whether the pages can be executed.")]
		bool execute = false,
		CancellationToken cancellationToken = default)
	{
		string expression = MemoryTargets.RequireExpression(address, "address");
		MemoryTargets.RequireRange(size, "size", 1, MaximumProtectionBytes);
		return _dispatch.Run(CheatEngineToolNames.MemorySetProtection, token =>
		{
			ICheatEngineClient client = _dispatch.Client;
			Address target = MemoryTargets.Resolve(client, expression, "address", token);
			MemoryTargets.RequireCommittedProtectionRange(client, target, size, token);
			ProtectionProbe probe = _dispatch.ExecuteLua(CheatEngineToolNames.MemorySetProtection,
				MemoryScripts.Protection, MemoryJsonContext.Default.ProtectionProbe, token, target.ToUInt64(), size,
				read, write, execute);
			return new ProtectionChange(HexFormat.Address(target), size, probe.Previous, probe.Current);
		}, cancellationToken);
	}

	private static void RequireValue(string? value, string parameter)
	{
		if (value is null)
		{
			throw CheatEngineToolException.InvalidArgument(parameter, "is required.");
		}
	}

	private static byte[] Repeat(byte[] pattern, int count)
	{
		byte[] bytes = new byte[pattern.Length * count];
		for (int index = 0; index < count; index++)
		{
			pattern.CopyTo(bytes, index * pattern.Length);
		}

		return bytes;
	}

	private static bool Matches(ICheatEngineClient client, Address address, byte[] expected,
		CancellationToken cancellationToken)
	{
		MemoryBytesReadOutcome outcome =
			client.Memory.ReadBytesDetailed(new MemoryBytesReadRequest(address, expected.Length), cancellationToken);
		return outcome.IsSuccess && outcome.Bytes.AsSpan().SequenceEqual(expected);
	}

	private static PreparedWrite[] CheckBatch(MemoryWriteItem[]? items)
	{
		if (items is null || items.Length == 0)
		{
			throw CheatEngineToolException.InvalidArgument("items", "must list at least one write.");
		}

		if (items.Length > MaximumBatchItems)
		{
			throw CheatEngineToolException.LimitExceeded("items", $"accepts at most {MaximumBatchItems} writes.");
		}

		PreparedWrite[] writes = new PreparedWrite[items.Length];
		long total = 0;
		for (int index = 0; index < items.Length; index++)
		{
			string prefix = $"items[{index.ToString(CultureInfo.InvariantCulture)}]";
			MemoryWriteItem item = items[index] ??
								   throw CheatEngineToolException.InvalidArgument(prefix, "must be an object.");
			string expression = MemoryTargets.RequireExpression(item.Address, prefix + ".address");
			MemoryTargets.RequireType(item.ValueType, prefix + ".valueType");
			RequireValue(item.Value, prefix + ".value");
			bool swapped = MemoryByteOrders.Require(item.ValueType, item.ByteOrder, prefix + ".byteOrder");
			byte[] encoded = McpValueCodec.Encode(item.ValueType, item.Value, 8, prefix + ".value");
			if (encoded.Length == 0)
			{
				throw CheatEngineToolException.InvalidArgument(prefix + ".value", "must not be empty.");
			}

			total += encoded.Length;
			writes[index] = new PreparedWrite(expression, item.ValueType, item.Value,
				swapped ? MemoryByteOrders.Swap(encoded) : encoded, item.ByteOrder);
		}

		return total <= MaximumWriteBytes
			? writes
			: throw CheatEngineToolException.LimitExceeded("items",
				$"write {total.ToString(CultureInfo.InvariantCulture)} bytes in total; the limit is {MaximumWriteBytes}.");
	}

	private static MemoryWriteBatchResult WriteBatch(ICheatEngineClient client, PreparedWrite[] writes, bool verify,
		CancellationToken cancellationToken)
	{
		// Every address resolves, and every pointer fits the target, before the first write.
		Address[] addresses = new Address[writes.Length];
		for (int index = 0; index < writes.Length; index++)
		{
			string parameter = $"items[{index.ToString(CultureInfo.InvariantCulture)}].address";
			addresses[index] = MemoryTargets.Resolve(client, writes[index].Expression, parameter, cancellationToken);
		}

		if (Array.Exists(writes, static write => write.Type is McpValueType.Pointer))
		{
			int pointerBytes = MemoryTargets.PointerBytes(client, cancellationToken);
			for (int index = 0; index < writes.Length; index++)
			{
				if (writes[index].Type is McpValueType.Pointer)
				{
					string parameter = $"items[{index.ToString(CultureInfo.InvariantCulture)}].value";
					writes[index] = writes[index] with
					{
						Bytes = McpValueCodec.Encode(McpValueType.Pointer, writes[index].Value, pointerBytes,
							parameter)
					};
				}
			}
		}

		CancellationToken token = cancellationToken;
		int completed = 0;
		int position = 0;
		while (position < writes.Length)
		{
			McpValueType type = writes[position].Type;
			MemoryByteOrder order = writes[position].Order;
			if (McpValueCodec.FixedSize(type, 8) is not null)
			{
				int end = position;
				while (end < writes.Length && writes[end].Type == type && writes[end].Order == order &&
					   end - position < MemoryBatchLimits.MaximumOperationCount)
				{
					end++;
				}

				MemoryPrimitiveBatchWriteOutcome outcome = WriteRun(client, type, order,
					writes.AsSpan(position, end - position), addresses.AsSpan(position), token);
				token = client.Stopping;
				if (!outcome.IsSuccess)
				{
					BatchWriteEffect effect = completed + outcome.CompletedCount > 0
						? BatchWriteEffect.Partial
						: outcome.EffectState is MemoryBatchWriteEffectState.NotStarted
							? BatchWriteEffect.NotStarted
							: BatchWriteEffect.Unknown;
					throw BatchFailure(client, writes.Length, completed + outcome.CompletedCount,
						outcome.FailedIndex is { } failed ? position + failed : null, effect, outcome.Failure!.Value);
				}

				completed += outcome.CompletedCount;
				position = end;
				continue;
			}

			PreparedWrite write = writes[position];
			bool succeeded = type is McpValueType.Bytes
				? client.Memory.TryWriteBytes(new MemoryBytesWriteRequest(addresses[position], write.Bytes),
					out CheatEngineFailure failure, token)
				: client.Memory.TryWriteString(new MemoryStringWriteRequest(addresses[position], write.Value,
						write.Bytes.Length / (type is McpValueType.WString ? 2 : 1),
						type is McpValueType.WString ? MemoryStringEncoding.Utf16 : MemoryStringEncoding.Utf8),
					out failure, token);
			token = client.Stopping;
			if (!succeeded)
			{
				BatchWriteEffect effect = completed > 0
					? BatchWriteEffect.Partial
					: failure.HostEffect is CheatEngineHostEffect.NotStarted or CheatEngineHostEffect.NotApplied
						? BatchWriteEffect.NotStarted
						: BatchWriteEffect.Unknown;
				throw BatchFailure(client, writes.Length, completed, position, effect, failure);
			}

			completed++;
			position++;
		}

		if (!verify)
		{
			return new MemoryWriteBatchResult(completed, false);
		}

		List<int> mismatched = [];
		for (int index = 0; index < writes.Length; index++)
		{
			if (!Matches(client, addresses[index], writes[index].Bytes, client.Stopping))
			{
				mismatched.Add(index);
			}
		}

		return new MemoryWriteBatchResult(completed, mismatched.Count == 0,
			mismatched.Count == 0 ? null : [.. mismatched]);
	}

	/// <summary>
	///     Writes one run of a fixed-size type as one typed batch. A big-endian run already holds its swapped bytes, so
	///     it is written as the unsigned integer of its width, whose little-endian bytes are those bytes.
	/// </summary>
	private static MemoryPrimitiveBatchWriteOutcome WriteRun(ICheatEngineClient client, McpValueType type,
		MemoryByteOrder order, ReadOnlySpan<PreparedWrite> run, ReadOnlySpan<Address> addresses,
		CancellationToken cancellationToken)
	{
		if (order is MemoryByteOrder.BigEndian)
		{
			return McpValueCodec.FixedSize(type, 8) switch
			{
				2 => WritePrimitives(client, run, addresses,
					static bytes => BinaryPrimitives.ReadUInt16LittleEndian(bytes), cancellationToken),
				4 => WritePrimitives(client, run, addresses,
					static bytes => BinaryPrimitives.ReadUInt32LittleEndian(bytes), cancellationToken),
				_ => WritePrimitives(client, run, addresses,
					static bytes => BinaryPrimitives.ReadUInt64LittleEndian(bytes), cancellationToken)
			};
		}

		return type switch
		{
			McpValueType.Int8 => WritePrimitives(client, run, addresses,
				static bytes => unchecked((sbyte) bytes[0]), cancellationToken),
			McpValueType.UInt8 => WritePrimitives(client, run, addresses, static bytes => bytes[0],
				cancellationToken),
			McpValueType.Int16 => WritePrimitives(client, run, addresses,
				static bytes => BinaryPrimitives.ReadInt16LittleEndian(bytes), cancellationToken),
			McpValueType.UInt16 => WritePrimitives(client, run, addresses,
				static bytes => BinaryPrimitives.ReadUInt16LittleEndian(bytes), cancellationToken),
			McpValueType.Int32 => WritePrimitives(client, run, addresses,
				static bytes => BinaryPrimitives.ReadInt32LittleEndian(bytes), cancellationToken),
			McpValueType.UInt32 => WritePrimitives(client, run, addresses,
				static bytes => BinaryPrimitives.ReadUInt32LittleEndian(bytes), cancellationToken),
			McpValueType.Int64 => WritePrimitives(client, run, addresses,
				static bytes => BinaryPrimitives.ReadInt64LittleEndian(bytes), cancellationToken),
			McpValueType.UInt64 => WritePrimitives(client, run, addresses,
				static bytes => BinaryPrimitives.ReadUInt64LittleEndian(bytes), cancellationToken),
			McpValueType.Float => WritePrimitives(client, run, addresses,
				static bytes => BinaryPrimitives.ReadSingleLittleEndian(bytes), cancellationToken),
			McpValueType.Double => WritePrimitives(client, run, addresses,
				static bytes => BinaryPrimitives.ReadDoubleLittleEndian(bytes), cancellationToken),
			_ => WritePrimitives(client, run, addresses, static bytes => new Address(bytes.Length == 4
				? BinaryPrimitives.ReadUInt32LittleEndian(bytes)
				: BinaryPrimitives.ReadUInt64LittleEndian(bytes)), cancellationToken)
		};
	}

	private static MemoryPrimitiveBatchWriteOutcome WritePrimitives<T>(ICheatEngineClient client,
		ReadOnlySpan<PreparedWrite> run, ReadOnlySpan<Address> addresses, Func<byte[], T> decode,
		CancellationToken cancellationToken) where T : unmanaged
	{
		MemoryAddressValue<T>[] values = new MemoryAddressValue<T>[run.Length];
		for (int index = 0; index < run.Length; index++)
		{
			values[index] = new MemoryAddressValue<T>(addresses[index], decode(run[index].Bytes));
		}

		return client.Memory.WritePrimitiveBatchDetailed(new MemoryPrimitiveBatchWriteRequest<T>(values),
			cancellationToken);
	}

	private static CheatEngineToolException BatchFailure(ICheatEngineClient client, int requested, int completed,
		int? failedIndex, BatchWriteEffect effect, CheatEngineFailure failure)
	{
		MemoryWriteBatchFailure details = new(completed, failedIndex, effect);
		string failedItem = failedIndex is { } index
			? $"item {index.ToString(CultureInfo.InvariantCulture)}"
			: "an item";
		string message =
			$"{completed.ToString(CultureInfo.InvariantCulture)} of {requested.ToString(CultureInfo.InvariantCulture)} items were written before {failedItem} failed: {failure.Message}";
		if (effect is BatchWriteEffect.NotStarted)
		{
			ToolError error = ToolFailureMapping.Map(failure, client.Stopping.IsCancellationRequested) with
			{
				Message = message,
				Details = JsonSerializer.SerializeToElement(details,
					MemoryJsonContext.Default.MemoryWriteBatchFailure)
			};
			return new CheatEngineToolException(error, failure.Exception);
		}

		return CheatEngineToolException.PartialEffect(message,
			effect is BatchWriteEffect.Partial ? ToolHostEffect.Started : ToolHostEffect.Unknown, details,
			MemoryJsonContext.Default.MemoryWriteBatchFailure, false,
			"Read the items with memory_read_batch before repeating any write; the completed items stay written.");
	}

	/// <summary>One checked write; <c>Bytes</c> are the bytes the target must hold, in its byte order.</summary>
	private sealed record PreparedWrite(
		string Expression,
		McpValueType Type,
		string Value,
		byte[] Bytes,
		MemoryByteOrder Order);
}
