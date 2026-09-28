using CheatEngine.Mcp.Core.Contract;

namespace CheatEngine.Mcp.Core.Values;

/// <summary>Validates <c>offset</c> and <c>limit</c> arguments and cuts one page out of a listing.</summary>
public static class Paging
{
	/// <summary>Returns the page that starts at <paramref name="offset" />.</summary>
	/// <typeparam name="T">The item type.</typeparam>
	/// <param name="items">The whole listing.</param>
	/// <param name="offset">The zero-based index of the first item; past the end yields an empty, complete page.</param>
	/// <param name="limit">The largest number of items to return.</param>
	/// <param name="maxLimit">The tool's largest accepted <paramref name="limit" />.</param>
	/// <param name="sourceTruncated">Whether a host-side cap already shortened <paramref name="items" />.</param>
	/// <returns>The page.</returns>
	/// <exception cref="CheatEngineToolException">
	///     <see cref="ToolErrorKind.InvalidArgument" /> for a negative offset or a limit below one, and
	///     <see cref="ToolErrorKind.LimitExceeded" /> for a limit above <paramref name="maxLimit" />.
	/// </exception>
	public static PageSlice<T> Slice<T>(IReadOnlyList<T> items, int offset, int limit, int maxLimit,
		bool sourceTruncated = false)
	{
		ArgumentNullException.ThrowIfNull(items);
		ArgumentOutOfRangeException.ThrowIfLessThan(maxLimit, 1);
		if (offset < 0)
		{
			throw CheatEngineToolException.InvalidArgument("offset", "must be zero or greater.");
		}

		if (limit < 1)
		{
			throw CheatEngineToolException.InvalidArgument("limit", "must be at least 1.");
		}

		if (limit > maxLimit)
		{
			throw CheatEngineToolException.LimitExceeded("limit", $"must be at most {maxLimit}.");
		}

		int total = items.Count;
		int start = Math.Min(offset, total);
		int count = Math.Min(limit, total - start);
		T[] page = new T[count];
		for (int index = 0; index < count; index++)
		{
			page[index] = items[start + index];
		}

		int end = start + count;
		return new PageSlice<T>(page, total, end < total ? end : null, sourceTruncated);
	}
}
