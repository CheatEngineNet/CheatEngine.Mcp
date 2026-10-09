using System.Reflection;

using CheatEngine.Client;
using CheatEngine.Client.Inspection;
using CheatEngine.Client.Processes;
using CheatEngine.Client.Results;

using CheatEngine.Mcp.Core.Contract;
using CheatEngine.Mcp.Core.Features;
using CheatEngine.Mcp.Core.Files;
using CheatEngine.Mcp.Tests.Support;
using CheatEngine.Mcp.Tools.Processes;
using CheatEngine.Mcp.Tools.Scan;
using CheatEngine.SDK.Engine.Inspection;
using CheatEngine.SDK.Engine.Runtime;
using CheatEngine.SDK.Engine.Values;

using Microsoft.Extensions.Options;

namespace CheatEngine.Mcp.Tests.NativeLua;

/// <summary>Native Lua coverage for fixed runtime, process and scanner v2 bodies.</summary>
public sealed partial class NativeLuaToolRuntimeTests
{
	[Fact]
	public void V2FixedLuaBodies_NeverLoadCallerCode()
	{
		IEnumerable<string> bodies = new[] { typeof(ProcessTools), typeof(ScanScripts) }
			.SelectMany(static type => type.GetFields(BindingFlags.NonPublic | BindingFlags.Static))
			.Where(static field =>
				field.FieldType == typeof(string) && field.Name.EndsWith("Script", StringComparison.Ordinal))
			.Select(static field => (string) field.GetRawConstantValue()!);

		Assert.NotEmpty(bodies);
		foreach (string body in bodies)
		{
			LuaFixedScriptAssert.NeverLoadsCode(body);
		}
	}

	[Fact]
	public void ScanV2_MainScannerFirstReadResetAndStop_UseTypedBoundedResults()
	{
		using RuntimeScope scope = CreateScope();
		InstallMainScanner();
		ScanTools tools = new(CreateNativeDispatch(new McpFeatureOptions()), new TargetResources());

		ScanState started = tools.First(value: "25", cancellationToken: Token);
		Assert.Equal(("main", "ui", "Scanning"), (started.ScannerName, started.Mode, started.State));
		Assert.Equal(1L, ReadGlobal("firstCalls"));
		InstallStubs("complete(false)");
		ScanResultsResult page = tools.ListResults(maximumResults: 2, cancellationToken: Token);
		Assert.Equal(3UL, page.Count);
		Assert.Equal(2, page.Results.Length);
		Assert.Equal("ABCD", page.Results[0].Address);
		Assert.Equal("ResultsReady", tools.GetStatus(cancellationToken: Token).State);
		Assert.Equal("Created", tools.Reset(cancellationToken: Token).Status!.State);

		tools.First(value: "25", cancellationToken: Token);
		ScanStopResult stopped = tools.Stop(cancellationToken: Token);
		Assert.True(stopped.StopRequested);
		Assert.Equal("Scanning", stopped.Status!.State);
		Assert.Equal((1L, false), (ReadGlobal("stopCalls"), ReadGlobal("stopForce")));
	}

	[Fact]
	public void ScanV2_MainScannerTransitionGuard_RefusesTargetChangeWhileUiScanRuns()
	{
		using RuntimeScope scope = CreateScope();
		InstallMainScanner();
		InstallStubs("f.btnNewScan.Enabled=false");
		ToolDispatch dispatch = CreateNativeDispatch(new McpFeatureOptions());

		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() =>
			new MainScannerTransitionGuard(dispatch).EnsureCanChangeTarget(new TargetTransition(77, 88), Token));

		Assert.Equal(ToolErrorKind.Busy, exception.Error.Kind);
		Assert.Equal(ToolHostEffect.NotStarted, exception.Error.HostEffect);
		Assert.Equal(ScanTools.MainScannerBusyMessage, exception.Error.Message);
	}

	[Theory]
	[InlineData(true, "exact", "10", null, 0)]
	[InlineData(true, "greater", "10", null, 1)]
	[InlineData(true, "less", "10", null, 2)]
	[InlineData(true, "between", "10", "20", 3)]
	[InlineData(true, "unknown", null, null, 4)]
	[InlineData(false, "exact", "10", null, 0)]
	[InlineData(false, "greater", "10", null, 1)]
	[InlineData(false, "less", "10", null, 2)]
	[InlineData(false, "between", "10", "20", 3)]
	[InlineData(false, "increased", null, null, 4)]
	[InlineData(false, "increasedBy", "10", null, 5)]
	[InlineData(false, "decreased", null, null, 6)]
	[InlineData(false, "decreasedBy", "10", null, 7)]
	[InlineData(false, "changed", null, null, 8)]
	[InlineData(false, "unchanged", null, null, 9)]
	public void ScanV2_MainScannerComparison_UsesTheExpectedNativeControlIndex(bool first, string comparison,
		string? value, string? upperValue, long expectedIndex)
	{
		using RuntimeScope scope = CreateScope();
		InstallMainScanner();
		using ScanTools tools = new(CreateNativeDispatch(new McpFeatureOptions()), new TargetResources());
		if (!first)
		{
			InstallStubs("complete(false)");
		}

		ScanState status = first
			? tools.First(value: value, comparison: comparison, upperValue: upperValue, cancellationToken: Token)
			: tools.Next(value: value, comparison: comparison, upperValue: upperValue, cancellationToken: Token);

		Assert.Equal("Scanning", status.State);
		Assert.Equal(expectedIndex, ReadGlobal("requestedComparison"));
		Assert.Equal(first ? 1L : 0L, ReadGlobal("firstCalls"));
		Assert.Equal(first ? 0L : 1L, ReadGlobal("nextCalls"));
	}

	[Theory]
	[InlineData("byte", "25", 1, false, false)]
	[InlineData("int16", "25", 2, false, false)]
	[InlineData("int32", "25", 3, false, false)]
	[InlineData("int64", "25", 4, false, false)]
	[InlineData("float", "25.25", 5, false, false)]
	[InlineData("double", "25.25", 6, false, false)]
	[InlineData("string", "fox", 7, false, false)]
	[InlineData("wstring", "fox", 7, true, false)]
	[InlineData("bytes", "AB CD", 8, false, true)]
	public void ScanV2_MainScannerValueType_UsesTheExpectedNativeControlAndEncoding(string valueType, string value,
		long expectedIndex, bool expectedUnicode, bool expectedHexadecimal)
	{
		using RuntimeScope scope = CreateScope();
		InstallMainScanner();
		using ScanTools tools = new(CreateNativeDispatch(new McpFeatureOptions()), new TargetResources());

		ScanState status = tools.First(valueType: valueType, value: value, cancellationToken: Token);

		Assert.Equal(valueType, status.ValueType);
		Assert.Equal(expectedIndex, ReadGlobal("requestedType"));
		Assert.Equal(expectedUnicode, ReadGlobal("requestedUnicode"));
		Assert.Equal(expectedHexadecimal, ReadGlobal("requestedHex"));
	}

	[Fact]
	public void ScanV2_MainScannerUnknownBaseline_RequiresNarrowingBeforeResultsCanBeRead()
	{
		using RuntimeScope scope = CreateScope();
		InstallMainScanner();
		using ScanTools tools = new(CreateNativeDispatch(new McpFeatureOptions()), new TargetResources());

		tools.First(comparison: "unknown", cancellationToken: Token);
		InstallStubs("complete(true)");
		ScanStatusResult baseline = tools.GetStatus(cancellationToken: Token);
		CheatEngineToolException unreadable = Assert.Throws<CheatEngineToolException>(() =>
			tools.ListResults(cancellationToken: Token));
		ScanState narrowed = tools.Next(comparison: "unchanged", cancellationToken: Token);
		InstallStubs("complete(false)");

		Assert.Equal("BaselineReady", baseline.State);
		Assert.Equal((ToolErrorKind.InvalidState, ToolHostEffect.NotStarted),
			(unreadable.Error.Kind, unreadable.Error.HostEffect));
		Assert.Contains("scan_next", unreadable.Error.Hint, StringComparison.Ordinal);
		Assert.Equal(("Scanning", 9L), (narrowed.State, ReadGlobal("requestedComparison")));
		Assert.True(tools.GetStatus(cancellationToken: Token).ResultsReady);
	}

	[Fact]
	public void ScanV2_MainScannerHiddenReset_ShowsTheWindowBeforeInvokingNewScan()
	{
		using RuntimeScope scope = CreateScope();
		InstallMainScanner();
		InstallStubs("complete(false); f.Visible=false");
		using ScanTools tools = new(CreateNativeDispatch(new McpFeatureOptions()), new TargetResources());

		ScanReleaseResult reset = tools.Reset(cancellationToken: Token);

		Assert.Equal("Created", reset.Status!.State);
		Assert.Equal((1L, 1L), (ReadGlobal("showCalls"), ReadGlobal("resetCalls")));
	}

	[Fact]
	public void ProcessV2_FixedEffects_ReturnObservedTypedHostState()
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs("""
		             pid=77; created=0; opened=0; saved=0; pointerSize=8
		             createProcess=function(path, parameters, debug, breakAtEntry) created=created+1; createdPath=path; pid=88 end
		             openFileAsProcess=function(filename, is64Bit, startAddress) opened=opened+1; openedFilename=filename; pid=99 end
		             getOpenedProcessID=function() return pid end
		             getOpenedFileSize=function() return 4096 end
		             saveOpenedFile=function(filename) saved=saved+1; savedFilename=filename end
		             threadLists=0; threadListsDestroyed=0
		             createStringList=function()
		               threadLists=threadLists+1
		               return {Count=0, Strings={}, destroy=function() threadListsDestroyed=threadListsDestroyed+1 end}
		             end
		             getThreadlist=function(list)
		               list.Strings[0]='64'; list.Strings[1]='C8'; list.Strings[2]='12C'; list.Count=3
		             end
		             setPointerSize=function(value) pointerSize=value end
		             getPointerSize=function() return pointerSize end
		             """);
		string input = typeof(ProcessTools).Assembly.Location;
		string output = Path.Combine(Path.GetTempPath(), $"ce-mcp-save-{Guid.NewGuid():N}.bin");
		ProcessTools tools = new(CreateNativeProcessDispatch(99), new TargetResources(),
			new TargetTransitionGuards([]), CreateProcessFiles());

		try
		{
			ProcessCreateResult created = tools.Create(input, "--probe", true, true,
				Token);
			ProcessOpenFileResult opened = tools.OpenFile(input, false, "400000",
				Token);
			ProcessSaveFileResult saved = tools.SaveFile(output, Token);
			ProcessThreadListResult threads = tools.ListThreads(Token);
			ProcessPointerSizeResult pointerSize = tools.SetPointerSize(4, Token);

			Assert.Equal(88, created.ProcessId);
			Assert.Equal((99, 4096L, Path.GetFileName(input), false),
				(opened.ObservedProcessId, opened.ObservedFileSize, opened.InputFileName, opened.Requested64Bit));
			Assert.Equal((true, Path.GetFileName(output)), (saved.Saved, saved.FileName));
			Assert.Equal(new long[] { 100, 200, 300 }, threads.Threads);
			Assert.Equal(4, pointerSize.PointerSize);
			Assert.Equal(1L, ReadGlobal("created"));
			Assert.Equal(1L, ReadGlobal("opened"));
			Assert.Equal(1L, ReadGlobal("saved"));
			Assert.Equal((1L, 1L), (ReadGlobal("threadLists"), ReadGlobal("threadListsDestroyed")));
			Assert.Equal(input, ReadGlobal("createdPath"));
			Assert.Equal(input, ReadGlobal("openedFilename"));
			string savedTemporary = Assert.IsType<string>(ReadGlobal("savedFilename"));
			Assert.NotEqual(output, savedTemporary);
			Assert.StartsWith(".", Path.GetFileName(savedTemporary));
			Assert.EndsWith(".partial", Path.GetFileName(savedTemporary));
		}
		finally
		{
			File.Delete(output);
		}
	}

	[Fact]
	public void ProcessV2_ThreadList_DestroysItsCallerOwnedStringListWhenCollectionFails()
	{
		using RuntimeScope scope = CreateScope();
		InstallStubs("""
		             pid=77; lists=0; destroyed=0
		             getOpenedProcessID=function() return pid end
		             createStringList=function()
		               lists=lists+1
		               return {Count=0, Strings={}, destroy=function() destroyed=destroyed+1 end}
		             end
		             getThreadlist=function(list) error('thread enumeration failed', 0) end
		             """);
		ProcessTools tools = new(CreateNativeProcessDispatch(77), new TargetResources(),
			new TargetTransitionGuards([]), CreateProcessFiles());

		Assert.Throws<CheatEngineToolException>(() => tools.ListThreads(Token));

		Assert.Equal((1L, 1L), (ReadGlobal("lists"), ReadGlobal("destroyed")));
	}

	/// <summary>Creates a native Lua dispatch with a stable target snapshot for the thread-list preparation.</summary>
	private static ToolDispatch CreateNativeProcessDispatch(int processId)
	{
		IProcessClient processes = ClientTestDouble.Create<IProcessClient>((method, _) =>
		{
			Assert.Equal(nameof(IProcessClient.GetCurrentProcess), method.Name);
			return new ProcessSnapshot(new TargetProcessId(processId), "native-lua.exe", null,
				TargetBackend.LocalProcess, CheatEngineArchitecture.X64, PointerSize.Bit64, 8, null, 1);
		});
		IOptions<McpExecutionOptions> options = Options.Create(new McpExecutionOptions());
		ICheatEngineClient client = ClientTestDouble.Client(
			(nameof(ICheatEngineClient.Lua), CreateJsonLuaClient().Lua),
			(nameof(ICheatEngineClient.Processes), processes));
		return new ToolDispatch(client, new McpFeatureGate(Options.Create(new McpFeatureOptions())), options,
			new DispatchStatistics(options), TimeProvider.System, new RecordingLogger<ToolDispatch>(),
			new PluginFixedLuaExecutor(client));
	}

	private static McpFilePaths CreateProcessFiles()
	{
		string root = Path.GetTempPath();
		return new McpFilePaths(new McpFileOptions { AllowedRoots = [root] },
			Path.Combine(root, "ce-mcp-native-registry"), Path.Combine(root, "ce-mcp-native-data"));
	}
}
