using System.Globalization;

namespace CheatEngine.Mcp.Core.Jobs;

/// <summary>
///     The identity and limits a new job receives from <see cref="JobRegistry" />. A Lua job's start script receives them
///     as its first arguments: <c>a[1]</c> the namespace, <c>a[2]</c> the id, <c>a[3]</c> the buffer limit and <c>a[4]</c>
///     the TTL in milliseconds, then its own arguments from <c>a[5]</c>.
/// </summary>
/// <param name="Id">The job id, <c>kind-namespace-number</c>.</param>
/// <param name="Namespace">The activation namespace.</param>
/// <param name="Kind">The job kind.</param>
/// <param name="TimeToLive">How long the job lives, and its results stay pollable, from its start.</param>
/// <param name="BufferLimit">How many items the job retains; beyond it the oldest are evicted.</param>
/// <param name="CreatedUtc">When the job started.</param>
public sealed record JobStart(
	string Id,
	string Namespace,
	string Kind,
	TimeSpan TimeToLive,
	int BufferLimit,
	DateTimeOffset CreatedUtc)
{
	/// <summary>The TTL in whole milliseconds, as the Lua kernel takes it.</summary>
	public int TimeToLiveMilliseconds => (int) TimeToLive.TotalMilliseconds;

	/// <summary>When the job expires.</summary>
	public DateTimeOffset ExpiresUtc => CreatedUtc + TimeToLive;

	/// <inheritdoc />
	public override string ToString()
	{
		return string.Create(CultureInfo.InvariantCulture, $"{Id} (TTL {TimeToLiveMilliseconds} ms)");
	}
}
