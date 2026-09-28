using System.Collections.Concurrent;

using CheatEngine.Mcp.Core.Contract;
using CheatEngine.Mcp.Tools.Memory;

using Xunit.Sdk;

namespace CheatEngine.Mcp.Tests.Tools.Memory;

/// <summary>
///     The activation's snapshot store: name rules, reservations under concurrency, caps and the activation's end.
/// </summary>
public sealed class MemorySnapshotStoreTests
{
	[Theory]
	[InlineData("a")]
	[InlineData("before.hit-1_B")]
	[InlineData("n123456789012345678901234567890123456789012345678901234567890123")]
	public void RequireName_PrintableAsciiNames_AreAccepted(string name)
	{
		Assert.Equal(name, MemorySnapshotStore.RequireName(name, "name"));
	}

	[Theory]
	[InlineData(null)]
	[InlineData("")]
	[InlineData("with space")]
	[InlineData("slash/name")]
	[InlineData("é")]
	[InlineData("n1234567890123456789012345678901234567890123456789012345678901234")]
	public void RequireName_OtherNames_AreInvalidArguments(string? name)
	{
		CheatEngineToolException exception =
			Assert.Throws<CheatEngineToolException>(() => MemorySnapshotStore.RequireName(name, "compareTo"));

		Assert.Equal(ToolErrorKind.InvalidArgument, exception.Error.Kind);
		Assert.Equal("compareTo", exception.Error.Details!.Value.GetProperty("parameter").GetString());
	}

	[Fact]
	public void Reserve_ConcurrentCalls_NeverExceedTheTotalCap()
	{
		MemorySnapshotStore store = new();
		ConcurrentBag<ToolErrorKind> refusals = [];
		int reserved = 0;

		Parallel.For(0, 64, index =>
		{
			try
			{
				store.Reserve($"s{index}", 8 * 1024 * 1024);
				Interlocked.Increment(ref reserved);
			}
			catch (CheatEngineToolException exception)
			{
				refusals.Add(exception.Error.Kind);
			}
		});

		Assert.Equal(8, reserved);
		Assert.Equal(MemorySnapshotStore.MaximumTotalBytes, store.TotalBytes);
		Assert.All(refusals, static kind => Assert.Equal(ToolErrorKind.LimitExceeded, kind));
		Assert.Equal(56, refusals.Count);
	}

	[Fact]
	public void Reserve_ConcurrentCallsForOneName_ReserveItOnce()
	{
		MemorySnapshotStore store = new();
		int reserved = 0;

		Parallel.For(0, 32, _ =>
		{
			try
			{
				store.Reserve("same", 16);
				Interlocked.Increment(ref reserved);
			}
			catch (CheatEngineToolException exception) when (exception.Error.Kind is ToolErrorKind.InvalidState)
			{
			}
		});

		Assert.Equal(1, reserved);
		Assert.Equal(16L, store.TotalBytes);
	}

	[Fact]
	public void Cancel_ReleasesAReservationButNeverAPublishedSnapshot()
	{
		MemorySnapshotStore store = new();
		store.Reserve("failed", 32);
		store.Reserve("kept", 16);
		store.Commit(Snapshot("kept", 16));

		store.Cancel("failed");
		store.Cancel("kept");

		Assert.Equal(16L, store.TotalBytes);
		Assert.Equal(["kept"], store.List().Select(static snapshot => snapshot.Name));
		store.Reserve("failed", 32);
	}

	[Fact]
	public void Remove_PublishedSnapshot_ReturnsItAndFreesTheName()
	{
		MemorySnapshotStore store = new();
		store.Reserve("one", 16);
		store.Commit(Snapshot("one", 16));

		MemorySnapshot removed = store.Remove("one");

		Assert.Equal(("one", 16), (removed.Name, removed.Size));
		Assert.Equal(0L, store.TotalBytes);
		Assert.Equal(ToolErrorKind.NotFound,
			Assert.Throws<CheatEngineToolException>(() => store.Get("one", "name")).Error.Kind);
		store.Reserve("one", 16);
	}

	[Fact]
	public void Dispose_DropsEverySnapshotAndRefusesNewReservations()
	{
		MemorySnapshotStore store = new();
		store.Reserve("pending", 16);
		store.Reserve("done", 16);
		store.Commit(Snapshot("done", 16));

		store.Dispose();
		store.Commit(Snapshot("pending", 16));

		Assert.Empty(store.List());
		Assert.Equal(0L, store.TotalBytes);
		Assert.Throws<ObjectDisposedException>(() => store.Reserve("late", 1));
	}

	private static MemorySnapshot Snapshot(string name, int size)
	{
		MemoryImage image = MemoryImage.Read(size, new MemoryFileTools.Chunk(new byte[size], []),
			static (_, _) => throw new XunitException("One chunk only."));
		return new MemorySnapshot(name, 0x1000, 42, 1, 8, DateTimeOffset.UnixEpoch, image);
	}
}
