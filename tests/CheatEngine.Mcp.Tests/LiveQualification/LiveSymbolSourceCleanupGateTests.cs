namespace CheatEngine.Mcp.Tests.LiveQualification;

public sealed class LiveSymbolSourceCleanupGateTests
{
	[Fact]
	public async Task EnsureCleanedAsync_ContainmentFailsOnce_RetriesBeforeAnyOuterRelease()
	{
		LiveSymbolSourceCleanupGate gate = new();
		List<string> events = [];
		int containmentCalls = 0;

		async Task CleanupThenReleaseAsync()
		{
			await gate.EnsureCleanedAsync(
				() =>
				{
					events.Add("stop");
					return Task.CompletedTask;
				},
				() =>
				{
					events.Add("containment");
					if (++containmentCalls == 1)
					{
						throw new InvalidOperationException("Simulated containment cleanup failure.");
					}
				});
			events.Add("release");
		}

		await Assert.ThrowsAsync<InvalidOperationException>(CleanupThenReleaseAsync);
		Assert.Equal(["stop", "containment"], events);

		await CleanupThenReleaseAsync();
		Assert.Equal(["stop", "containment", "stop", "containment", "release"], events);
		Assert.Equal(2, containmentCalls);

		await gate.EnsureCleanedAsync(
			() => throw new InvalidOperationException("Verified cleanup must not stop processes again."),
			() => throw new InvalidOperationException("Verified containment must not be removed again."));
		Assert.Equal(["stop", "containment", "stop", "containment", "release"], events);
	}
}
