using CheatEngine.Mcp.Core.Jobs;
using CheatEngine.Mcp.Tools.Pointer;

namespace CheatEngine.Mcp.Tests.Tools.Pointer;

/// <summary>Protects the handoff from a newly registered job to a retained pointer map or scan.</summary>
public sealed class PointerSlotLifecycleTests
{
	[Fact]
	public void MapSlot_DeleteBeforeAttachment_RejectsTheUnstartedJob()
	{
		PointerMapSlot slot = new("map", 1);
		ManagedJob<int> job = Job("pointermap");

		Assert.Null(slot.BeginDelete());
		Assert.False(slot.TryAttach(job));
		Assert.Null(slot.Job);
	}

	[Fact]
	public void ScanSlot_DeleteBeforeAttachment_RejectsTheUnstartedJob()
	{
		PointerScanSlot slot = new("scan", "map", 0x1000, 8, 1);
		ManagedJob<int> job = Job("pointerscan");

		Assert.Null(slot.BeginDelete());
		Assert.False(slot.TryAttach(job));
		Assert.Null(slot.Job);
	}

	private static ManagedJob<int> Job(string kind)
	{
		JobStart start = new($"{kind}-test-1", "test", kind, TimeSpan.FromSeconds(60), 1,
			TimeProvider.System.GetUtcNow());
		return new ManagedJob<int>(start, TimeProvider.System);
	}
}
