using CheatEngine.Mcp.Tests.Support;

using Microsoft.Extensions.Logging.Abstractions;

namespace CheatEngine.Mcp.Tests.Hosting;

/// <summary>The pool against real loopback backends: one handshake per record, bounded size and lifetime.</summary>
public sealed class BackendConnectionPoolTests : IAsyncDisposable
{
	private readonly string _directory =
		Path.Combine(Path.GetTempPath(), $"CheatEngine.Mcp.PoolTests-{Guid.NewGuid():N}");

	private readonly ManualTime _time = new();

	public ValueTask DisposeAsync()
	{
		if (Directory.Exists(_directory))
		{
			Directory.Delete(_directory, true);
		}

		return ValueTask.CompletedTask;
	}

	[Fact]
	public async Task RentAsync_SameRecordTwice_SharesOneHandshake()
	{
		await using FakeBackend backend = await FakeBackend.StartAsync(new InstanceRegistry(_directory), "shared");
		await using BackendConnectionPool pool = CreatePool();

		await RentAndReturnAsync(pool, backend.Descriptor);
		await RentAndReturnAsync(pool, backend.Descriptor);

		Assert.Equal(1, backend.InitializeCount);
		Assert.Equal(1, pool.Count);
	}

	[Fact]
	public async Task RentAsync_IdleBeyondLifetime_ConnectsAgain()
	{
		await using FakeBackend backend = await FakeBackend.StartAsync(new InstanceRegistry(_directory), "idle");
		await using BackendConnectionPool pool = CreatePool();
		await RentAndReturnAsync(pool, backend.Descriptor);

		_time.Advance(TimeSpan.FromMinutes(4));
		await RentAndReturnAsync(pool, backend.Descriptor);
		Assert.Equal(1, backend.InitializeCount);

		_time.Advance(TimeSpan.FromMinutes(5));
		await RentAndReturnAsync(pool, backend.Descriptor);
		Assert.Equal(2, backend.InitializeCount);
		Assert.Equal(1, pool.Count);
	}

	[Fact]
	public async Task RentAsync_BeyondCapacity_EvictsTheLeastRecentlyUsed()
	{
		InstanceRegistry registry = new(_directory);
		await using FakeBackend first = await FakeBackend.StartAsync(registry, "first");
		await using FakeBackend second = await FakeBackend.StartAsync(registry, "second");
		await using FakeBackend third = await FakeBackend.StartAsync(registry, "third");
		await using BackendConnectionPool pool = CreatePool(2);

		await RentAndReturnAsync(pool, first.Descriptor);
		_time.Advance(TimeSpan.FromSeconds(1));
		await RentAndReturnAsync(pool, second.Descriptor);
		_time.Advance(TimeSpan.FromSeconds(1));
		await RentAndReturnAsync(pool, first.Descriptor);
		_time.Advance(TimeSpan.FromSeconds(1));
		await RentAndReturnAsync(pool, third.Descriptor);
		await RentAndReturnAsync(pool, first.Descriptor);
		await RentAndReturnAsync(pool, second.Descriptor);

		Assert.Equal(2, pool.Count);
		Assert.Equal(1, first.InitializeCount);
		Assert.Equal(2, second.InitializeCount);
		Assert.Equal(1, third.InitializeCount);
	}

	[Fact]
	public async Task RentAsync_ChangedRecord_ReplacesTheOldClient()
	{
		await using FakeBackend backend = await FakeBackend.StartAsync(new InstanceRegistry(_directory), "changed");
		await using BackendConnectionPool pool = CreatePool();
		await RentAndReturnAsync(pool, backend.Descriptor);

		backend.RepublishWithNewToken();
		await RentAndReturnAsync(pool, backend.Descriptor);

		Assert.Equal(2, backend.InitializeCount);
		Assert.Equal(1, pool.Count);
	}

	[Fact]
	public async Task RentAsync_UnreachableBackend_FailsAndPoolsNothing()
	{
		await using FakeBackend backend = await FakeBackend.StartAsync(new InstanceRegistry(_directory), "gone");
		await backend.StopListeningAsync();
		await using BackendConnectionPool pool = CreatePool();

		InstanceUnavailableException failure = await Assert.ThrowsAsync<InstanceUnavailableException>(() =>
			pool.RentAsync(backend.Descriptor, TestContext.Current.CancellationToken));

		Assert.DoesNotContain(backend.Descriptor.AccessToken, failure.Message, StringComparison.Ordinal);
		Assert.Equal(0, pool.Count);
	}

	[Fact]
	public void Key_Record_BindsTheTokenOnlyAsItsHash()
	{
		InstanceDescriptor record = new("ce-1-00000000000000000000000000000001", "key",
			Guid.Parse("00000000-0000-0000-0000-000000000001"), 1, 1, "http://127.0.0.1:40001/",
			new string('a', 64), "2.0.0");

		BackendConnectionKey key = BackendConnectionKey.From(record);

		Assert.Equal("FFE054FE7AE0CB6DC65C3AF9B61D5209F439851DB43D0BA5997337DF154668EB", key.TokenHash);
		Assert.DoesNotContain(record.AccessToken, key.ToString(), StringComparison.Ordinal);
		Assert.NotEqual(key, BackendConnectionKey.From(record with
		{
			AccessToken = new string('b', 64)
		}));
		Assert.NotEqual(key, BackendConnectionKey.From(record with
		{
			Endpoint = "http://127.0.0.1:40002/"
		}));
	}

	private BackendConnectionPool CreatePool(int capacity = BackendConnectionPool.DefaultCapacity)
	{
		return new BackendConnectionPool(_time, NullLoggerFactory.Instance, capacity,
			BackendConnectionPool.DefaultIdleLifetime);
	}

	private static async Task RentAndReturnAsync(BackendConnectionPool pool, InstanceDescriptor record)
	{
		using BackendConnectionPool.BackendLease lease =
			await pool.RentAsync(record, TestContext.Current.CancellationToken);
		Assert.NotNull(lease.Client);
	}

	/// <summary>A clock that moves only when a test advances it.</summary>
	private sealed class ManualTime : TimeProvider
	{
		private long _ticks = TimeSpan.TicksPerDay;

		public override long TimestampFrequency => TimeSpan.TicksPerSecond;

		public override long GetTimestamp()
		{
			return Interlocked.Read(ref _ticks);
		}

		internal void Advance(TimeSpan elapsed)
		{
			Interlocked.Add(ref _ticks, elapsed.Ticks);
		}
	}
}
