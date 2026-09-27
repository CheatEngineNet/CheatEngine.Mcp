using System.ComponentModel;

using CheatEngine.Client;
using CheatEngine.Client.Memory;
using CheatEngine.Client.Results;
using CheatEngine.Mcp.Core.Contract;
using CheatEngine.Mcp.Core.Files;
using CheatEngine.Mcp.Core.Values;
using CheatEngine.SDK.Engine.Inspection;
using CheatEngine.SDK.Engine.Values;

using ModelContextProtocol.Server;

namespace CheatEngine.Mcp.Tools.Memory;

/// <summary>The <c>memory_*</c> tool that writes target memory to a host file under the configured write roots.</summary>
[McpServerToolType]
public sealed class MemoryFileTools
{
	/// <summary>The largest range of <c>memory_dump_to_file</c>.</summary>
	internal const int MaximumDumpBytes = 256 * 1024 * 1024;

	/// <summary>The most zero-filled ranges one result lists.</summary>
	internal const int MaximumZeroRanges = 256;

	private const int PageBytes = 4096;

	private readonly ToolDispatch _dispatch;
	private readonly McpFilePaths _files;

	/// <summary>Creates the tool; the constructor does no Cheat Engine or file work.</summary>
	/// <param name="dispatch">The activation's dispatch facade.</param>
	/// <param name="files">The activation's host-file policy.</param>
	public MemoryFileTools(ToolDispatch dispatch, McpFilePaths files)
	{
		ArgumentNullException.ThrowIfNull(dispatch);
		ArgumentNullException.ThrowIfNull(files);
		_dispatch = dispatch;
		_files = files;
	}

	/// <summary>Writes a target range to a file under the configured write roots.</summary>
	[McpServerTool(Name = CheatEngineToolNames.MemoryDumpToFile, Title = "Dump memory to a file", ReadOnly = false,
		Destructive = false, Idempotent = true, OpenWorld = true, UseStructuredContent = true)]
	[McpMeta(McpDispatchClass.MetaKey, McpDispatchClass.Short)]
	[Description(
		"Write up to 256 MiB of target memory to a file on the Cheat Engine host. The path must be absolute, local and inside a folder listed in Mcp:Files:AllowedRoots (empty by default, which refuses every dump); the MCP data and registry folders are always refused. Reads 1 MiB per dispatch into a temporary file that replaces the target file only when complete; unreadable memory fails the dump unless unreadable is zero.")]
	public FileDumpResult DumpToFile(
		[Description("An address or Cheat Engine address expression.")]
		string address,
		[Description("The number of bytes to dump, 1 to 268435456.")]
		int size,
		[Description("The absolute path of the file to write, inside a folder of Mcp:Files:AllowedRoots.")]
		string path,
		[Description("Whether an existing file may be replaced.")]
		bool overwrite = false,
		[Description("What to do with unreadable memory: fail (default, no file is written) or zero (write zeros and list them).")]
		UnreadableMemory unreadable = UnreadableMemory.Fail,
		CancellationToken cancellationToken = default)
	{
		string expression = MemoryTargets.RequireExpression(address, "address");
		MemoryTargets.RequireRange(size, "size", 1, MaximumDumpBytes);
		bool zeroFill = unreadable is UnreadableMemory.Zero;
		ZeroRanges zeros = new();
		using McpFileWrite write = OpenWrite(path, overwrite);
		FileStream stream = write.Stream;
		(Address target, long epoch, Chunk first) = _dispatch.Run(CheatEngineToolNames.MemoryDumpToFile, token =>
		{
			ICheatEngineClient client = _dispatch.Client;
			Address resolved = MemoryTargets.Resolve(client, expression, "address", token);
			return (resolved, MemoryTargets.SelectionEpoch(client, token),
				ReadChunk(client, resolved, Math.Min(size, MemoryTargets.ChunkBytes), zeroFill, token));
		}, cancellationToken);
		Chunk chunk = first;
		int offset = 0;
		while (true)
		{
			zeros.Add(offset, chunk.Zeros);
			Write(stream, chunk.Bytes);
			offset += chunk.Bytes.Length;
			if (offset >= size)
			{
				break;
			}

			int start = offset;
			int length = Math.Min(size - offset, MemoryTargets.ChunkBytes);
			chunk = _dispatch.Run(CheatEngineToolNames.MemoryDumpToFile, token =>
			{
				ICheatEngineClient client = _dispatch.Client;
				MemoryTargets.RequireSameTarget(client, epoch, CheatEngineToolNames.MemoryDumpToFile, token);
				return ReadChunk(client, target + start, length, zeroFill, token);
			}, cancellationToken);
		}

		Complete(write);
		return new FileDumpResult(write.FullPath, HexFormat.Address(target), size, zeros.Bytes, [.. zeros.Listed],
			zeros.Truncated);
	}

	/// <summary>
	///     Reads one chunk inside the current dispatch. With <paramref name="zeroFill" />, an unreadable part is skipped
	///     to the end of its region when Cheat Engine reports it uncommitted or unreadable, and page by page otherwise.
	/// </summary>
	private static Chunk ReadChunk(ICheatEngineClient client, Address start, int length, bool zeroFill,
		CancellationToken cancellationToken)
	{
		byte[] buffer = new byte[length];
		List<(int Offset, int Length)> zeros = [];
		int position = 0;
		while (position < length)
		{
			int take = position == 0 ? length : Math.Min(length - position, PageBytes - PageOffset(start + position));
			MemoryBytesReadOutcome outcome =
				client.Memory.ReadBytesDetailed(new MemoryBytesReadRequest(start + position, take), cancellationToken);
			// A failure before Cheat Engine returned anything has a default, empty prefix.
			outcome.Bytes.AsSpan().CopyTo(buffer.AsSpan(position));
			if (outcome.IsSuccess)
			{
				position += take;
				continue;
			}

			CheatEngineFailure failure = outcome.Failure!.Value;
			if (!zeroFill || failure.Kind is not CheatEngineFailureKind.MemoryReadFailed)
			{
				throw CheatEngineToolException.FromFailure(failure, client.Stopping.IsCancellationRequested);
			}

			position += outcome.ConfirmedLength;
			int skip = UnreadableSpan(client, start + position, length - position, cancellationToken);
			zeros.Add((position, skip));
			position += skip;
		}

		return new Chunk(buffer, [.. zeros]);
	}

	/// <summary>How many bytes from an address that failed to read are written as zeros.</summary>
	private static int UnreadableSpan(ICheatEngineClient client, Address address, int remaining,
		CancellationToken cancellationToken)
	{
		int page = Math.Min(remaining, PageBytes - PageOffset(address));
		if (!client.Inspection.TryGetMemoryRegion(address, out MemoryRegionInfo region, out _, cancellationToken))
		{
			return page;
		}

		MemoryAccess access = MemoryTargets.Access(region.Protection);
		if (region.State is MemoryRegionState.Committed && access.Read)
		{
			return page;
		}

		ulong regionEnd = region.BaseAddress.ToUInt64() + region.Size.Value;
		ulong value = address.ToUInt64();
		return regionEnd > value ? (int) Math.Min((ulong) remaining, regionEnd - value) : page;
	}

	private static int PageOffset(Address address)
	{
		return (int) (address.ToUInt64() % PageBytes);
	}

	private McpFileWrite OpenWrite(string path, bool overwrite)
	{
		try
		{
			return _files.BeginWrite(path, CheatEngineToolNames.MemoryDumpToFile, overwrite);
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
		{
			throw FileFailure("The dump file could not be created.", exception);
		}
	}

	private static void Write(FileStream stream, byte[] bytes)
	{
		try
		{
			stream.Write(bytes);
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
		{
			throw FileFailure("The dump file could not be written.", exception);
		}
	}

	private static void Complete(McpFileWrite write)
	{
		try
		{
			write.Commit();
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
		{
			throw FileFailure("The dump file could not be completed; the target file is unchanged.", exception);
		}
	}

	private static CheatEngineToolException FileFailure(string message, Exception exception)
	{
		// Only memory was read: the target is unchanged, and the target file is replaced only by a complete dump.
		return new CheatEngineToolException(new ToolError(ToolErrorKind.InvalidState, message,
			CheatEngineToolNames.MemoryDumpToFile, ToolHostEffect.NotApplied, false,
			"Check that the folder is writable and has free space, then repeat the dump."), exception);
	}

	private sealed record Chunk(byte[] Bytes, (int Offset, int Length)[] Zeros);

	/// <summary>The zero-filled ranges of a dump, merged when adjacent and listed up to a bound.</summary>
	private sealed class ZeroRanges
	{
		private int _lastEnd = -1;

		internal List<ZeroFilledRange> Listed
		{
			get;
		} = [];

		internal int Bytes
		{
			get;
			private set;
		}

		internal bool Truncated
		{
			get;
			private set;
		}

		internal void Add(int chunkOffset, (int Offset, int Length)[] ranges)
		{
			foreach ((int offset, int length) in ranges)
			{
				int start = chunkOffset + offset;
				Bytes += length;
				if (start == _lastEnd && !Truncated)
				{
					ZeroFilledRange last = Listed[^1];
					Listed[^1] = last with
					{
						Length = last.Length + length
					};
				}
				else if (Listed.Count < MaximumZeroRanges)
				{
					Listed.Add(new ZeroFilledRange(HexFormat.Offset(start), length));
				}
				else
				{
					Truncated = true;
				}

				_lastEnd = start + length;
			}
		}
	}
}
