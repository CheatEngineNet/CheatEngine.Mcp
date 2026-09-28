using CheatEngine.Mcp.Core.Contract;
using CheatEngine.Mcp.Core.Jobs;

namespace CheatEngine.Mcp.Tests.Core;

/// <summary>The managed ring buffer: sequences, oldest-first eviction and a non-consuming cursor.</summary>
public sealed class JobWriterTests
{
	private static readonly int[] Retained = [3, 4, 5];
	private static readonly int[] OldestRetained = [3];
	private static readonly int[] AfterDiscard = [];

	[Fact]
	public void Read_FullRing_EvictsTheOldestItemsAndShowsTheGapThroughFirstSequence()
	{
		JobWriter<int> writer = new(3);
		for (int item = 1; item <= 5; item++)
		{
			Assert.True(writer.Add(item));
		}

		JobWriterPage<int> page = writer.Read(0, 10);
		Assert.Equal(Retained, page.Items);
		Assert.Equal((3L, 5L, false, 2L), (page.FirstSequence, page.NextAfterSequence, page.More, page.Dropped));

		JobWriterPage<int> behind = writer.Read(1, 1);
		Assert.Equal(OldestRetained, behind.Items);
		Assert.Equal((3L, true), (behind.NextAfterSequence, behind.More));

		JobWriterPage<int> caughtUp = writer.Read(5, 10);
		Assert.Empty(caughtUp.Items);
		Assert.Equal((5L, false), (caughtUp.NextAfterSequence, caughtUp.More));
		Assert.Equal((3L, 5L, 2L), (writer.Counters().Buffered, writer.Counters().Total, writer.Counters().Dropped));
	}

	[Fact]
	public void Read_SameCursor_IsIdempotentAndNeverConsumes()
	{
		JobWriter<int> writer = new(8);
		writer.Add(10);
		writer.Add(20);

		JobWriterPage<int> first = writer.Read(0, 1);
		JobWriterPage<int> again = writer.Read(0, 1);

		Assert.Equal(first.Items, again.Items);
		Assert.Equal((first.NextAfterSequence, first.More), (again.NextAfterSequence, again.More));
		Assert.Equal(2, writer.Read(0, 10).Items.Length);
	}

	[Fact]
	public void Read_CursorPastTheLastSequenceOrEmptyBuffer_IsRefusedOrUnchanged()
	{
		JobWriter<int> writer = new(4);

		JobWriterPage<int> empty = writer.Read(0, 5);
		Assert.Empty(empty.Items);
		Assert.Equal((1L, 0L, false), (empty.FirstSequence, empty.NextAfterSequence, empty.More));

		CheatEngineToolException error = Assert.Throws<CheatEngineToolException>(() => writer.Read(1, 5));
		Assert.Equal((ToolErrorKind.InvalidArgument, ToolHostEffect.NotStarted),
			(error.Error.Kind, error.Error.HostEffect));
	}

	[Fact]
	public void Close_EndedJob_IgnoresLaterItemsAndProgressAndDiscardDropsItems()
	{
		JobWriter<int> writer = new(4);
		writer.Add(1);
		writer.Progress(1, 10);
		writer.Close();

		Assert.False(writer.Add(2));
		writer.Progress(5, 10);
		Assert.Equal((1L, 10L), (writer.Counters().ProgressDone!.Value, writer.Counters().ProgressTotal!.Value));

		writer.Discard();
		Assert.Equal(AfterDiscard, writer.Read(1, 10).Items);
		Assert.Equal(1L, writer.Counters().Total);
		Assert.Throws<ArgumentOutOfRangeException>(() => writer.Progress(-1));
		Assert.Throws<ArgumentOutOfRangeException>(() => new JobWriter<int>(0));
	}
}
