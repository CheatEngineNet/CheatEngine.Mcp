using System.ComponentModel;
using System.Reflection;
using System.Text.Json;

using CheatEngine.Client;
using CheatEngine.Client.Assembly;
using CheatEngine.Client.Extensions.DependencyInjection;
using CheatEngine.Client.Inspection;
using CheatEngine.Client.Processes;
using CheatEngine.Client.Results;
using CheatEngine.Mcp.Core.Contract;
using CheatEngine.Mcp.Core.Features;
using CheatEngine.Mcp.Core.Files;
using CheatEngine.Mcp.Core.Jobs;
using CheatEngine.Mcp.Tests.Core;
using CheatEngine.Mcp.Tests.Support;
using CheatEngine.Mcp.Tools.Asm;
using CheatEngine.Mcp.Tools.Code;
using CheatEngine.Mcp.Tools.Table;
using CheatEngine.SDK.Engine.Inspection;
using CheatEngine.SDK.Engine.Runtime;
using CheatEngine.SDK.Engine.Values;

using Microsoft.Extensions.Options;

namespace CheatEngine.Mcp.Tests.Tools;

/// <summary>Focused contract tests for the v2 Code, Asm and Table containers.</summary>
public sealed class CodeAsmTableV2Tests : IDisposable
{
	private readonly McpFilePathsTests.Scratch _scratch = new();
	private static CancellationToken Token => TestContext.Current.CancellationToken;

	public void Dispose()
	{
		_scratch.Dispose();
	}

	[Fact]
	public void DisassemblyColumns_ClientColumnsMisordered_RetainsTypedBytesAndLength()
	{
		StateTestHarness harness = new();
		harness.Answer<CodeLuaDisassemblyColumns>(_ => new CodeLuaDisassemblyColumns("game.exe+1000", "nop", "annotation"));
		AssemblyInstructionSnapshot typed = new(new Address(0x401000), 1, "annotation", "90", "nop", [0x90]);

		AssemblyInstructionSnapshot corrected = harness.Dispatch.Run("code_decode",
			token => CodeTools.CorrectColumns(harness.Dispatch, typed, "code_decode", token), Token);

		Assert.Equal((typed.Address, typed.Length), (corrected.Address, corrected.Length));
		Assert.Equal(typed.Bytes, corrected.Bytes);
		Assert.Equal(("game.exe+1000", "nop", "annotation"),
			(corrected.AddressText, corrected.Opcode, corrected.Extra));
		Assert.Equal(1, harness.Dispatches);
	}

	[Fact]
	public void DisassembleBytes_UsesFixedBoundedLuaScript()
	{
		StateTestHarness harness = new();
		string? source = null;
		harness.Answer<CodeLuaByteDisassembly>(value =>
		{
			source = value;
			return new CodeLuaByteDisassembly("401000", "nop");
		});

		CodeByteDisassembly result =
			new CodeTools(harness.Dispatch, harness.Jobs).DisassembleBytes("90", "401000", Token);

		Assert.Equal(new CodeByteDisassembly("401000", "nop"), result);
		Assert.Contains("local text = disassembleBytes(a[1], origin)", source, StringComparison.Ordinal);
		Assert.Contains("origin could not be resolved", source, StringComparison.Ordinal);
		Assert.DoesNotContain("load(", source, StringComparison.Ordinal);
		Assert.Equal(1, harness.Dispatches);
	}

	[Fact]
	public void DisassembleBytes_SpacedMixedCaseText_PassesContiguousUppercaseDigitsToCheatEngine()
	{
		StateTestHarness harness = new();
		string? source = null;
		harness.Answer<CodeLuaByteDisassembly>(value =>
		{
			source = value;
			return new CodeLuaByteDisassembly("0", "mov rax,[rip+00000000]");
		});

		new CodeTools(harness.Dispatch, harness.Jobs).DisassembleBytes(" 48 8b\t05\r\n00 00 0000 ", null, Token);

		Assert.Contains("\"488B0500000000\"", source, StringComparison.Ordinal);
		Assert.DoesNotContain("48 8b", source, StringComparison.Ordinal);
	}

	[Fact]
	public void DissectorPages_SortAddressesNumericallyBeforeFormattingThem()
	{
		Assert.Contains("math.ult(left.from, right.from)", CodeScripts.FindReferences, StringComparison.Ordinal);
		Assert.Contains("math.ult(left.address, right.address)", CodeScripts.FindStrings,
			StringComparison.Ordinal);
		Assert.All([CodeScripts.FindReferences, CodeScripts.FindStrings], static script =>
			Assert.DoesNotContain("fromAddress <", script, StringComparison.Ordinal));
	}

	[Fact]
	public void CodeJobEventAddress_DescribesTheDissectedStartAddress()
	{
		string description = typeof(CodeJobEvent).GetProperty(nameof(CodeJobEvent.Address))!
			.GetCustomAttribute<System.ComponentModel.DescriptionAttribute>()!.Description;

		Assert.Contains("first dissected address for a dissect event", description, StringComparison.Ordinal);
		Assert.DoesNotContain("omitted", description, StringComparison.Ordinal);
	}

	[Fact]
	public void DissectorUnavailable_DeclaresAContractHostRefusal()
	{
		Assert.Contains("mcp.err('host_refused', 'Cheat Engine has no code dissector.', 'not_started')",
			CodeScripts.Dissect, StringComparison.Ordinal);
		Assert.DoesNotContain("capability_unavailable", CodeScripts.Dissect, StringComparison.Ordinal);
		Assert.All([CodeScripts.FindReferences, CodeScripts.FindStrings, CodeScripts.ListFunctions], script =>
			Assert.Contains("Cheat Engine has no code dissector.", script, StringComparison.Ordinal));
	}

	[Fact]
	public void DissectorEnumerations_StayBoundedAndCooperateWithTheDispatchBudget()
	{
		Assert.Contains("count > a[4]", CodeScripts.FindReferences, StringComparison.Ordinal);
		Assert.Contains("scanned > a[4]", CodeScripts.FindStrings, StringComparison.Ordinal);
		Assert.All([CodeScripts.FindReferences, CodeScripts.FindStrings], script =>
		{
			Assert.Contains("mcp.expired()", script, StringComparison.Ordinal);
			LuaFixedScriptAssert.NeverLoadsCode(script);
		});
	}

	[Theory]
	[InlineData("9")]
	[InlineData("GG")]
	[InlineData("   ")]
	public void DisassembleBytes_MalformedHexadecimalText_RefusesBeforeLua(string bytes)
	{
		StateTestHarness harness = new();

		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() =>
			new CodeTools(harness.Dispatch, harness.Jobs).DisassembleBytes(bytes, cancellationToken: Token));

		Assert.Equal((ToolErrorKind.InvalidArgument, ToolHostEffect.NotStarted),
			(exception.Error.Kind, exception.Error.HostEffect));
		Assert.Empty(harness.LuaCalls);
	}

	[Fact]
	public async Task StartSearch_StopWhileNativeCallRuns_ReportsPartialThenReleasesTheJob()
	{
		using SearchHarness harness = new();
		CodeTools tools = new(harness.Dispatch, harness.Jobs);
		CodeJobStart started = tools.StartSearch("401000", 2, "nop", cancellationToken: Token);
		ManagedJob<CodeJobEvent> job = harness.Jobs.Get<ManagedJob<CodeJobEvent>>(started.JobId, CodeTools.JobKind);
		Assert.True(harness.EnteredDisassembly.Wait(TimeSpan.FromSeconds(10), Token));

		try
		{
			CheatEngineToolException stopping = Assert.Throws<CheatEngineToolException>(() =>
				harness.Jobs.Stop(started.JobId, Token));
			Assert.Equal((ToolErrorKind.PartialEffect, true), (stopping.Error.Kind, stopping.Error.Retryable));
		}
		finally
		{
			harness.ReleaseDisassembly.Set();
		}

		await job.Completion.WaitAsync(TimeSpan.FromSeconds(10), Token);
		JobStopResult stopped = harness.Jobs.Stop(started.JobId, Token);
		Assert.True(stopped.AlreadyReleased);
		Assert.Equal(JobState.Stopped, job.State);
		Assert.Empty(harness.Resources.List());
	}

	[Fact]
	public async Task StartSearch_ZeroLengthHostInstruction_FailsInsteadOfLooping()
	{
		using SearchHarness harness = new(0);
		CodeTools tools = new(harness.Dispatch, harness.Jobs);
		CodeJobStart started = tools.StartSearch("401000", 2, "nop", cancellationToken: Token);
		ManagedJob<CodeJobEvent> job = harness.Jobs.Get<ManagedJob<CodeJobEvent>>(started.JobId, CodeTools.JobKind);
		Assert.True(harness.EnteredDisassembly.Wait(TimeSpan.FromSeconds(10), Token));
		harness.ReleaseDisassembly.Set();

		await job.Completion.WaitAsync(TimeSpan.FromSeconds(10), Token);

		Assert.Equal(JobState.Failed, job.State);
	}

	[Fact]
	public void GenerateInjection_ProducesReviewedRollbackScript()
	{
		AsmGeneratedScript result = AsmTools.GenerateInjection("game.exe", "48 89 ?? 24", "48 89 5C 24 08", "mcpHook");

		Assert.Equal(("48895C2408", "mcpHook"), (result.ExpectedBytes, result.SymbolName));
		Assert.Contains("aobscanmodule(mcpHook,game.exe,48 89 ?? 24)", result.Script, StringComparison.Ordinal);
		Assert.Contains("assert(mcpHook,48 89 5C 24 08)", result.Script, StringComparison.Ordinal);
		Assert.Contains("[DISABLE]", result.Script, StringComparison.Ordinal);
		Assert.Contains("mcpHook:\ndb 48 89 5C 24 08", result.Script, StringComparison.Ordinal);
		Assert.Contains("unregistersymbol(mcpHook)", result.Script, StringComparison.Ordinal);
		Assert.Contains("dealloc(newmem)", result.Script, StringComparison.Ordinal);
		Assert.DoesNotContain("_aob", result.Script, StringComparison.Ordinal);
	}

	[Fact]
	public void GenerateInjection_WithOffset_RegistersTheSymbolAtTheInjectionPoint()
	{
		const string signature = "E8 ?? ?? ?? ?? 48 8B 05 ?? ?? ?? ?? 90 90 90 90 48 89 5C 24 08";

		AsmGeneratedScript result =
			AsmTools.GenerateInjection("game.exe", signature, "48 89 5C 24 08 90", "mcpHook", 16);

		Assert.Equal(("48895C240890", "mcpHook"), (result.ExpectedBytes, result.SymbolName));
		Assert.StartsWith($"[ENABLE]\naobscanmodule(mcpHook_aob,game.exe,{signature})\n" +
						  "assert(mcpHook_aob+10,48 89 5C 24 08 90)\nalloc(newmem,2048,mcpHook_aob)\n",
			result.Script, StringComparison.Ordinal);
		Assert.Contains("label(mcpHook)\nregistersymbol(mcpHook)\n", result.Script, StringComparison.Ordinal);
		Assert.Contains("code:\ndb 48 89 5C 24 08 90\njmp return", result.Script, StringComparison.Ordinal);
		Assert.Contains("mcpHook_aob+10:\nmcpHook:\njmp newmem\nnop\nreturn:", result.Script, StringComparison.Ordinal);
		Assert.EndsWith("[DISABLE]\nmcpHook:\ndb 48 89 5C 24 08 90\nunregistersymbol(mcpHook)\ndealloc(newmem)",
			result.Script, StringComparison.Ordinal);
	}

	[Theory]
	[InlineData(-1)]
	[InlineData(4)]
	public void GenerateInjection_OffsetOutsideTheSignature_RefusesWithInvalidArgument(int offset)
	{
		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() =>
			AsmTools.GenerateInjection("game.exe", "48 89 ?? 24", "48 89 5C 24 08", "mcpHook", offset));

		Assert.Equal((ToolErrorKind.InvalidArgument, "offset"),
			(exception.Error.Kind, exception.Error.Details!.Value.GetProperty("parameter").GetString()));
	}

	[Fact]
	public void Assemble_UndefinedPreference_RefusesBeforeDispatch()
	{
		StateTestHarness harness = new();

		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() =>
			new AsmTools(harness.Dispatch, harness.Resources).Assemble("401000", ["nop"],
				(InstructionEncodingPreference) 4, cancellationToken: Token));

		Assert.Equal((ToolErrorKind.InvalidArgument, "preference"),
			(exception.Error.Kind, exception.Error.Details!.Value.GetProperty("parameter").GetString()));
		Assert.Equal(0, harness.Dispatches);
	}

	[Fact]
	public void AssemblePreference_DocumentsTheClientIntegers()
	{
		string description = Method(nameof(AsmTools.Assemble)).GetParameters()
			.Single(static parameter => parameter.Name == "preference")
			.GetCustomAttribute<DescriptionAttribute>()!.Description;

		int[] values =
		[
			(int) InstructionEncodingPreference.None, (int) InstructionEncodingPreference.Short,
			(int) InstructionEncodingPreference.Long, (int) InstructionEncodingPreference.Far
		];

		Assert.Equal([0, 1, 2, 3], values);
		Assert.All(["0 none", "1 short", "2 long", "3 far"],
			text => Assert.Contains(text, description, StringComparison.Ordinal));
	}

	[Fact]
	public void Check_AcceptedEnable_AlsoChecksTheDisableSection()
	{
		StateTestHarness harness = new();
		string? source = null;
		harness.Answer<AsmLuaCheck>(value =>
		{
			source = value;
			return new AsmLuaCheck(false, "Error in line 4 (bad) :This instruction can't be compiled", false);
		});
		AsmTools tools = new(harness.Dispatch, harness.Resources,
			CheckingAutoAssembler(new AutoAssemblerCheckResult(true, null, false)));

		AsmCheckResult result = tools.Check("[ENABLE]\nnop\n[DISABLE]\nbad", cancellationToken: Token);

		Assert.Equal(new AsmCheckResult(false, "Error in line 4 (bad) :This instruction can't be compiled", false,
			AsmScriptSection.Disable), result);
		Assert.Contains("pcall(autoAssembleCheck, a[1], false, false)", source, StringComparison.Ordinal);
		Assert.Contains($"[2] = {AsmTools.MaximumHostMessageBytes}", source, StringComparison.Ordinal);
		LuaFixedScriptAssert.NeverLoadsCode(AsmScripts.CheckDisable);
	}

	[Fact]
	public void Check_RejectedEnable_ReportsTheEnableSectionWithoutCheckingDisable()
	{
		StateTestHarness harness = new();
		AsmTools tools = new(harness.Dispatch, harness.Resources,
			CheckingAutoAssembler(new AutoAssemblerCheckResult(false, "Error in line 2", true)));

		AsmCheckResult result = tools.Check("[ENABLE]\nbad\n[DISABLE]\nnop", cancellationToken: Token);

		Assert.Equal(new AsmCheckResult(false, "Error in line 2", true, AsmScriptSection.Enable), result);
		Assert.Empty(harness.LuaCalls);
	}

	[Fact]
	public void Check_BothSectionsAccepted_OmitsFailedSection()
	{
		StateTestHarness harness = new();
		harness.Answer<AsmLuaCheck>(static _ => new AsmLuaCheck(true));
		AsmTools tools = new(harness.Dispatch, harness.Resources,
			CheckingAutoAssembler(new AutoAssemblerCheckResult(true, null, false)));

		AsmCheckResult result = tools.Check("[ENABLE]\nnop\n[DISABLE]\nnop", cancellationToken: Token);

		Assert.Equal(new AsmCheckResult(true), result);
		Assert.Equal("{\"accepted\":true,\"hostMessagesTruncated\":false}",
			JsonSerializer.Serialize(result, AsmJsonContext.Default.AsmCheckResult));
		Assert.Contains("\"failedSection\":\"disable\"",
			JsonSerializer.Serialize(new AsmCheckResult(false, "x", false, AsmScriptSection.Disable),
				AsmJsonContext.Default.AsmCheckResult), StringComparison.Ordinal);
	}

	[Fact]
	public void GenerateApiHook_UsesTheFixedClientLuaScript()
	{
		StateTestHarness harness = new();
		string? source = null;
		harness.Answer<AsmLuaScript>(value =>
		{
			source = value;
			return new AsmLuaScript("[ENABLE]\nnop\n[DISABLE]\nnop");
		});

		AsmGeneratedScript result = new AsmTools(harness.Dispatch, harness.Resources).GenerateApiHook("401000",
			"401005",
			cancellationToken: Token);

		Assert.Equal("[ENABLE]\nnop\n[DISABLE]\nnop", result.Script);
		Assert.Contains("generateAPIHookScript(a[1], a[2], a[3], a[4], a[5])", source, StringComparison.Ordinal);
		Assert.DoesNotContain("load(", source, StringComparison.Ordinal);
	}

	[Fact]
	public void GenerateApiHook_IncompleteHostSource_ReportsHostRefusal()
	{
		StateTestHarness harness = new();
		harness.Answer<AsmLuaScript>(static _ => new AsmLuaScript("[ENABLE]\nnop"));

		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() =>
			new AsmTools(harness.Dispatch, harness.Resources).GenerateApiHook("401000", "401005",
				cancellationToken: Token));

		Assert.Equal((ToolErrorKind.HostRefused, ToolHostEffect.Completed),
			(exception.Error.Kind, exception.Error.HostEffect));
	}

	[Fact]
	public void Apply_WithoutDisableSection_RefusesBeforeClientMutation()
	{
		AutoAssemblerHarness harness = new();

		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() =>
			harness.Tools.Apply("[ENABLE]\nnop", cancellationToken: Token));

		Assert.Equal((ToolErrorKind.InvalidArgument, ToolHostEffect.NotStarted),
			(exception.Error.Kind, exception.Error.HostEffect));
		Assert.Equal(0, harness.ApplyCalls);
	}

	[Fact]
	public void Apply_WhenAutoAssemblerIsDisabled_RefusesBeforeClientMutation()
	{
		AutoAssemblerHarness harness = new(new McpFeatureOptions { EnableAutoAssembler = false });

		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() =>
			harness.Tools.Apply("[ENABLE]\nnop\n[DISABLE]\nnop", cancellationToken: Token));

		Assert.Equal((ToolErrorKind.CapabilityDisabled, ToolHostEffect.NotStarted),
			(exception.Error.Kind, exception.Error.HostEffect));
		Assert.Equal(0, harness.ApplyCalls);
	}

	[Fact]
	public void AutoAssemblerEntryPoints_DeclareTheStaticAutoAssemblerRequirement()
	{
		MethodInfo[] methods =
		[
			Method(nameof(AsmTools.Check)), Method(nameof(AsmTools.Apply)), Method(nameof(AsmTools.ApplyCodePatch))
		];

		Assert.All(methods, static method => Assert.Contains(method.GetCustomAttributes<RequiresFeatureAttribute>(),
			static requirement => requirement.Feature == McpFeature.AutoAssembler));
	}

	[Fact]
	public void ReleasePatch_CompletedLease_ForgetsTheRetainedPatchAndResource()
	{
		AutoAssemblerHarness harness = new();

		AsmPatchApplied applied = harness.Tools.Apply("[ENABLE]\nnop\n[DISABLE]\nnop", "owned", Token);
		Assert.True(applied.Retained);
		Assert.Single(harness.Resources.List());

		AsmPatchReleased released = harness.Tools.ReleasePatch(applied.PatchId, Token);

		Assert.Equal((true, "Released", "Completed", false, false),
			(released.Released, released.Release, released.HostEffect, released.Retryable,
				released.RequiresManualRecovery));
		Assert.Equal(1, harness.ReleaseCalls);
		Assert.Empty(harness.Tools.ListPatches().Patches);
		Assert.Empty(harness.Resources.List());
	}

	[Fact]
	public void Apply_AfterTargetChange_ReportsTheActualUnretainedReleaseOutcome()
	{
		AutoAssemblerHarness harness = new(null, true);

		AsmPatchApplied applied = harness.Tools.Apply("[ENABLE]\nnop\n[DISABLE]\nnop", cancellationToken: Token);

		Assert.Equal((false, true, true, "RefusedTargetChanged", "NotStarted"),
			(applied.Retained, applied.AppliedAfterTargetChange, applied.RequiresManualRecovery, applied.Release,
				applied.HostEffect));
		Assert.Empty(harness.Tools.ListPatches().Patches);
		Assert.Empty(harness.Resources.List());
	}

	[Fact]
	public void Load_LuaTableWithUnsafeLuaDisabled_RefusesBeforeTheClientCanLoadIt()
	{
		string root = _scratch.CreateFolder("tables");
		string table = Path.Combine(root, "lua.ct");
		File.WriteAllText(table, "<?xml version=\"1.0\"?><CheatTable><LuaScript>print('x')</LuaScript></CheatTable>");
		RecordingDispatcher dispatcher = new();
		ToolDispatch dispatch = CreateDispatch(dispatcher, new McpFeatureOptions { EnableUnsafeLua = false });
		McpFilePaths files = new(new McpFileOptions(), _scratch.CreateFolder("registry"),
			_scratch.CreateFolder("data"));
		CheatEngineClientOptions options = new();
		options.AllowedTableRoots.Add(root);
		TableTools tools = new(dispatch, files, Options.Create(options));

		CheatEngineToolException exception =
			Assert.Throws<CheatEngineToolException>(() => tools.Load(table, false, Token));

		Assert.Equal((ToolErrorKind.CapabilityDisabled, ToolHostEffect.NotStarted),
			(exception.Error.Kind, exception.Error.HostEffect));
		Assert.Equal(0, dispatcher.Calls);
	}

	[Fact]
	public void Load_MalformedTablePath_ReportsInvalidArgument()
	{
		TableTools tools = new(CreateDispatch(new RecordingDispatcher(), new McpFeatureOptions()),
			new McpFilePaths(new McpFileOptions(), _scratch.CreateFolder("registry"), _scratch.CreateFolder("data")),
			Options.Create(new CheatEngineClientOptions()));

		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() =>
			tools.Load("C:\\tables\\bad\0.ct", cancellationToken: Token));

		Assert.Equal((ToolErrorKind.InvalidArgument, ToolHostEffect.NotStarted),
			(exception.Error.Kind, exception.Error.HostEffect));
	}

	[Fact]
	public void Load_PathOutsideClientTableRoots_RefusesBeforeInspectionOrClientLoad()
	{
		string root = _scratch.CreateFolder("tables");
		string outside = Path.Combine(_scratch.CreateFolder("outside"), "untrusted.ct");
		File.WriteAllText(outside, "<?xml version=\"1.0\"?><CheatTable />");
		RecordingDispatcher dispatcher = new();
		CheatEngineClientOptions options = new();
		options.AllowedTableRoots.Add(root);
		TableTools tools = new(CreateDispatch(dispatcher, new McpFeatureOptions()),
			new McpFilePaths(new McpFileOptions(), _scratch.CreateFolder("registry"), _scratch.CreateFolder("data")),
			Options.Create(options));

		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() =>
			tools.Load(outside, cancellationToken: Token));

		Assert.Equal((ToolErrorKind.InvalidArgument, ToolHostEffect.NotStarted),
			(exception.Error.Kind, exception.Error.HostEffect));
		Assert.Equal(0, dispatcher.Calls);
	}

	[Fact]
	public void ListFiles_AllowedRoots_ReturnsOnlyTablesAndSkipsNestedJunctions()
	{
		string root = _scratch.CreateFolder("tables");
		File.WriteAllText(Path.Combine(root, "first.ct"), "table");
		File.WriteAllText(Path.Combine(root, "second.XML"), "table");
		File.WriteAllText(Path.Combine(root, "ignored.txt"), "table");
		string outside = _scratch.CreateFolder("outside");
		File.WriteAllText(Path.Combine(outside, "hidden.ct"), "table");
		_scratch.CreateJunction("tables\\linked", outside);
		CheatEngineClientOptions options = new();
		options.AllowedTableRoots.Add(root);
		McpFilePaths files = new(new McpFileOptions(), _scratch.CreateFolder("registry"),
			_scratch.CreateFolder("data"));
		TableTools tools = new(CreateDispatch(new RecordingDispatcher(), new McpFeatureOptions()), files,
			Options.Create(options));

		TableFilePage result = tools.ListFiles();

		Assert.True(result.Exact);
		Assert.Equal(2, result.Total);
		Assert.Equal(["first.ct", "second.XML"], result.Files.Select(file => Path.GetFileName(file.Path)).ToArray());
		Assert.All(result.Files, file => Assert.True(file.LastWriteUtc.Offset == TimeSpan.Zero));
	}

	private static ToolDispatch CreateDispatch(RecordingDispatcher dispatcher, McpFeatureOptions features)
	{
		IOptions<McpExecutionOptions> execution = Options.Create(new McpExecutionOptions());
		return new ToolDispatch(ClientTestDouble.Client(dispatcher.Dispatcher, CancellationToken.None),
			new McpFeatureGate(Options.Create(features)), execution, new DispatchStatistics(execution),
			TimeProvider.System,
			new RecordingLogger<ToolDispatch>());
	}

	private static MethodInfo Method(string name)
	{
		return typeof(AsmTools).GetMethod(name, BindingFlags.Instance | BindingFlags.Public)
			   ?? throw new InvalidOperationException($"Missing {name}.");
	}

	private static IAutoAssemblerClient CheckingAutoAssembler(AutoAssemblerCheckResult enable)
	{
		return ClientTestDouble.Create<IAutoAssemblerClient>((method, _) =>
			method.Name == nameof(IAutoAssemblerClient.Check) ? enable : throw new NotSupportedException(method.Name));
	}

	private sealed class AutoAssemblerHarness
	{
		private readonly bool _appliedAfterTargetChange;
		private bool _released;

		internal AutoAssemblerHarness(McpFeatureOptions? features = null, bool appliedAfterTargetChange = false)
		{
			_appliedAfterTargetChange = appliedAfterTargetChange;
			Resources = new TargetResources();
			Tools = new AsmTools(CreateDispatch(new RecordingDispatcher(), features ?? new McpFeatureOptions()),
				Resources,
				ClientTestDouble.Create<IAutoAssemblerClient>(AutoAssembler));
		}

		internal int ApplyCalls
		{
			get;
			private set;
		}

		internal int ReleaseCalls
		{
			get;
			private set;
		}

		internal TargetResources Resources
		{
			get;
		}

		internal AsmTools Tools
		{
			get;
		}

		private object? AutoAssembler(MethodInfo method, object?[]? arguments)
		{
			if (method.Name != nameof(IAutoAssemblerClient.ApplyPatch))
			{
				throw new NotSupportedException(method.Name);
			}

			ApplyCalls++;
			return ClientTestDouble.Create<IAutoAssemblerPatchLease>(Lease);
		}

		private object? Lease(MethodInfo method, object?[]? arguments)
		{
			return method.Name switch
			{
				"get_Name" => "owned",
				"get_SelectionEpoch" => 1L,
				"get_AppliedAfterTargetChange" => _appliedAfterTargetChange,
				"get_CanDisable" => true,
				"get_IsReleased" => _released || _appliedAfterTargetChange,
				"get_RequiresManualRecovery" => _appliedAfterTargetChange,
				"get_LastReleaseOutcome" => _appliedAfterTargetChange
					? new LeaseReleaseOutcome(LeaseReleaseKind.RefusedTargetChanged, CheatEngineHostEffect.NotStarted)
					: null,
				"get_HostWarnings" => null,
				"get_HostWarningsTruncated" => false,
				"Release" => Release(),
				_ => throw new NotSupportedException(method.Name)
			};
		}

		private LeaseReleaseOutcome Release()
		{
			ReleaseCalls++;
			_released = true;
			return new LeaseReleaseOutcome(LeaseReleaseKind.Released, CheatEngineHostEffect.Completed);
		}
	}

	private sealed class SearchHarness : IDisposable
	{
		private readonly int _instructionLength;

		internal SearchHarness(int instructionLength = 1)
		{
			_instructionLength = instructionLength;
			IInspectionClient inspection = ClientTestDouble.Create<IInspectionClient>(Resolve);
			IProcessClient processes = ClientTestDouble.Create<IProcessClient>(Process);
			IAssemblyClient assembly = ClientTestDouble.Create<IAssemblyClient>(Disassemble);
			Client = ClientTestDouble.Client(Dispatcher.Dispatcher, CancellationToken.None,
				(nameof(ICheatEngineClient.Inspection), inspection), (nameof(ICheatEngineClient.Processes), processes),
				(nameof(ICheatEngineClient.Assembly), assembly));
			IOptions<McpExecutionOptions> execution = Options.Create(new McpExecutionOptions());
			StateTestHarness lua = new();
			lua.Answer<CodeLuaDisassemblyColumns>(_ => new CodeLuaDisassemblyColumns("401000", "nop", string.Empty));
			Dispatch = new ToolDispatch(Client, new McpFeatureGate(Options.Create(new McpFeatureOptions())), execution,
				new DispatchStatistics(execution), TimeProvider.System, new RecordingLogger<ToolDispatch>(), lua.FixedLua);
			Resources = new TargetResources();
			Jobs = new JobRegistry(Dispatch, Resources, execution, TimeProvider.System);
		}

		internal ICheatEngineClient Client
		{
			get;
		}

		internal RecordingDispatcher Dispatcher
		{
			get;
		} = new();

		internal ToolDispatch Dispatch
		{
			get;
		}

		internal ManualResetEventSlim EnteredDisassembly
		{
			get;
		} = new();

		internal JobRegistry Jobs
		{
			get;
		}

		internal ManualResetEventSlim ReleaseDisassembly
		{
			get;
		} = new();

		internal TargetResources Resources
		{
			get;
		}

		public void Dispose()
		{
			ReleaseDisassembly.Set();
			Jobs.Dispose();
			EnteredDisassembly.Dispose();
			ReleaseDisassembly.Dispose();
		}

		private object? Disassemble(MethodInfo method, object?[]? arguments)
		{
			if (method.Name != nameof(IAssemblyClient.Disassemble))
			{
				throw new NotSupportedException(method.Name);
			}

			EnteredDisassembly.Set();
			ReleaseDisassembly.Wait();
			Address address = (Address) arguments![0]!;
			return new AssemblyInstructionSnapshot(address, _instructionLength, string.Empty, "90", "nop", [0x90]);
		}

		private static object? Process(MethodInfo method, object?[]? arguments)
		{
			if (method.Name != nameof(IProcessClient.GetCurrentProcess))
			{
				throw new NotSupportedException(method.Name);
			}

			return new ProcessSnapshot(new TargetProcessId(42), null, null, TargetBackend.LocalProcess,
				CheatEngineArchitecture.X64, PointerSize.Bit64, 8, null, 1);
		}

		private static object? Resolve(MethodInfo method, object?[]? arguments)
		{
			if (method.Name != nameof(IInspectionClient.TryResolveAddress))
			{
				throw new NotSupportedException(method.Name);
			}

			arguments![2] = new Address(0x401000);
			arguments[3] = default(CheatEngineFailure);
			return true;
		}
	}
}
