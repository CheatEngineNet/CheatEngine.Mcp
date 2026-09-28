namespace CheatEngine.Mcp.Core.Values;

/// <summary>One page of a listing; tools copy its fields into their own result record.</summary>
/// <typeparam name="T">The item type.</typeparam>
/// <param name="Items">The items of this page.</param>
/// <param name="Total">The number of items in the whole listing.</param>
/// <param name="NextOffset">The offset of the next page; <see langword="null" /> (omitted) when the listing is complete.</param>
/// <param name="Truncated">Whether a host-side cap shortened the listing itself, not merely this page.</param>
public readonly record struct PageSlice<T>(IReadOnlyList<T> Items, int Total, int? NextOffset, bool Truncated);
