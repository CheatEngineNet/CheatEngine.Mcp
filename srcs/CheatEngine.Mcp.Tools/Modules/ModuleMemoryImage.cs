using CheatEngine.Client;
using CheatEngine.Client.Memory;
using CheatEngine.Client.Results;
using CheatEngine.Mcp.Core.Contract;
using CheatEngine.SDK.Engine.Values;

namespace CheatEngine.Mcp.Tools.Modules;

/// <summary>
///     A module mapped in target memory, read on demand in 64 KiB pages through the Client and cached for one dispatch.
///     Every read stays inside the module and the whole object reads at most its byte budget.
/// </summary>
internal sealed class ModuleMemoryImage : IPeImage
{
	private const int PageSize = 64 * 1024;

	private readonly ulong _base;
	private readonly long _budget;
	private readonly ICheatEngineClient _client;
	private readonly string _module;
	private readonly Dictionary<ulong, Page> _pages = [];
	private readonly ulong _size;
	private readonly CancellationToken _token;

	/// <summary>Creates the view; construct it inside the dispatch that reads through it.</summary>
	/// <param name="client">The activation's Client.</param>
	/// <param name="module">The module name, used in errors.</param>
	/// <param name="baseAddress">The module's base address.</param>
	/// <param name="size">The module's mapped size.</param>
	/// <param name="budget">The most bytes this view may read from the target.</param>
	/// <param name="token">The dispatch body's token.</param>
	internal ModuleMemoryImage(ICheatEngineClient client, string module, ulong baseAddress, ulong size, long budget,
		CancellationToken token)
	{
		_client = client;
		_module = module;
		_base = baseAddress;
		_size = size;
		_budget = budget;
		_token = token;
	}

	/// <summary>How many bytes this view read from the target.</summary>
	internal long BytesRead
	{
		get;
		private set;
	}

	/// <inheritdoc />
	/// <exception cref="CheatEngineToolException">
	///     <c>memory_read_failed</c> when the range is unreadable, <c>limit_exceeded</c> past the byte budget.
	/// </exception>
	public void Read(uint rva, Span<byte> destination)
	{
		if (rva + (ulong) destination.Length > _size)
		{
			throw new InvalidDataException(
				$"A table at RVA {rva:X} with {destination.Length} bytes lies outside the module's {_size} bytes.");
		}

		int copied = 0;
		while (copied < destination.Length)
		{
			ulong current = rva + (ulong) copied;
			Page page = GetPage(current / PageSize);
			int offset = (int) (current % PageSize);
			int available = page.Bytes.Length - offset;
			if (available <= 0)
			{
				throw CheatEngineToolException.FromFailure(page.Failure!.Value,
					_client.Stopping.IsCancellationRequested);
			}

			int count = Math.Min(available, destination.Length - copied);
			page.Bytes.AsSpan(offset, count).CopyTo(destination[copied..]);
			copied += count;
		}
	}

	private Page GetPage(ulong index)
	{
		if (_pages.TryGetValue(index, out Page? cached))
		{
			return cached;
		}

		ulong start = index * PageSize;
		int length = (int) Math.Min(PageSize, _size - start);
		if (BytesRead + length > _budget)
		{
			throw CheatEngineToolException.LimitExceeded("module",
				$"{_module} would need more than {_budget / (1024 * 1024)} MiB of reads for this request.");
		}

		BytesRead += length;
		MemoryBytesReadOutcome outcome = _client.Memory.ReadBytesDetailed(
			new MemoryBytesReadRequest(new Address(_base + start), length), _token);
		if (outcome.Failure is { } failure && failure.Kind is not CheatEngineFailureKind.MemoryReadFailed)
		{
			throw CheatEngineToolException.FromFailure(failure, _client.Stopping.IsCancellationRequested);
		}

		Page page = new(outcome.Bytes.IsDefault ? [] : [.. outcome.Bytes], outcome.Failure);
		_pages.Add(index, page);
		return page;
	}

	private sealed record Page(byte[] Bytes, CheatEngineFailure? Failure);
}
