using System.Diagnostics;
using System.Runtime.Versioning;
using System.Text.Json;

namespace CheatEngine.Mcp.Tests.LiveQualification;

[SupportedOSPlatform("windows")]
public sealed class LiveTargetManifestReaderTests
{
	[Fact]
	public async Task ReadAsync_WaitsForARealExclusiveFileLockThenReadsThePublishedManifest()
	{
		string directory = CreateDirectory();
		string path = Path.Combine(directory, "target-A.json");
		await File.WriteAllTextAsync(path, "{\"processId\":42}", TestContext.Current.CancellationToken);
		using CancellationTokenSource readerCancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
		Task<LiveTargetManifestReader.Result>? read = null;
		try
		{
			TaskCompletionSource<int> sharingRetry = new(TaskCreationOptions.RunContinuationsAsynchronously);
			using (Process target = Process.GetCurrentProcess())
			{
				using (FileStream held = new(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
				{
					read = LiveTargetManifestReader.ReadAsync(path, target, TimeSpan.FromSeconds(10),
						readerCancellation.Token, retry => sharingRetry.TrySetResult(retry));
					bool lockVerified = false;
					try
					{
						Assert.True(await sharingRetry.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken) >= 1);
						Assert.False(read.IsCompleted);
						lockVerified = true;
					}
					finally
					{
						if (!lockVerified)
						{
							await readerCancellation.CancelAsync();
						}
					}
				}

				LiveTargetManifestReader.Result result = await read;
				Assert.Equal(42, result.Node["processId"]!.GetValue<int>());
				Assert.True(result.Attempts >= 2);
				Assert.True(result.SharingRetries >= 1);
			}
		}
		finally
		{
			await readerCancellation.CancelAsync();
			if (read is not null)
			{
				try
				{
					await read;
				}
				catch (OperationCanceledException) when (readerCancellation.IsCancellationRequested)
				{
				}
			}
			Directory.Delete(directory, true);
		}
	}

	[Theory]
	[InlineData(0)]
	[InlineData(LiveTargetManifestReader.MaximumBytes + 1)]
	public async Task ReadAsync_RejectsPublishedFilesOutsideItsByteBounds(int length)
	{
		string directory = CreateDirectory();
		string path = Path.Combine(directory, "target-A.json");
		await File.WriteAllBytesAsync(path, new byte[length], TestContext.Current.CancellationToken);
		try
		{
			using Process target = Process.GetCurrentProcess();
			await Assert.ThrowsAsync<InvalidDataException>(() => LiveTargetManifestReader.ReadAsync(path, target,
				TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken));
		}
		finally
		{
			Directory.Delete(directory, true);
		}
	}

	[Fact]
	public async Task ReadAsync_RejectsMalformedPublishedJsonWithoutRetrying()
	{
		string directory = CreateDirectory();
		string path = Path.Combine(directory, "target-A.json");
		await File.WriteAllTextAsync(path, "{", TestContext.Current.CancellationToken);
		try
		{
			using Process target = Process.GetCurrentProcess();
			await Assert.ThrowsAnyAsync<JsonException>(() => LiveTargetManifestReader.ReadAsync(path, target, TimeSpan.FromSeconds(2),
				TestContext.Current.CancellationToken));
		}
		finally
		{
			Directory.Delete(directory, true);
		}
	}

	[Fact]
	public async Task ReadAsync_RejectsAnExitedOwnedTargetBeforeOpeningTheManifest()
	{
		string directory = CreateDirectory();
		string path = Path.Combine(directory, "target-A.json");
		try
		{
			ProcessStartInfo start = new(Path.Combine(Environment.SystemDirectory, "cmd.exe"))
			{
				CreateNoWindow = true,
				UseShellExecute = false
			};
			start.ArgumentList.Add("/c");
			start.ArgumentList.Add("exit 0");
			using Process target = Process.Start(start) ?? throw new InvalidOperationException("The short-lived owned target did not start.");
			await target.WaitForExitAsync(TestContext.Current.CancellationToken);

			await Assert.ThrowsAsync<InvalidOperationException>(() => LiveTargetManifestReader.ReadAsync(path, target,
				TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken));
		}
		finally
		{
			Directory.Delete(directory, true);
		}
	}

	[Fact]
	public async Task ReadAsync_UsesItsInternalDeadlineForAnAbsentManifest()
	{
		string directory = CreateDirectory();
		string path = Path.Combine(directory, "target-A.json");
		try
		{
			using Process target = Process.GetCurrentProcess();
			await Assert.ThrowsAsync<TimeoutException>(() => LiveTargetManifestReader.ReadAsync(path, target,
				TimeSpan.FromMilliseconds(50), TestContext.Current.CancellationToken));
		}
		finally
		{
			Directory.Delete(directory, true);
		}
	}

	[Fact]
	public async Task ReadAsync_HonorsCancellationWhileWaitingForManifestVisibility()
	{
		string directory = CreateDirectory();
		string path = Path.Combine(directory, "target-A.json");
		try
		{
			using Process target = Process.GetCurrentProcess();
			using CancellationTokenSource cancelled = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
			cancelled.CancelAfter(TimeSpan.FromMilliseconds(100));
			await Assert.ThrowsAnyAsync<OperationCanceledException>(() => LiveTargetManifestReader.ReadAsync(path, target,
				TimeSpan.FromSeconds(2), cancelled.Token));
		}
		finally
		{
			Directory.Delete(directory, true);
		}
	}

	private static string CreateDirectory()
	{
		string directory = Path.Combine(Path.GetTempPath(), "CheatEngine.Mcp.Tests", Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(directory);
		return directory;
	}
}
