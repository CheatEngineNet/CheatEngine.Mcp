using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace CheatEngine.Mcp.Tests.LiveQualification;

/// <summary>Reads an atomically-published owned-target manifest through the short Windows sharing window.</summary>
internal static class LiveTargetManifestReader
{
	internal const int MaximumBytes = 64 * 1024;
	private static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(50);

	/// <summary>Reads one published manifest before the target exits, the deadline elapses, or cancellation is requested.</summary>
	internal static async Task<Result> ReadAsync(string path, Process target, TimeSpan timeout,
		CancellationToken cancellationToken, Action<int>? sharingRetryObserver = null)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(path);
		ArgumentNullException.ThrowIfNull(target);
		ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(timeout, TimeSpan.Zero);

		Stopwatch elapsed = Stopwatch.StartNew();
		Exception? lastTransient = null;
		int attempts = 0;
		int sharingRetries = 0;
		using CancellationTokenSource deadline = new(timeout);
		using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);
		try
		{
			while (true)
			{
				ThrowIfCancelledOrTimedOut();
				if (target.HasExited)
				{
					throw new InvalidOperationException("The owned target exited before its manifest became readable.");
				}

				attempts++;
				try
				{
					await using FileStream stream = new(path, FileMode.Open, FileAccess.Read,
						FileShare.ReadWrite | FileShare.Delete, 4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
					ThrowIfCancelledOrTimedOut();
					long length = stream.Length;
					if (length is < 1 or > MaximumBytes)
					{
						throw new InvalidDataException($"The owned target manifest must contain 1 through {MaximumBytes} bytes.");
					}

					byte[] bytes = GC.AllocateUninitializedArray<byte>(checked((int) length));
					await stream.ReadExactlyAsync(bytes, linked.Token);
					if (await stream.ReadAsync(new byte[1], linked.Token) != 0)
					{
						throw new InvalidDataException("The owned target manifest changed while it was being read.");
					}

					JsonNode node = JsonNode.Parse(bytes) ?? throw new InvalidDataException("The owned target manifest was empty.");
					if (node is not JsonObject)
					{
						throw new InvalidDataException("The owned target manifest must be a JSON object.");
					}

					ThrowIfCancelledOrTimedOut();
					return new Result(node, attempts, sharingRetries, checked((long) elapsed.Elapsed.TotalMilliseconds));
				}
				catch (FileNotFoundException exception)
				{
					lastTransient = exception;
				}
				catch (IOException exception) when (IsSharingViolation(exception))
				{
					lastTransient = exception;
					sharingRetries++;
					sharingRetryObserver?.Invoke(sharingRetries);
				}

				await Task.Delay(RetryDelay, linked.Token);
			}
		}
		catch (OperationCanceledException) when (deadline.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
		{
			throw TimedOut();
		}

		void ThrowIfCancelledOrTimedOut()
		{
			cancellationToken.ThrowIfCancellationRequested();
			if (deadline.IsCancellationRequested)
			{
				throw TimedOut();
			}
		}

		TimeoutException TimedOut() => new("The owned target manifest did not become readable before the deadline.", lastTransient);
	}

	private static bool IsSharingViolation(IOException exception)
	{
		int windowsError = exception.HResult & 0xffff;
		return windowsError is 32 or 33;
	}

	/// <summary>One bounded manifest-read receipt suitable for live-qualification evidence.</summary>
	internal sealed record Result(JsonNode Node, int Attempts, int SharingRetries, long ElapsedMs);
}
