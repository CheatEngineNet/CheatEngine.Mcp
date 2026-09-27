using System.Collections.Immutable;
using System.ComponentModel;
using System.Globalization;
using System.Security.Cryptography;

using CheatEngine.Client;
using CheatEngine.Client.Memory;
using CheatEngine.Client.Results;
using CheatEngine.Mcp.Core.Contract;
using CheatEngine.Mcp.Core.Values;
using CheatEngine.SDK.Engine.Values;

using ModelContextProtocol.Server;

namespace CheatEngine.Mcp.Tools.Memory;

/// <summary>The read-only <c>memory_*</c> tools: typed reads, batched reads, range comparison and hashing.</summary>
[McpServerToolType]
public sealed class MemoryReadTools
{
	/// <summary>The most values one <c>memory_read</c> returns.</summary>
	internal const int MaximumCount = 1024;

	/// <summary>The largest bytes read of <c>memory_read</c> and of one batch item.</summary>
	internal const int MaximumBytes = 16384;

	/// <summary>The largest string length of <c>memory_read</c> and of one batch item.</summary>
	internal const int MaximumStringLength = 4096;

	/// <summary>The most items of <c>memory_read_batch</c>.</summary>
	internal const int MaximumBatchItems = 1024;

	/// <summary>The largest total payload of <c>memory_read_batch</c>.</summary>
	internal const int MaximumBatchBytes = 256 * 1024;

	/// <summary>The largest range of <c>memory_compare</c>.</summary>
	internal const int MaximumCompareBytes = 16 * 1024 * 1024;

	/// <summary>The most differences <c>memory_compare</c> lists.</summary>
	internal const int MaximumDifferences = 256;

	/// <summary>The longest run one listed difference covers.</summary>
	internal const int MaximumDifferenceBytes = 64;

	/// <summary>The largest range of <c>memory_hash</c>.</summary>
	internal const int MaximumHashBytes = 64 * 1024 * 1024;

	private readonly ToolDispatch _dispatch;

	/// <summary>Creates the tools; the constructor does no Cheat Engine work.</summary>
	/// <param name="dispatch">The activation's dispatch facade.</param>
	public MemoryReadTools(ToolDispatch dispatch)
	{
		ArgumentNullException.ThrowIfNull(dispatch);
		_dispatch = dispatch;
	}

	/// <summary>Reads one typed value, consecutive values, a string or bytes.</summary>
	[McpServerTool(Name = CheatEngineToolNames.MemoryRead, Title = "Read memory", ReadOnly = true,
		Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
	[McpMeta(McpDispatchClass.MetaKey, McpDispatchClass.Short)]
	[Description(
		"Read target memory at one address: one typed value, up to 1024 consecutive values of a fixed-size type (count), a string (length) or raw bytes (size). Values are text: decimal integers, round-trippable floats, hexadecimal pointers, spaced hexadecimal bytes. An unreadable address fails with memory_read_failed.")]
	public MemoryReadResult Read(
		[Description(
			"An address or Cheat Engine address expression, such as game.exe+1C, [game.exe+10]+8 or 7FF6A1B2C3D0.")]
		string address,
		[Description("The value type to read.")]
		McpValueType valueType,
		[Description(
			"How many consecutive values of a fixed-size type to read, 1 to 1024; only 1 for string, wstring and bytes.")]
		int count = 1,
		[Description("The byte count for valueType bytes, 1 to 16384; refused for other types.")]
		int? size = null,
		[Description(
			"The maximum string length for string and wstring, 1 to 4096 (default 256); refused for other types.")]
		int? length = null,
		CancellationToken cancellationToken = default)
	{
		string expression = MemoryTargets.RequireExpression(address, "address");
		MemoryTargets.RequireType(valueType, "valueType");
		int? readLength = CheckShape(valueType, size, length, "size", "length");
		MemoryTargets.RequireRange(count, "count", 1, MaximumCount);
		int? fixedSize = McpValueCodec.FixedSize(valueType, 8);
		if (fixedSize is null && count != 1)
		{
			throw CheatEngineToolException.InvalidArgument("count", "must be 1 for string, wstring and bytes.");
		}

		return _dispatch.Run(CheatEngineToolNames.MemoryRead, token =>
		{
			ICheatEngineClient client = _dispatch.Client;
			Address target = MemoryTargets.Resolve(client, expression, "address", token);
			string resolved = HexFormat.Address(target);
			if (count == 1)
			{
				return new MemoryReadResult(resolved, valueType,
					McpValueCodec.Read(client, target, valueType, readLength, token));
			}

			int elementBytes = valueType is McpValueType.Pointer
				? MemoryTargets.PointerBytes(client, token)
				: fixedSize!.Value;
			ImmutableArray<byte> bytes =
				client.Memory.ReadBytes(new MemoryBytesReadRequest(target, elementBytes * count), token);
			string[] values = new string[count];
			for (int index = 0; index < count; index++)
			{
				values[index] = MemoryTargets.Decode(valueType, bytes.AsSpan(index * elementBytes, elementBytes),
					elementBytes);
			}

			return new MemoryReadResult(resolved, valueType, Values: values);
		}, cancellationToken);
	}

	/// <summary>Reads up to 1024 typed addresses; each item reports its own error.</summary>
	[McpServerTool(Name = CheatEngineToolNames.MemoryReadBatch, Title = "Read many addresses", ReadOnly = true,
		Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
	[McpMeta(McpDispatchClass.MetaKey, McpDispatchClass.Short)]
	[Description(
		"Read up to 1024 addresses, each with its own value type, in one call (256 KiB in total). Fixed-size types are read in typed batches; an address that does not resolve or cannot be read reports its own error in band while the others are still read, so check every item.")]
	public MemoryReadBatchResult ReadBatch(
		[Description(
			"The addresses to read, 1 to 1024, each with its value type and, for bytes or strings, its size or length.")]
		MemoryReadItem[] items,
		CancellationToken cancellationToken = default)
	{
		BatchRead[] reads = CheckBatch(items);
		return _dispatch.Run(CheatEngineToolNames.MemoryReadBatch,
			token => ReadBatch(_dispatch.Client, reads, token), cancellationToken);
	}

	/// <summary>Compares two target ranges and lists how they differ.</summary>
	[McpServerTool(Name = CheatEngineToolNames.MemoryCompare, Title = "Compare memory", ReadOnly = true,
		Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
	[McpMeta(McpDispatchClass.MetaKey, McpDispatchClass.Short)]
	[Description(
		"Compare two target ranges of up to 16 MiB and list the differing runs with both byte sequences. Reads 1 MiB per Cheat Engine dispatch and fails with target_changed if Cheat Engine selects another process in between; stops once maxDifferences runs are listed.")]
	public MemoryCompareResult Compare(
		[Description("The first address or Cheat Engine address expression.")]
		string addressA,
		[Description("The second address or Cheat Engine address expression.")]
		string addressB,
		[Description("The number of bytes to compare, 1 to 16777216.")]
		int size,
		[Description("The most differing runs to list, 1 to 256.")]
		int maxDifferences = 64,
		CancellationToken cancellationToken = default)
	{
		string first = MemoryTargets.RequireExpression(addressA, "addressA");
		string second = MemoryTargets.RequireExpression(addressB, "addressB");
		MemoryTargets.RequireRange(size, "size", 1, MaximumCompareBytes);
		MemoryTargets.RequireRange(maxDifferences, "maxDifferences", 1, MaximumDifferences);
		List<MemoryDifference> differences = [];
		bool truncated = false;
		(Address a, Address b, long epoch, byte[] chunkA, byte[] chunkB) = _dispatch.Run(
			CheatEngineToolNames.MemoryCompare, token =>
			{
				ICheatEngineClient client = _dispatch.Client;
				Address resolvedA = MemoryTargets.Resolve(client, first, "addressA", token);
				Address resolvedB = MemoryTargets.Resolve(client, second, "addressB", token);
				long observed = MemoryTargets.SelectionEpoch(client, token);
				int length = Math.Min(size, MemoryTargets.ChunkBytes);
				return (resolvedA, resolvedB, observed, ReadArray(client, resolvedA, length, token),
					ReadArray(client, resolvedB, length, token));
			}, cancellationToken);
		int offset = 0;
		while (true)
		{
			truncated = Diff(chunkA, chunkB, offset, differences, maxDifferences);
			offset += chunkA.Length;
			if (truncated || offset >= size)
			{
				break;
			}

			int length = Math.Min(size - offset, MemoryTargets.ChunkBytes);
			int start = offset;
			(chunkA, chunkB) = _dispatch.Run(CheatEngineToolNames.MemoryCompare, token =>
			{
				ICheatEngineClient client = _dispatch.Client;
				MemoryTargets.RequireSameTarget(client, epoch, CheatEngineToolNames.MemoryCompare, token);
				return (ReadArray(client, a + start, length, token), ReadArray(client, b + start, length, token));
			}, cancellationToken);
		}

		return new MemoryCompareResult(HexFormat.Address(a), HexFormat.Address(b), size, differences.Count == 0,
			truncated, [.. differences]);
	}

	/// <summary>Hashes a target range.</summary>
	[McpServerTool(Name = CheatEngineToolNames.MemoryHash, Title = "Hash memory", ReadOnly = true,
		Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
	[McpMeta(McpDispatchClass.MetaKey, McpDispatchClass.Short)]
	[Description(
		"Hash a target range of up to 64 MiB with MD5, SHA-1 or SHA-256, for example to detect that code or data changed. Reads 1 MiB per Cheat Engine dispatch and fails with target_changed if Cheat Engine selects another process in between; an unreadable byte fails with memory_read_failed.")]
	public MemoryHashResult Hash(
		[Description("An address or Cheat Engine address expression.")]
		string address,
		[Description("The number of bytes to hash, 1 to 67108864.")]
		int size,
		[Description("The hash algorithm: md5, sha1 or sha256.")]
		MemoryHashAlgorithm algorithm = MemoryHashAlgorithm.Sha256,
		CancellationToken cancellationToken = default)
	{
		string expression = MemoryTargets.RequireExpression(address, "address");
		MemoryTargets.RequireRange(size, "size", 1, MaximumHashBytes);
		HashAlgorithmName name = algorithm switch
		{
			MemoryHashAlgorithm.Md5 => HashAlgorithmName.MD5,
			MemoryHashAlgorithm.Sha1 => HashAlgorithmName.SHA1,
			MemoryHashAlgorithm.Sha256 => HashAlgorithmName.SHA256,
			_ => throw CheatEngineToolException.InvalidArgument("algorithm", "must be md5, sha1 or sha256.")
		};
		using IncrementalHash hash = IncrementalHash.CreateHash(name);
		(Address target, long epoch, byte[] chunk) = _dispatch.Run(CheatEngineToolNames.MemoryHash, token =>
		{
			ICheatEngineClient client = _dispatch.Client;
			Address resolved = MemoryTargets.Resolve(client, expression, "address", token);
			return (resolved, MemoryTargets.SelectionEpoch(client, token),
				ReadArray(client, resolved, Math.Min(size, MemoryTargets.ChunkBytes), token));
		}, cancellationToken);
		int offset = 0;
		while (true)
		{
			hash.AppendData(chunk);
			offset += chunk.Length;
			if (offset >= size)
			{
				break;
			}

			int start = offset;
			int length = Math.Min(size - offset, MemoryTargets.ChunkBytes);
			chunk = _dispatch.Run(CheatEngineToolNames.MemoryHash, token =>
			{
				ICheatEngineClient client = _dispatch.Client;
				MemoryTargets.RequireSameTarget(client, epoch, CheatEngineToolNames.MemoryHash, token);
				return ReadArray(client, target + start, length, token);
			}, cancellationToken);
		}

		return new MemoryHashResult(HexFormat.Address(target), size, algorithm,
			Convert.ToHexStringLower(hash.GetHashAndReset()));
	}

	/// <summary>Reads an exact byte range inside the current dispatch.</summary>
	internal static byte[] ReadArray(ICheatEngineClient client, Address address, int length,
		CancellationToken cancellationToken)
	{
		return [.. client.Memory.ReadBytes(new MemoryBytesReadRequest(address, length), cancellationToken)];
	}

	/// <summary>
	///     Checks the shape arguments of one read: <paramref name="size" /> only for bytes and <paramref name="length" />
	///     only for strings.
	/// </summary>
	/// <returns>The length to pass to the codec.</returns>
	private static int? CheckShape(McpValueType valueType, int? size, int? length, string sizeParameter,
		string lengthParameter)
	{
		switch (valueType)
		{
			case McpValueType.Bytes:
				if (length is not null)
				{
					throw CheatEngineToolException.InvalidArgument(lengthParameter,
						"applies only to string and wstring; use size for bytes.");
				}

				if (size is not { } byteCount)
				{
					throw CheatEngineToolException.InvalidArgument(sizeParameter, "is required for bytes.");
				}

				return (int) MemoryTargets.RequireRange(byteCount, sizeParameter, 1, MaximumBytes);
			case McpValueType.String or McpValueType.WString:
				if (size is not null)
				{
					throw CheatEngineToolException.InvalidArgument(sizeParameter,
						"applies only to bytes; use length for strings.");
				}

				return (int) MemoryTargets.RequireRange(length ?? McpValueCodec.DefaultStringLength, lengthParameter, 1,
					MaximumStringLength);
			default:
				if (size is not null)
				{
					throw CheatEngineToolException.InvalidArgument(sizeParameter, "applies only to bytes.");
				}

				if (length is not null)
				{
					throw CheatEngineToolException.InvalidArgument(lengthParameter,
						"applies only to string and wstring.");
				}

				return null;
		}
	}

	private static BatchRead[] CheckBatch(MemoryReadItem[]? items)
	{
		if (items is null || items.Length == 0)
		{
			throw CheatEngineToolException.InvalidArgument("items", "must list at least one address.");
		}

		if (items.Length > MaximumBatchItems)
		{
			throw CheatEngineToolException.LimitExceeded("items", $"accepts at most {MaximumBatchItems} addresses.");
		}

		BatchRead[] reads = new BatchRead[items.Length];
		long total = 0;
		for (int index = 0; index < items.Length; index++)
		{
			string prefix = $"items[{index.ToString(CultureInfo.InvariantCulture)}]";
			MemoryReadItem item = items[index] ??
								  throw CheatEngineToolException.InvalidArgument(prefix, "must be an object.");
			string expression = MemoryTargets.RequireExpression(item.Address, prefix + ".address");
			MemoryTargets.RequireType(item.ValueType, prefix + ".valueType");
			int? length = CheckShape(item.ValueType, item.Size, item.Length, prefix + ".size", prefix + ".length");
			// A pointer is charged at 8 bytes and a UTF-16 character at 2, whatever the target.
			total += McpValueCodec.FixedSize(item.ValueType, 8) ??
					 (item.ValueType is McpValueType.WString ? 2L * length!.Value : length!.Value);
			reads[index] = new BatchRead(expression, item.ValueType, length);
		}

		return total <= MaximumBatchBytes
			? reads
			: throw CheatEngineToolException.LimitExceeded("items",
				$"read {total.ToString(CultureInfo.InvariantCulture)} bytes in total; the limit is {MaximumBatchBytes}.");
	}

	private static MemoryReadBatchResult ReadBatch(ICheatEngineClient client, BatchRead[] reads,
		CancellationToken cancellationToken)
	{
		MemoryReadBatchEntry?[] entries = new MemoryReadBatchEntry?[reads.Length];
		Address[] addresses = new Address[reads.Length];
		for (int index = 0; index < reads.Length; index++)
		{
			if (MemoryTargets.TryResolve(client, reads[index].Expression, out addresses[index],
					out CheatEngineFailure failure, cancellationToken))
			{
				continue;
			}

			entries[index] = MemoryTargets.IsItemFailure(failure)
				? new MemoryReadBatchEntry(reads[index].Expression,
					Error: MemoryTargets.ItemError(client, failure))
				: throw CheatEngineToolException.FromFailure(failure, client.Stopping.IsCancellationRequested);
		}

		foreach (IGrouping<McpValueType, int> run in Enumerable.Range(0, reads.Length)
					 .Where(index =>
						 entries[index] is null && McpValueCodec.FixedSize(reads[index].Type, 8) is not null)
					 .GroupBy(index => reads[index].Type))
		{
			int[] indices = [.. run];
			switch (run.Key)
			{
				case McpValueType.Int8:
					ReadPrimitives<sbyte>(client, indices, addresses, entries, cancellationToken);
					break;
				case McpValueType.UInt8:
					ReadPrimitives<byte>(client, indices, addresses, entries, cancellationToken);
					break;
				case McpValueType.Int16:
					ReadPrimitives<short>(client, indices, addresses, entries, cancellationToken);
					break;
				case McpValueType.UInt16:
					ReadPrimitives<ushort>(client, indices, addresses, entries, cancellationToken);
					break;
				case McpValueType.Int32:
					ReadPrimitives<int>(client, indices, addresses, entries, cancellationToken);
					break;
				case McpValueType.UInt32:
					ReadPrimitives<uint>(client, indices, addresses, entries, cancellationToken);
					break;
				case McpValueType.Int64:
					ReadPrimitives<long>(client, indices, addresses, entries, cancellationToken);
					break;
				case McpValueType.UInt64:
					ReadPrimitives<ulong>(client, indices, addresses, entries, cancellationToken);
					break;
				case McpValueType.Float:
					ReadPrimitives<float>(client, indices, addresses, entries, cancellationToken);
					break;
				case McpValueType.Double:
					ReadPrimitives<double>(client, indices, addresses, entries, cancellationToken);
					break;
				default:
					ReadPrimitives<Address>(client, indices, addresses, entries, cancellationToken);
					break;
			}
		}

		for (int index = 0; index < reads.Length; index++)
		{
			if (entries[index] is null)
			{
				entries[index] = ReadVariable(client, reads[index], addresses[index], cancellationToken);
			}
		}

		MemoryReadBatchEntry[] items = [.. entries.Select(static entry => entry!)];
		return new MemoryReadBatchResult(items.Count(static item => item.Error is not null), items);
	}

	/// <summary>
	///     Reads one fixed-size type in Client batches of at most <see cref="MemoryBatchLimits.MaximumOperationCount" />
	///     addresses and the Client's payload bound, resuming after each failed index.
	/// </summary>
	private static void ReadPrimitives<T>(ICheatEngineClient client, int[] indices, Address[] addresses,
		MemoryReadBatchEntry?[] entries, CancellationToken cancellationToken) where T : unmanaged
	{
		int limit = Math.Min(MemoryBatchLimits.MaximumOperationCount,
			MemoryResourceLimits.DefaultMaximumBatchPayloadBytes / 8);
		int position = 0;
		while (position < indices.Length)
		{
			int count = Math.Min(indices.Length - position, limit);
			Address[] chunk = new Address[count];
			for (int index = 0; index < count; index++)
			{
				chunk[index] = addresses[indices[position + index]];
			}

			MemoryPrimitiveBatchReadOutcome<T> outcome =
				client.Memory.ReadPrimitiveBatchDetailed(new MemoryPrimitiveBatchReadRequest<T>(chunk),
					cancellationToken);
			ImmutableArray<T> values = outcome.Values;
			for (int index = 0; index < outcome.CompletedCount; index++)
			{
				entries[indices[position + index]] =
					new MemoryReadBatchEntry(HexFormat.Address(chunk[index]), McpValueCodec.Format(values[index]));
			}

			if (outcome.IsSuccess)
			{
				position += count;
				continue;
			}

			CheatEngineFailure failure = outcome.Failure!.Value;
			if (outcome.FailedIndex is not { } failed || !MemoryTargets.IsItemFailure(failure))
			{
				throw CheatEngineToolException.FromFailure(failure, client.Stopping.IsCancellationRequested);
			}

			entries[indices[position + failed]] = new MemoryReadBatchEntry(HexFormat.Address(chunk[failed]),
				Error: MemoryTargets.ItemError(client, failure));
			position += failed + 1;
		}
	}

	private static MemoryReadBatchEntry ReadVariable(ICheatEngineClient client, BatchRead read, Address address,
		CancellationToken cancellationToken)
	{
		string resolved = HexFormat.Address(address);
		CheatEngineFailure failure;
		if (read.Type is McpValueType.Bytes)
		{
			MemoryBytesReadOutcome outcome = client.Memory.ReadBytesDetailed(
				new MemoryBytesReadRequest(address, read.Length!.Value), cancellationToken);
			if (outcome.IsSuccess)
			{
				return new MemoryReadBatchEntry(resolved, HexFormat.Bytes(outcome.Bytes.AsSpan()));
			}

			failure = outcome.Failure!.Value;
		}
		else if (client.Memory.TryReadString(new MemoryStringReadRequest(address, read.Length!.Value,
						 read.Type is McpValueType.WString ? MemoryStringEncoding.Utf16 : MemoryStringEncoding.Utf8),
					 out string? text, out failure, cancellationToken))
		{
			return new MemoryReadBatchEntry(resolved, text);
		}

		return MemoryTargets.IsItemFailure(failure)
			? new MemoryReadBatchEntry(resolved, Error: MemoryTargets.ItemError(client, failure))
			: throw CheatEngineToolException.FromFailure(failure, client.Stopping.IsCancellationRequested);
	}

	/// <summary>Appends the differing runs of one chunk pair; returns whether more runs exist than may be listed.</summary>
	private static bool Diff(byte[] a, byte[] b, int baseOffset, List<MemoryDifference> differences, int maximum)
	{
		int index = 0;
		while (index < a.Length)
		{
			if (a[index] == b[index])
			{
				index++;
				continue;
			}

			int start = index;
			while (index < a.Length && a[index] != b[index] && index - start < MaximumDifferenceBytes)
			{
				index++;
			}

			if (differences.Count == maximum)
			{
				return true;
			}

			int length = index - start;
			differences.Add(new MemoryDifference(HexFormat.Offset(baseOffset + start), length,
				HexFormat.Bytes(a.AsSpan(start, length)), HexFormat.Bytes(b.AsSpan(start, length))));
		}

		return false;
	}

	private sealed record BatchRead(string Expression, McpValueType Type, int? Length);
}
