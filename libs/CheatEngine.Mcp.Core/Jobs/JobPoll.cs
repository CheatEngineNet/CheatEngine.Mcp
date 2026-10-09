using System.ComponentModel;

namespace CheatEngine.Mcp.Core.Jobs;

/// <summary>
///     One page of a job's buffered items. Reading this page does not consume it: <c>afterSequence</c> is a cursor,
///     never an acknowledgement. Items remain readable until the job expires or the buffer evicts them.
/// </summary>
/// <typeparam name="TItem">The item type.</typeparam>
/// <param name="Job">The job's status.</param>
/// <param name="Items">The items after the cursor, oldest first, at most the requested limit.</param>
/// <param name="FirstSequence">
///     The oldest retained sequence; when it exceeds the cursor plus one, older items were evicted and are lost.
/// </param>
/// <param name="NextAfterSequence">The cursor to pass to the next poll: the sequence of the last item returned.</param>
/// <param name="More">Whether retained items remain after <paramref name="NextAfterSequence" />.</param>
/// <param name="Dropped">How many of the oldest items were evicted because the buffer was full.</param>
public sealed record JobPoll<TItem>(
	[property: Description("The job's status.")]
	JobStatus Job,
	[property: Description("The items after the cursor, oldest first.")]
	IReadOnlyList<TItem> Items,
	[property: Description("The oldest retained sequence; a gap after the cursor means evicted items.")]
	long FirstSequence,
	[property: Description("The afterSequence to pass to the next poll.")]
	long NextAfterSequence,
	[property: Description("Whether retained items remain after nextAfterSequence.")]
	bool More,
	[property: Description("How many of the oldest items were evicted because the buffer was full.")]
	long Dropped);
