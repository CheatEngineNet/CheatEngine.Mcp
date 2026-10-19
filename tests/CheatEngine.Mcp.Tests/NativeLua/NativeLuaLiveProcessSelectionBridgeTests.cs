using System.Text.Json.Nodes;

using CheatEngine.Mcp.Tests.LiveQualification;

namespace CheatEngine.Mcp.Tests.NativeLua;

/// <summary>Real-Lua coverage for the fixed, qualification-only process-selection bridge.</summary>
public sealed partial class NativeLuaToolRuntimeTests
{
	private const string SelectionBridgeStubs = """
		processId = 12
		processHandle = 34
		readCalls = 0
		receipt = ''
		getOpenedProcessID = function() return processId end
		getOpenedProcessHandle = function() return processHandle end
		readInteger = function(address, signed)
		  assert(address == 4096 and signed == true)
		  readCalls = readCalls + 1
		  return 20260926
		end
		io = { open = function()
		  return { write = function(_, text) receipt = receipt .. text end, close = function() end }
		end }
		""";

	[Fact]
	public async Task ProcessSelectionBridge_RenderAsync_BindsManifestFactsAndOwnedReceiptPath()
	{
		string directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(directory);
		string manifest = Path.Combine(directory, "target-A.json");
		try
		{
			await File.WriteAllTextAsync(manifest, "{\"processId\":77,\"address\":\"0x1000\"}", TestContext.Current.CancellationToken);

			RenderedProcessSelectionBridge bridge = await LiveProcessSelectionBridge.RenderAsync(manifest, TestContext.Current.CancellationToken);

			Assert.Equal((77, "0x1000", Path.Combine(directory, "target-A-process-selection.jsonl")),
				(bridge.ProcessId, bridge.Address, bridge.ReceiptPath));
			Assert.Contains("local mcp_selection_target = 77", bridge.Source, StringComparison.Ordinal);
			Assert.Contains("local mcp_selection_address = 4096", bridge.Source, StringComparison.Ordinal);
		}
		finally
		{
			Directory.Delete(directory, true);
		}
	}

	[Fact]
	public void ProcessSelectionBridge_ForwardsArgumentsOnceAndPreservesNilAndMultipleResults()
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs(SelectionBridgeStubs + """
			calls = 0
			openProcess = function(...)
			  calls = calls + 1
			  seen = table.pack(...)
			  return 'first', nil, 'third'
			end
			""" + LiveProcessSelectionBridge.Render(77, 4096, "C:\\owned\\selection.jsonl") + """
			result = table.pack(openProcess(77, nil, 'tail'))
			""");

		Assert.Equal(1L, ReadGlobal("calls"));
		Assert.Equal((3L, 77L, "tail"), ((long) EvaluateLua("seen.n")!, EvaluateLua("seen[1]"),
			EvaluateLua("seen[3]")));
		Assert.Null(EvaluateLua("seen[2]"));
		Assert.Equal((3L, "first", "third"), ((long) EvaluateLua("result.n")!, EvaluateLua("result[1]"),
			EvaluateLua("result[3]")));
		Assert.Null(EvaluateLua("result[2]"));
		Assert.Equal(1L, ReadGlobal("readCalls"));
	}

	[Fact]
	public void ProcessSelectionBridge_PreservesNoOriginalReturn()
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs(SelectionBridgeStubs + """
			openProcess = function(...) calls = (calls or 0) + 1 end
			""" + LiveProcessSelectionBridge.Render(77, 4096, "C:\\owned\\selection.jsonl") + """
			result = table.pack(openProcess(77))
			""");

		Assert.Equal((1L, 0L), (ReadGlobal("calls"), (long) EvaluateLua("result.n")!));
	}

	[Fact]
	public void ProcessSelectionBridge_PreservesRaisedErrorAndRecordsPostCallFacts()
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs(SelectionBridgeStubs + """
			openProcess = function(...)
			  processId = 77
			  processHandle = 99
			  error('original failure', 0)
			end
			""" + LiveProcessSelectionBridge.Render(77, 4096, "C:\\owned\\selection.jsonl") + """
			ok, errorText = pcall(function() openProcess(77) end)
			""");

		Assert.False((bool) ReadGlobal("ok")!);
		Assert.Equal("original failure", ReadGlobal("errorText"));
		Assert.Contains("\"afterHandle\":\"99\"", (string) ReadGlobal("receipt")!, StringComparison.Ordinal);
		Assert.Contains("\"ceValue\":\"20260926\"", (string) ReadGlobal("receipt")!, StringComparison.Ordinal);
	}

	[Fact]
	public void ProcessSelectionBridge_PreservesOpaqueOriginalErrorWhenDiagnosticStringificationFails()
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs(SelectionBridgeStubs + """
			errorObject = setmetatable({}, { __tostring = function() error('cannot stringify') end })
			getOpenedProcessHandle = function() error(errorObject, 0) end
			openProcess = function(...) error(errorObject, 0) end
			""" + LiveProcessSelectionBridge.Render(77, 4096, "C:\\owned\\selection.jsonl") + """
			ok, returnedError = pcall(openProcess, 77)
			""");

		Assert.Equal(false, ReadGlobal("ok"));
		Assert.Equal(true, EvaluateLua("returnedError == errorObject"));
		JsonNode row = JsonNode.Parse((string) ReadGlobal("receipt")!)!;
		Assert.Equal("<unprintable>", row["failure"]!.GetValue<string>());
	}

	[Fact]
	public void ProcessSelectionBridge_EscapesControlCharactersInJsonReceipt()
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs(SelectionBridgeStubs + """
			openProcess = function(...) return 'value\t\0\n"\\' end
			""" + LiveProcessSelectionBridge.Render(77, 4096, "C:\\owned\\selection.jsonl") + "openProcess(77)");

		JsonNode row = JsonNode.Parse((string) ReadGlobal("receipt")!)!;
		Assert.Equal("value\t\0\n\"\\", row["openReturnValue"]!.GetValue<string>());
		Assert.Equal("string", row["openReturnType"]!.GetValue<string>());
		Assert.Equal(1, row["resultCount"]!.GetValue<int>());
	}

	[Fact]
	public void ProcessSelectionBridge_CapsReceiptsAndLoggingFailureDoesNotChangeTheOriginalResult()
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs(SelectionBridgeStubs + """
			calls = 0
			openProcess = function(...) calls = calls + 1; return 'ok' end
			""" + LiveProcessSelectionBridge.Render(77, 4096, "C:\\owned\\selection.jsonl") + """
			for i = 1, 35 do value = openProcess(77) end
			""");

		Assert.Equal((35L, "ok"), (ReadGlobal("calls"), ReadGlobal("value")));
		Assert.Equal(32L, EvaluateLua("select(2, string.gsub(receipt, string.char(10), ''))"));

		InstallStubs("io = { open = function() error('receipt unavailable', 0) end }");
		InstallStubs(LiveProcessSelectionBridge.Render(77, 4096, "C:\\owned\\selection.jsonl") +
			"valueAfterLogFailure = openProcess(77)");
		Assert.Equal("ok", ReadGlobal("valueAfterLogFailure"));
	}
}
