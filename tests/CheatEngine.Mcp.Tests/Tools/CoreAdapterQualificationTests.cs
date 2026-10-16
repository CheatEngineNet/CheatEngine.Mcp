using System.Collections.Immutable;
using System.Reflection;

using CheatEngine.Client;
using CheatEngine.Client.Assembly;
using CheatEngine.Client.Inspection;
using CheatEngine.Client.Lua;
using CheatEngine.Client.Processes;
using CheatEngine.Client.Results;
using CheatEngine.Mcp.Core.Contract;
using CheatEngine.Mcp.Core.Features;
using CheatEngine.Mcp.Core.Files;
using CheatEngine.Mcp.Core.Jobs;
using CheatEngine.Mcp.Core.Values;
using CheatEngine.Mcp.Tests.Support;
using CheatEngine.Mcp.Tools.Asm;
using CheatEngine.Mcp.Tools.Code;
using CheatEngine.Mcp.Tools.Processes;
using CheatEngine.SDK.Engine.Inspection;
using CheatEngine.SDK.Engine.Runtime;
using CheatEngine.SDK.Engine.Values;

using Microsoft.Extensions.Options;

namespace CheatEngine.Mcp.Tests.Tools;

/// <summary>Positive adapter checks for the remaining typed and fixed-Lua Code and Process surface.</summary>
public sealed class CoreAdapterQualificationTests
{
	private static CancellationToken Token => TestContext.Current.CancellationToken;

	[Fact]
	public void Assemble_SequentialInstructions_ForwardActualOriginsAndEncodingOptions()
	{
		using CodeAdapterHarness harness = new();
		AsmTools tools = new(harness.Dispatch, harness.Resources);

		AsmAssembleResult result = tools.Assemble("401000", ["mov eax,ebx", "ret"],
			InstructionEncodingPreference.Long, true, Token);

		Assert.Equal(("8BC3C3", 3), (result.Bytes, result.Size));
		Assert.Equal([new AsmInstruction("401000", "mov eax,ebx", "8BC3", 2),
			new AsmInstruction("401002", "ret", "C3", 1)], result.Instructions);
		Assert.Equal([new AssemblyInstructionRequest(new Address(0x401000), "mov eax,ebx", InstructionEncodingPreference.Long, true),
			new AssemblyInstructionRequest(new Address(0x401002), "ret", InstructionEncodingPreference.Long, true)], harness.Assembled);
	}

	[Fact]
	public async Task DissectJob_KnownRange_PollsRetainedEventAndReleasesJob()
	{
		using CodeAdapterHarness harness = new();
		harness.Answer<CodeLuaDissected>(static _ => new CodeLuaDissected("401000", 16));
		CodeJobStart started = harness.Tools.StartDissect("401000", 16, 30, Token);
		ManagedJob<CodeJobEvent> job = harness.Jobs.Get<ManagedJob<CodeJobEvent>>(started.JobId, CodeTools.JobKind);
		try
		{
			await job.Completion.WaitAsync(TimeSpan.FromSeconds(10), Token);
			CodeJobPage page = harness.Tools.PollJob(started.JobId, 0, 1);
			Assert.True(page.Job.State == JobState.Completed,
				page.Job.Error + string.Join('\n', harness.Log.Entries.Select(static entry => entry.Exception?.ToString())));
			Assert.Equal(new CodeJobEvent("dissect", "401000", null, 16), Assert.Single(page.Items));
			Assert.Equal((JobState.Completed, 16L, 16L, 1L, false, 0L),
				(page.Job.State, page.Job.ProgressDone, page.Job.ProgressTotal, page.NextAfterSequence, page.More, page.Dropped));
			Assert.Equal(page.Items, harness.Tools.PollJob(started.JobId, 0, 1).Items);
			Assert.Empty(harness.Tools.PollJob(started.JobId, page.NextAfterSequence, 1).Items);
		}
		finally
		{
			Assert.True(harness.Jobs.Stop(started.JobId, Token).Released);
		}
		Assert.Empty(harness.Resources.List());
		Assert.Equal(0, harness.Jobs.Count);
		Assert.Equal([CheatEngineToolNames.CodeStartDissect], harness.LuaOperations);
		Assert.Contains("[1] = 0x401000, [2] = 16", harness.LuaSources[0], StringComparison.Ordinal);
	}

	[Fact]
	public void DecodeAndDisassemble_MapTypedInstructionsInRequestedOrder()
	{
		using CodeAdapterHarness harness = new();
		Queue<CodeLuaDisassemblyColumns> columns = new(
		[
			new CodeLuaDisassemblyColumns("401000", "mov eax,ebx", ""),
			new CodeLuaDisassemblyColumns("401000", "mov eax,ebx", ""),
			new CodeLuaDisassemblyColumns("401002", "ret", "")
		]);
		harness.Answer<CodeLuaDisassemblyColumns>(_ => columns.Dequeue());
		harness.AddInstruction(0x401000, "mov eax,ebx", [0x8B, 0xC3]);
		harness.AddInstruction(0x401002, "ret", [0xC3]);
		CodeTools tools = harness.Tools;

		CodeDecodeResult decoded = tools.Decode("401000", Token);
		CodeDisassembly disassembly = tools.Disassemble("401000", 2, cancellationToken: Token);

		Assert.Equal(("401000", "mov eax,ebx", "8BC3", 2),
			(decoded.Instruction.Address, decoded.Instruction.Opcode, decoded.Instruction.Bytes, decoded.Length));
		Assert.Equal(["401000", "401002"], disassembly.Instructions.Select(static item => item.Address));
		Assert.Equal(["8BC3", "C3"], disassembly.Instructions.Select(static item => item.Bytes));
		Assert.Equal([0x401000UL, 0x401000UL, 0x401002UL], harness.Disassembled);
		Assert.Empty(columns);
	}

	[Fact]
	public void DissectorViews_MapFixedLuaPagesAndClearSideEffect()
	{
		using CodeAdapterHarness harness = new();
		harness.Answer<CodeLuaReferencePage>(_ => new CodeLuaReferencePage("5000", 2, true,
			[new CodeLuaReference("401000", "5000", "call")], 1));
		harness.Answer<CodeLuaStringPage>(_ => new CodeLuaStringPage(2, true,
			[new CodeLuaString("6000", "Health")], 1));
		harness.Answer<CodeLuaFunctionPage>(_ => new CodeLuaFunctionPage(2, ["401000"], 1));
		harness.Answer<CodeLuaCleared>(_ => new CodeLuaCleared(true));

		CodeReferencePage references = harness.Tools.FindReferences("5000", 1, 1, Token);
		CodeStringPage strings = harness.Tools.FindStrings("hea", 1, 1, Token);
		CodeFunctionPage functions = harness.Tools.ListFunctions(1, 1, Token);
		CodeClearResult cleared = harness.Tools.ClearDissect(Token);

		Assert.Equal(("5000", 2, true, 1, 1),
			(references.Address, references.Total, references.Exact, references.Offset, references.NextOffset));
		Assert.Equal(new CodeReference("401000", "5000", "call"), Assert.Single(references.References));
		Assert.Equal((2, true, 1, "6000", "Health"),
			(strings.Total, strings.Exact, strings.Offset, strings.Strings[0].Address, strings.Strings[0].Text));
		Assert.Equal((2, 1, "401000", 1),
			(functions.Total, functions.Offset, Assert.Single(functions.Functions), functions.NextOffset));
		Assert.True(cleared.Cleared);
		Assert.Equal(
		[
			CheatEngineToolNames.CodeFindReferences, CheatEngineToolNames.CodeFindStrings,
			CheatEngineToolNames.CodeListFunctions, CheatEngineToolNames.CodeClearDissect
		], harness.LuaOperations);
	}

	[Fact]
	public void ProcessListCurrentAndAttach_ForwardTypedRequestsAndMapResults()
	{
		using ProcessAdapterHarness harness = new();
		harness.Current = Process(7, "old.exe", 4, 2);
		harness.Local = new LocalProcessEnumerationResult(
			ImmutableArray.Create(new LocalProcessSnapshot(new LocalProcessId(11), "alpha.exe", null),
				new LocalProcessSnapshot(new LocalProcessId(12), "beta.exe", null)), true);
		harness.Attached = Process(12, "beta.exe", 8, 3);

		ProcessListResult listed = harness.Tools.List(2, "beta", Token);
		ProcessCurrentResult current = harness.Tools.GetCurrent(Token);
		ProcessAttachResult attached = harness.Tools.Attach("12", Token);

		Assert.Equal([new ProcessSummary(11, "alpha.exe"), new ProcessSummary(12, "beta.exe")], listed.Processes);
		Assert.True(listed.Truncated);
		Assert.Equal(new ProcessCurrentResult(true, 7, "old.exe", 4, 2), current);
		Assert.Equal(new ProcessAttachResult(12, "beta.exe", false), attached);
		Assert.Equal(new LocalProcessEnumerationRequest(2, "beta"), Assert.Single(harness.ListRequests));
		Assert.Equal([nameof(IProcessClient.GetLocalProcesses), nameof(IProcessClient.TryGetCurrentProcess),
			nameof(IProcessClient.TryGetCurrentProcess), nameof(IProcessClient.Attach)], harness.Calls);
	}

	[Fact]
	public void ProcessGetCurrent_Unattached_MapsTheNormalEmptyState()
	{
		using ProcessAdapterHarness harness = new()
		{
			HasCurrent = false
		};

		ProcessCurrentResult current = harness.Tools.GetCurrent(Token);

		Assert.Equal(new ProcessCurrentResult(false), current);
		Assert.Equal([nameof(IProcessClient.TryGetCurrentProcess)], harness.Calls);
	}

	private sealed class CodeAdapterHarness : IDisposable
	{
		private readonly StateTestHarness _lua = new();
		private readonly Dictionary<ulong, (string Opcode, byte[] Bytes)> _instructions = [];

		internal CodeAdapterHarness()
		{
			IAssemblyClient assembly = ClientTestDouble.Create<IAssemblyClient>(Assembly);
			IInspectionClient inspection = ClientTestDouble.Create<IInspectionClient>(Inspection);
			IProcessClient processes = ClientTestDouble.Create<IProcessClient>((method, _) =>
			{
				Assert.Equal(nameof(IProcessClient.GetCurrentProcess), method.Name);
				return Process(7, "owned.exe", 8, 1);
			});
			ICheatEngineClient client = ClientTestDouble.Client(new RecordingDispatcher().Dispatcher, CancellationToken.None,
				(nameof(ICheatEngineClient.Assembly), assembly), (nameof(ICheatEngineClient.Inspection), inspection),
				(nameof(ICheatEngineClient.Processes), processes));
			IOptions<McpExecutionOptions> options = Options.Create(new McpExecutionOptions());
			ToolDispatch dispatch = new(client, new McpFeatureGate(Options.Create(new McpFeatureOptions())), options,
				new DispatchStatistics(options), TimeProvider.System, Log, _lua.FixedLua);
			Dispatch = dispatch;
			Resources = new TargetResources();
			Jobs = new JobRegistry(dispatch, Resources, options, TimeProvider.System);
			Tools = new CodeTools(dispatch, Jobs);
		}

		internal TargetResources Resources
		{
			get;
		}
		internal ToolDispatch Dispatch
		{
			get;
		}
		internal JobRegistry Jobs
		{
			get;
		}
		internal CodeTools Tools
		{
			get;
		}
		internal List<ulong> Disassembled { get; } = [];
		internal RecordingLogger<ToolDispatch> Log { get; } = new();
		internal List<AssemblyInstructionRequest> Assembled { get; } = [];
		internal string[] LuaOperations => [.. _lua.LuaCalls.Select(static call => call.Operation)];
		internal string[] LuaSources => [.. _lua.LuaCalls.Select(static call => call.Source)];

		internal void Answer<T>(Func<string, T> answer) => _lua.Answer(answer);
		internal void AddInstruction(ulong address, string opcode, byte[] bytes) => _instructions[address] = (opcode, bytes);

		private object? Inspection(MethodInfo method, object?[]? arguments)
		{
			Assert.Equal(nameof(IInspectionClient.TryResolveAddress), method.Name);
			object?[] values = arguments!;
			Assert.True(HexParse.TryAddress(((SymbolExpression) values[0]!).Value, out ulong address));
			values[2] = new Address(address);
			values[3] = default(CheatEngineFailure);
			return true;
		}

		private object? Assembly(MethodInfo method, object?[]? arguments)
		{
			if (method.Name == nameof(IAssemblyClient.Assemble))
			{
				AssemblyInstructionRequest request = Assert.IsType<AssemblyInstructionRequest>(arguments![0]);
				Assembled.Add(request);
				return request.Instruction == "ret" ? ImmutableArray.Create((byte) 0xC3)
					: ImmutableArray.Create((byte) 0x8B, (byte) 0xC3);
			}
			Assert.Equal(nameof(IAssemblyClient.Disassemble), method.Name);
			object?[] values = arguments!;
			ulong address = ((Address) values[0]!).ToUInt64();
			Disassembled.Add(address);
			(string opcode, byte[] bytes) = _instructions[address];
			return new AssemblyInstructionSnapshot(new Address(address), bytes.Length, HexFormat.Address(address), opcode,
				string.Empty, bytes);
		}

		public void Dispose()
		{
			Jobs.Dispose();
			_lua.Stopping.Dispose();
		}
	}

	private sealed class ProcessAdapterHarness : IDisposable
	{
		private readonly StateTestHarness _lua = new();

		internal ProcessAdapterHarness()
		{
			IProcessClient processes = ClientTestDouble.Create<IProcessClient>(Process);
			ICheatEngineClient client = ClientTestDouble.Client(new RecordingDispatcher().Dispatcher, CancellationToken.None,
				(nameof(ICheatEngineClient.Processes), processes));
			IOptions<McpExecutionOptions> options = Options.Create(new McpExecutionOptions());
			ToolDispatch dispatch = new(client, new McpFeatureGate(Options.Create(new McpFeatureOptions
			{
				EnableTargetCodeExecution = true
			})), options, new DispatchStatistics(options), TimeProvider.System, new RecordingLogger<ToolDispatch>(),
				_lua.FixedLua);
			Resources = new TargetResources();
			Tools = new ProcessTools(dispatch, Resources, new TargetTransitionGuards([]), Files());
			_lua.Answer<MonoAutoAttachState>(_ => new MonoAutoAttachState(false, false, false, true));
		}

		internal ProcessTools Tools
		{
			get;
		}
		internal TargetResources Resources
		{
			get;
		}
		internal ProcessSnapshot Current
		{
			get; set;
		}
		internal ProcessSnapshot Attached
		{
			get; set;
		}
		internal LocalProcessEnumerationResult Local
		{
			get; set;
		}
		internal bool HasCurrent { get; set; } = true;
		internal List<string> Calls { get; } = [];
		internal List<LocalProcessEnumerationRequest> ListRequests { get; } = [];

		private object? Process(MethodInfo method, object?[]? arguments)
		{
			object?[] values = arguments!;
			Calls.Add(method.Name);
			switch (method.Name)
			{
				case nameof(IProcessClient.GetLocalProcesses):
					ListRequests.Add((LocalProcessEnumerationRequest) values[0]!);
					return Local;
				case nameof(IProcessClient.TryGetCurrentProcess):
					if (!HasCurrent)
					{
						values[0] = default(ProcessSnapshot);
						values[1] = new CheatEngineFailure(CheatEngineFailureKind.TargetNotAttached,
							"Processes.GetCurrentProcess", "No process is selected.",
							hostEffect: CheatEngineHostEffect.NotStarted);
						return false;
					}

					values[0] = Current;
					values[1] = default(CheatEngineFailure);
					return true;
				case nameof(IProcessClient.Attach):
					Assert.Equal(12, ((TargetProcessId) values[0]!).Value);
					return Attached;
				default:
					throw new Xunit.Sdk.XunitException($"Unexpected process call {method.Name}.");
			}
		}

		private static McpFilePaths Files()
		{
			string root = Path.GetTempPath();
			return new McpFilePaths(new McpFileOptions { AllowedRoots = [root] }, Path.Combine(root, "ce-mcp-registry"),
				Path.Combine(root, "ce-mcp-data"));
		}

		public void Dispose()
		{
			_lua.Stopping.Dispose();
		}
	}

	private static ProcessSnapshot Process(int id, string name, int pointerSize, long epoch) =>
		new(new TargetProcessId(id), name, null, TargetBackend.LocalProcess,
			pointerSize == 4 ? CheatEngineArchitecture.X86 : CheatEngineArchitecture.X64,
			pointerSize == 4 ? PointerSize.Bit32 : PointerSize.Bit64, pointerSize, null, epoch);
}
