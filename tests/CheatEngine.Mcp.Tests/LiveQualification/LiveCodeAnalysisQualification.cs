using System.Runtime.Versioning;
using System.Text.Json.Nodes;

using CheatEngine.Mcp.Core.Contract;

namespace CheatEngine.Mcp.Tests.LiveQualification;

/// <summary>Small, unexecuted instruction-buffer qualification for code comments and bounded code-search jobs.</summary>
[SupportedOSPlatform("windows")]
internal static class LiveCodeAnalysisQualification
{
	private const string Allocation = "live-code-analysis";
	private const string Comment = "owned code-analysis comment";

	internal static async Task VerifyAsync(LiveSandboxSession sandbox, LiveMcpInstanceClient instance)
	{
		bool allocated = false;
		bool jobMayBeRunning = false;
		Exception? failure = null;
		List<Exception> cleanup = [];
		string? address = null;
		string? jobId = null;
		try
		{
			JsonNode allocation = await CallAsync(instance, CheatEngineToolNames.MemoryAllocate,
				new Dictionary<string, object?> { ["name"] = Allocation, ["size"] = 16 });
			allocated = true;
			address = Text(allocation, "address");
			JsonNode written = await CallAsync(instance, CheatEngineToolNames.MemoryWrite, new Dictionary<string, object?>
			{
				["address"] = address,
				["valueType"] = "bytes",
				["value"] = "90 C3 90 C3",
				["verify"] = true
			});
			Assert.True(written["verified"]!.GetValue<bool>());

			JsonNode set = await CallAsync(instance, CheatEngineToolNames.CodeSetComment,
				new Dictionary<string, object?> { ["address"] = address, ["comment"] = Comment });
			Assert.Equal((address, Comment), (Text(set, "address"), Text(set, "comment")));
			JsonNode comments = await CallAsync(instance, CheatEngineToolNames.CodeGetComments,
				new Dictionary<string, object?> { ["addresses"] = new[] { address } });
			JsonNode comment = Assert.Single(comments["comments"]!.AsArray())!;
			Assert.Equal((address, Comment), (Text(comment, "address"), Text(comment, "comment")));
			sandbox.Record("all_tools_code_set_comment", set.DeepClone());
			sandbox.Record("all_tools_code_get_comments", comments.DeepClone());

			jobMayBeRunning = true;
			JsonNode start = await CallAsync(instance, CheatEngineToolNames.CodeStartSearch, new Dictionary<string, object?>
			{
				["address"] = address,
				["size"] = 4,
				["textContains"] = "nop",
				["lifetimeSeconds"] = 30,
				["maximumResults"] = 4
			});
			jobId = Text(start, "jobId");
			JsonNode page = await WaitForSearchAsync(instance, jobId);
			JsonArray events = page["items"]!.AsArray();
			Assert.Equal(2, events.Count);
			Assert.Equal(0L, page["dropped"]!.GetValue<long>());
			Assert.False(page["more"]!.GetValue<bool>());
			Assert.Equal([address, At(address, 2)], events.Select(item => Text(item, "address")));
			Assert.All(events, item =>
			{
				Assert.NotNull(item);
				Assert.Equal("search", Text(item, "kind"));
				Assert.Equal("nop", Text(item["instruction"], "opcode").Trim(), ignoreCase: true);
				Assert.Equal(Text(item, "address"), Text(item["instruction"], "address"));
				Assert.Equal(("90", 1), (Text(item["instruction"], "bytes"), item["instruction"]!["size"]!.GetValue<int>()));
			});
			sandbox.Record("all_tools_code_start_search", new
			{
				jobId,
				matches = 2,
				range = 4
			});
			sandbox.Record("all_tools_code_poll_job", page.DeepClone());
		}
		catch (Exception exception) { failure = exception; throw; }
		finally
		{
			if (jobId is not null)
			{
				await CleanupAsync(cleanup, async () =>
				{
					await StopSearchAsync(instance, jobId);
					jobMayBeRunning = false;
				});
			}
			else if (jobMayBeRunning)
			{
				cleanup.Add(new InvalidOperationException("Search start did not return an identity; the owned allocation is retained for host teardown."));
			}
			if (address is not null)
			{
				await CleanupAsync(cleanup, async () =>
				{
					JsonNode cleared = await CallAsync(instance, CheatEngineToolNames.CodeSetComment,
						new Dictionary<string, object?> { ["address"] = address, ["comment"] = "" });
					Assert.Equal(string.Empty, Text(cleared, "comment"));
					JsonNode comments = await CallAsync(instance, CheatEngineToolNames.CodeGetComments,
						new Dictionary<string, object?> { ["addresses"] = new[] { address } });
					JsonNode? clearedComment = Assert.Single(comments["comments"]!.AsArray());
					Assert.NotNull(clearedComment);
					Assert.True(clearedComment!["comment"] is null || clearedComment["comment"]!.GetValue<string>() == string.Empty);
				});
			}
			if (allocated && !jobMayBeRunning)
			{
				await CleanupAsync(cleanup, async () =>
				{
					JsonNode freed = await CallAsync(instance, CheatEngineToolNames.MemoryFree,
						new Dictionary<string, object?> { ["name"] = Allocation });
					Assert.True(freed["release"]!["isComplete"]!.GetValue<bool>());
					LiveMcpToolResult absent = await instance.CallToolRawAsync(CheatEngineToolNames.MemoryFree,
						new Dictionary<string, object?> { ["name"] = Allocation });
					Assert.True(absent.IsError);
					Assert.Equal("not_found", absent.Payload!["error"]!["kind"]!.GetValue<string>());
				});
			}
			sandbox.Record("live_code_analysis_cleanup", new
			{
				allocationRetainedForHostTeardown = allocated && jobMayBeRunning,
				failures = cleanup.Select(error => error.ToString()).ToArray()
			});
			LiveLifecycleQualification.ThrowWithCleanup(failure, cleanup);
		}
	}

	private static async Task StopSearchAsync(LiveMcpInstanceClient instance, string jobId)
	{
		for (int attempt = 0; attempt < 5; attempt++)
		{
			LiveMcpToolResult result = await instance.CallToolRawAsync(CheatEngineToolNames.RuntimeStopJob,
				new Dictionary<string, object?> { ["jobId"] = jobId });
			if (result.IsError)
			{
				JsonNode? error = result.Payload?["error"];
				Assert.Equal("partial_effect", Text(error, "kind"));
				Assert.True(error!["retryable"]!.GetValue<bool>());
				await Task.Delay(100, TestContext.Current.CancellationToken);
				continue;
			}
			Assert.Equal(jobId, Text(result.Payload, "jobId"));
			Assert.True(result.Payload!["released"]!.GetValue<bool>());
			LiveMcpToolResult absent = await instance.CallToolRawAsync(CheatEngineToolNames.CodePollJob,
				new Dictionary<string, object?> { ["jobId"] = jobId, ["afterSequence"] = 0L, ["limit"] = 1 });
			Assert.True(absent.IsError);
			Assert.Equal("not_found", Text(absent.Payload?["error"], "kind"));
			return;
		}
		throw new TimeoutException("Search stop remains pending after five bounded attempts; the owned allocation is retained for host teardown.");
	}

	private static async Task<JsonNode> WaitForSearchAsync(LiveMcpInstanceClient instance, string jobId)
	{
		for (int attempt = 0; attempt != 40; attempt++)
		{
			JsonNode page = await CallAsync(instance, CheatEngineToolNames.CodePollJob,
				new Dictionary<string, object?> { ["jobId"] = jobId, ["afterSequence"] = 0L, ["limit"] = 4 });
			string state = Text(page["job"], "state");
			if (state == "completed")
			{
				return page;
			}

			Assert.Equal("running", state);
			await Task.Delay(100, TestContext.Current.CancellationToken);
		}
		throw new TimeoutException("code search did not complete within 4 seconds.");
	}

	private static async Task CleanupAsync(List<Exception> failures, Func<Task> action)
	{
		try
		{
			await action();
		}
		catch (Exception exception) { failures.Add(exception); }
	}
	private static async Task<JsonNode> CallAsync(LiveMcpInstanceClient instance, string tool,
		IReadOnlyDictionary<string, object?>? arguments = null) => await instance.CallToolAsync(tool, arguments)
		?? throw new InvalidDataException($"Tool '{tool}' returned no payload.");
	private static string Text(JsonNode? node, string name) => node?[name]?.GetValue<string>()
		?? throw new InvalidDataException($"Expected {name}.");
	private static string At(string address, int offset)
	{
		return (Convert.ToUInt64(address, 16) + (uint) offset).ToString("X", System.Globalization.CultureInfo.InvariantCulture);
	}
}
