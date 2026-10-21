using System.Runtime.Versioning;
using System.Text.Json.Nodes;

namespace CheatEngine.Mcp.Tests.LiveQualification;

[SupportedOSPlatform("windows")]
public sealed class LiveDebuggerEventOracleTests
{
	private static readonly LiveDebuggerExecutionQualification.Manifest Owned = new(42, 7, 0x1000, 0x1009, 0x1017, 0x1037, "1108", "1104");

	[Fact]
	public void BreakpointListPresence_UsesExactAddressRegardlessOfOwner()
	{
		JsonNode page = JsonNode.Parse("""{"breakpoints":[{"address":"1009","owned":false},{"address":"0x1017","owned":true}]}""")!;

		Assert.True(LiveDebuggerExecutionQualification.HasBreakpointAt(page, 0x1009));
		Assert.True(LiveDebuggerExecutionQualification.HasBreakpointAt(page, 0x1017));
		Assert.False(LiveDebuggerExecutionQualification.HasBreakpointAt(page, 0x100A));
	}

	[Fact]
	public void CapturePage_AcceptsTheExactOwnedRepeatedGroup()
	{
		LiveDebuggerExecutionQualification.AssertCapturePage(CapturePage(), Owned, "capture-owned");
	}

	[Theory]
	[InlineData("foreign thread")]
	[InlineData("wrong instruction")]
	[InlineData("wrong register")]
	[InlineData("heuristic context")]
	[InlineData("wrong first context")]
	[InlineData("wrong last context")]
	[InlineData("only one hit")]
	[InlineData("progress mismatch")]
	[InlineData("dropped hits")]
	[InlineData("more results")]
	[InlineData("foreign process")]
	[InlineData("wrong state")]
	public void CapturePage_RejectsIncompleteOrForeignEvidence(string mutation)
	{
		JsonNode page = CapturePage();
		JsonNode hit = page["hits"]![0]!;
		switch (mutation)
		{
			case "foreign thread":
				hit["context"]!["threadId"] = 8L;
				break;
			case "wrong instruction":
				hit["context"]!["instructionAddress"] = "1010";
				break;
			case "wrong register":
				hit["context"]!["registers"]!["RIP"] = "1010";
				break;
			case "heuristic context":
				hit["context"]!["isHeuristic"] = true;
				break;
			case "wrong first context":
				hit["firstContext"]!["threadId"] = 8L;
				break;
			case "wrong last context":
				hit["lastContext"]!["registers"]!["RIP"] = "1010";
				break;
			case "only one hit":
				hit["hitCount"] = 1L;
				break;
			case "progress mismatch":
				page["job"]!["progressDone"] = 3L;
				break;
			case "dropped hits":
				page["dropped"] = 1L;
				break;
			case "more results":
				page["more"] = true;
				break;
			case "foreign process":
				page["job"]!["processId"] = 99;
				break;
			case "wrong state":
				page["job"]!["state"] = "failed";
				break;
			default:
				throw new ArgumentOutOfRangeException(nameof(mutation));
		}
		Assert.IsAssignableFrom<Xunit.Sdk.XunitException>(Record.Exception(() =>
			LiveDebuggerExecutionQualification.AssertCapturePage(page, Owned, "capture-owned")));
	}

	[Theory]
	[InlineData(1)]
	[InlineData(2)]
	[InlineData(3)]
	public void TracePage_AcceptsTheExactOwnedInstructionSequence(int count)
	{
		LiveDebuggerExecutionQualification.AssertTracePage(TracePage(count), Owned, "trace-owned", count);
	}

	[Theory]
	[InlineData("wrong second instruction")]
	[InlineData("foreign thread")]
	[InlineData("changed stack")]
	[InlineData("wrong total")]
	[InlineData("missing step")]
	[InlineData("not completed")]
	[InlineData("cleanup failure")]
	[InlineData("wrong cursor")]
	public void TracePage_RejectsIncompleteOrForeignEvidence(string mutation)
	{
		JsonNode page = TracePage(3);
		switch (mutation)
		{
			case "wrong second instruction":
				page["steps"]![1]!["ip"] = "1009";
				break;
			case "foreign thread":
				page["steps"]![2]!["threadId"] = 8L;
				break;
			case "changed stack":
				page["steps"]![2]!["stackPointer"] = "7008";
				page["steps"]![2]!["registers"]!["RSP"] = "7008";
				break;
			case "wrong total":
				page["job"]!["total"] = 2L;
				break;
			case "missing step":
				page["steps"]!.AsArray().RemoveAt(2);
				break;
			case "not completed":
				page["job"]!["state"] = "running";
				break;
			case "cleanup failure":
				page["job"]!["cleanupError"] = "hook remains";
				break;
			case "wrong cursor":
				page["nextAfterSequence"] = 2L;
				break;
			default:
				throw new ArgumentOutOfRangeException(nameof(mutation));
		}
		Assert.IsAssignableFrom<Xunit.Sdk.XunitException>(Record.Exception(() =>
			LiveDebuggerExecutionQualification.AssertTracePage(page, Owned, "trace-owned", 3)));
	}

	private static JsonNode CapturePage()
	{
		JsonNode page = Page("capture-owned", "debugcapture", "running", 1, 2);
		JsonNode context = Context("1009", 0);
		page["hits"] = new JsonArray(new JsonObject
		{
			["context"] = context,
			["firstContext"] = context.DeepClone(),
			["lastContext"] = context.DeepClone(),
			["hitCount"] = 2L
		});
		return page;
	}

	private static JsonNode TracePage(int count)
	{
		JsonNode page = Page("trace-owned", "debugtrace", "completed", count, count);
		page["job"]!["progressTotal"] = (long) count;
		page["steps"] = count switch
		{
			1 => new JsonArray(Context("1009")),
			2 => new JsonArray(Context("1009"), Context("1010")),
			3 => new JsonArray(Context("1009"), Context("1010"), Context("1017")),
			_ => throw new ArgumentOutOfRangeException(nameof(count))
		};
		return page;
	}

	private static JsonNode Page(string id, string kind, string state, int count, int progress) => JsonNode.Parse($$"""
		{"job":{"jobId":"{{id}}","kind":"{{kind}}","state":"{{state}}","processId":42,
		"buffered":{{count}},"total":{{count}},"dropped":0,"progressDone":{{progress}},"requiresManualRecovery":false},
		"firstSequence":1,"nextAfterSequence":{{count}},"dropped":0,"more":false}
		""")!;

	private static JsonNode Context(string ip, int threadId = 7) => JsonNode.Parse($$"""
		{"ip":"{{ip}}","threadId":{{threadId}},"instructionAddress":"{{ip}}","isHeuristic":false,
		"registers":{"RIP":"{{ip}}","RSP":"7000"},"stackPointer":"7000"}
		""")!;
}
