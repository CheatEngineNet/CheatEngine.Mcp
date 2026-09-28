namespace CheatEngine.Mcp.Tools.Pointer;

/// <summary>One stored pointer path: a root, and the offsets applied after each dereference.</summary>
/// <param name="BaseAddress">The root address when the path was found.</param>
/// <param name="Module">The module that holds the root, which rebases the path, or <see langword="null" />.</param>
/// <param name="ModuleOffset">The root's offset in <paramref name="Module" />; 0 without a module.</param>
/// <param name="Offsets">The offsets in dereference order, one per pointer read.</param>
internal sealed record PointerPath(ulong BaseAddress, string? Module, ulong ModuleOffset, long[] Offsets)
{
	/// <summary>How the path was last verified.</summary>
	internal PointerVerification Verification
	{
		get;
		init;
	} = PointerVerification.SnapshotMatch;

	/// <summary>The sum of the offsets' magnitudes, used to rank paths.</summary>
	internal ulong OffsetSum
	{
		get
		{
			ulong sum = 0;
			foreach (long offset in Offsets)
			{
				ulong magnitude = offset < 0 ? unchecked((ulong) -(offset + 1)) + 1 : (ulong) offset;
				sum = sum > ulong.MaxValue - magnitude ? ulong.MaxValue : sum + magnitude;
			}

			return sum;
		}
	}
}
