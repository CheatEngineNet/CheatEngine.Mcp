using CheatEngine.Mcp.Tools.Modules;

namespace CheatEngine.Mcp.Tests.Tools.Modules;

/// <summary>How compared bytes become reported ranges.</summary>
public sealed class PatchCollectorTests
{
	[Fact]
	public void Compare_RunsWithinTheMergeGap_BecomeOneRangeWithTheBytesBetween()
	{
		PatchCollector collector = new(8);
		byte[] file = [0x90, 0x90, 0x90, 0x90, 0x90, 0x90, 0x90, 0x90, 0x90, 0x90, 0x90, 0x90];
		byte[] memory = [0x90, 0xE9, 0x90, 0x90, 0x90, 0xCC, 0x90, 0x90, 0x90, 0x90, 0xCC, 0x90];

		collector.Compare(0x1000, ".text", file, memory);
		collector.Break();

		// 0x1001 and 0x1005 are three equal bytes apart: merged. 0x100A is four apart: a new range.
		Assert.Equal(2, collector.Patches.Count);
		CollectedPatch first = collector.Patches[0];
		Assert.Equal((0x1001u, ".text", 5), (first.Rva, first.Section, first.Length));
		Assert.Equal([0x90, 0x90, 0x90, 0x90, 0x90], first.FileBytes);
		Assert.Equal([0xE9, 0x90, 0x90, 0x90, 0xCC], first.MemoryBytes);
		Assert.Equal((0x100Au, 1), (collector.Patches[1].Rva, collector.Patches[1].Length));
	}

	[Fact]
	public void Compare_ContiguousCalls_ContinueOneRangeAndAGapBreaksIt()
	{
		PatchCollector collector = new(8);

		collector.Compare(0x1000, ".text", [1, 1], [1, 2]);
		collector.Compare(0x1002, ".text", [1, 1], [2, 1]);
		collector.Compare(0x1010, ".text", [1], [3]);
		collector.Break();

		Assert.Equal([(0x1001u, 2), (0x1010u, 1)],
			collector.Patches.Select(static patch => (patch.Rva, patch.Length)));
	}

	[Fact]
	public void Compare_ExcludedAddresses_CountAsEqual()
	{
		PatchCollector collector = new(8);

		collector.Compare(0x2000, ".rdata", [0, 0, 0, 0], [9, 9, 9, 9], static address => address < 0x2003);
		collector.Break();

		CollectedPatch patch = Assert.Single(collector.Patches);
		Assert.Equal((0x2003u, 1), (patch.Rva, patch.Length));
	}

	[Fact]
	public void Compare_LongRange_KeepsItsLengthButShowsAtMost64Bytes()
	{
		PatchCollector collector = new(8);
		byte[] file = new byte[100];
		byte[] memory = Enumerable.Repeat((byte) 0xCC, 100).ToArray();

		collector.Compare(0x3000, ".text", file, memory);
		collector.Break();

		CollectedPatch patch = Assert.Single(collector.Patches);
		Assert.Equal(100, patch.Length);
		Assert.Equal(PatchCollector.MaximumShownBytes, patch.FileBytes.Length);
		Assert.Equal(PatchCollector.MaximumShownBytes, patch.MemoryBytes.Length);
	}

	[Fact]
	public void Compare_RangeBeyondTheLimit_MarksTheCollectorFull()
	{
		PatchCollector collector = new(2);

		collector.Compare(0x1000, ".text", [0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0],
			[1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0]);
		collector.Break();

		Assert.True(collector.IsFull);
		Assert.Equal([0x1000u, 0x1005u], collector.Patches.Select(static patch => patch.Rva));
	}

	[Fact]
	public void Compare_UnequalRuns_AreRejected()
	{
		PatchCollector collector = new(1);

		Assert.Throws<ArgumentException>(() => collector.Compare(0, ".text", [1, 2], [1]));
		Assert.Throws<ArgumentOutOfRangeException>(() => new PatchCollector(0));
	}
}
