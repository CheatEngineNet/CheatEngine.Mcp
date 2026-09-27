using System.Collections.Immutable;
using System.Reflection;

using CheatEngine.Client;
using CheatEngine.Client.Inspection;
using CheatEngine.Client.Lua;
using CheatEngine.Client.Memory;
using CheatEngine.Client.Processes;
using CheatEngine.Client.Results;
using CheatEngine.Mcp.Core.Features;
using CheatEngine.Mcp.Tests.Support;
using CheatEngine.SDK.Engine.Inspection;
using CheatEngine.SDK.Engine.Runtime;
using CheatEngine.SDK.Engine.Values;

using Microsoft.Extensions.Options;

namespace CheatEngine.Mcp.Tests.Tools.Modules;

/// <summary>
///     A simulated target for the module and symbol tools: a process, its modules and sections, a mapped memory range,
///     symbol tables and canned Lua results. Every Client member the tools may use is counted; any other member fails the
///     test.
/// </summary>
internal sealed class ModuleSymbolTarget
{
	internal const int ProcessId = 4242;

	private readonly Dictionary<string, int> _calls = new(StringComparer.Ordinal);

	internal ModuleSymbolTarget()
	{
		IProcessClient processes = ClientTestDouble.Create<IProcessClient>((method, _) =>
		{
			Count(method);
			return method.Name switch
			{
				nameof(IProcessClient.GetCurrentProcess) => CurrentProcess(),
				_ => throw new NotSupportedException($"Unexpected process call {method.Name}.")
			};
		});
		IInspectionClient inspection = ClientTestDouble.Create<IInspectionClient>((method, arguments) =>
		{
			Count(method);
			return Inspect(method, arguments!);
		});
		IMemoryClient memory = ClientTestDouble.Create<IMemoryClient>((method, arguments) =>
		{
			Count(method);
			return method.Name == nameof(IMemoryClient.ReadBytesDetailed)
				? Read((MemoryBytesReadRequest) arguments![0]!)
				: throw new NotSupportedException($"Unexpected memory call {method.Name}.");
		});
		ILuaClient lua = ClientTestDouble.Create<ILuaClient>((method, arguments) =>
		{
			Count(method);
			Assert.Equal(nameof(ILuaClient.Execute), method.Name);
			object operation = arguments![0]!;
			LuaSources.Add((string) operation.GetType().GetProperty("Source")!.GetValue(operation)!);
			Type resultType = method.GetGenericArguments()[1];
			Type valueType = resultType.GetGenericArguments()[0];
			if (LuaFailure is { } failure)
			{
				throw failure;
			}

			object? value = LuaResults.TryGetValue(valueType, out object? canned)
				? canned
				: throw new NotSupportedException($"No Lua result for {valueType.Name}.");
			return Activator.CreateInstance(resultType, value, LuaError, 0);
		});
		Client = ClientTestDouble.Client(Dispatcher.Dispatcher, CancellationToken.None,
			(nameof(ICheatEngineClient.Processes), processes), (nameof(ICheatEngineClient.Inspection), inspection),
			(nameof(ICheatEngineClient.Memory), memory), (nameof(ICheatEngineClient.Lua), lua));
		Dispatch = CreateDispatch(Client);
	}

	internal RecordingDispatcher Dispatcher
	{
		get;
	} = new();

	internal ICheatEngineClient Client
	{
		get;
	}

	internal ToolDispatch Dispatch
	{
		get;
	}

	internal bool Attached
	{
		get;
		set;
	} = true;

	internal long SelectionEpoch
	{
		get;
		set;
	} = 7;

	/// <summary>Runs before every memory read; a test uses it to change the target mid-operation.</summary>
	internal Action<MemoryBytesReadRequest>? BeforeRead
	{
		get;
		set;
	}

	internal List<ModuleInfo> Modules
	{
		get;
	} = [];

	internal Dictionary<string, ModuleSectionInfo[]> Sections
	{
		get;
	} = new(StringComparer.OrdinalIgnoreCase);

	internal Dictionary<string, ulong> Addresses
	{
		get;
	} = new(StringComparer.Ordinal);

	internal Dictionary<ulong, string> Names
	{
		get;
	} = [];

	internal Dictionary<string, SymbolInfo> Symbols
	{
		get;
	} = new(StringComparer.Ordinal);

	/// <summary>Failures that an expression's resolution returns instead of an address.</summary>
	internal Dictionary<string, CheatEngineFailureKind> ResolveFailures
	{
		get;
	} = new(StringComparer.Ordinal);

	internal ulong MemoryBase
	{
		get;
		set;
	}

	internal byte[] Memory
	{
		get;
		set;
	} = [];

	/// <summary>4 KiB pages, by address, that fail to read.</summary>
	internal HashSet<ulong> UnreadablePages
	{
		get;
	} = [];

	internal Func<SymbolRegistration, ISymbolRegistrationLease>? Register
	{
		get;
		set;
	}

	internal Dictionary<Type, object> LuaResults
	{
		get;
	} = [];

	internal object? LuaError
	{
		get;
		set;
	}

	internal Exception? LuaFailure
	{
		get;
		set;
	}

	internal List<string> LuaSources
	{
		get;
	} = [];

	internal int TotalClientCalls => _calls.Values.Sum();

	internal int Calls(string member)
	{
		return _calls.GetValueOrDefault(member);
	}

	internal static ToolDispatch CreateDispatch(ICheatEngineClient client)
	{
		IOptions<McpExecutionOptions> options = Options.Create(new McpExecutionOptions());
		return new ToolDispatch(client, new McpFeatureGate(Options.Create(new McpFeatureOptions())), options,
			new DispatchStatistics(options), TimeProvider.System, new RecordingLogger<ToolDispatch>(),
			new PluginFixedLuaExecutor(client));
	}

	internal ModuleInfo AddModule(string name, ulong baseAddress, ulong size, string path = "C:\\Games\\Game\\game.exe")
	{
		ModuleInfo module = new(name, new Address(baseAddress), new MemorySize(size), true, path);
		Modules.Add(module);
		return module;
	}

	private ProcessSnapshot CurrentProcess()
	{
		if (!Attached)
		{
			throw new CheatEngineFailure(CheatEngineFailureKind.TargetNotAttached, "Process.Current",
				"No process is attached.", hostEffect: CheatEngineHostEffect.NotStarted).ToException();
		}

		return new ProcessSnapshot(new TargetProcessId(ProcessId), "game.exe", null, TargetBackend.LocalProcess,
			CheatEngineArchitecture.X64, PointerSize.Bit64, 8, null, SelectionEpoch);
	}

	private object? Inspect(MethodInfo method, object?[] arguments)
	{
		switch (method.Name)
		{
			case nameof(IInspectionClient.TryGetModules):
				{
					InspectionCollectionRequest request = (InspectionCollectionRequest) arguments[0]!;
					if (!Attached && arguments[1] is null)
					{
						return Fail(arguments, 3, CheatEngineFailureKind.TargetNotAttached,
							ImmutableArray<ModuleInfo>.Empty);
					}

					if (Modules.Count > request.MaximumItems)
					{
						return Fail(arguments, 3, CheatEngineFailureKind.ResultLimitExceeded,
							ImmutableArray<ModuleInfo>.Empty);
					}

					arguments[2] = Modules.ToImmutableArray();
					arguments[3] = default(CheatEngineFailure);
					return true;
				}
			case nameof(IInspectionClient.GetModuleSections):
				return Sections.TryGetValue(((ModuleName) arguments[0]!).Value, out ModuleSectionInfo[]? sections)
					? sections.ToImmutableArray()
					: ImmutableArray<ModuleSectionInfo>.Empty;
			case nameof(IInspectionClient.TryResolveAddress):
				{
					string expression = ((SymbolExpression) arguments[0]!).Value;
					if (ResolveFailures.TryGetValue(expression, out CheatEngineFailureKind kind))
					{
						return Fail(arguments, 3, kind, default(Address));
					}

					if (!Addresses.TryGetValue(expression, out ulong address))
					{
						return Fail(arguments, 3, CheatEngineFailureKind.NotFound, default(Address));
					}

					arguments[2] = new Address(address);
					arguments[3] = default(CheatEngineFailure);
					return true;
				}
			case nameof(IInspectionClient.TryResolveName):
				{
					ulong address = ((Address) arguments[0]!).ToUInt64();
					if (!Names.TryGetValue(address, out string? name))
					{
						return Fail(arguments, 2, CheatEngineFailureKind.NotFound, null);
					}

					arguments[1] = name;
					arguments[2] = default(CheatEngineFailure);
					return true;
				}
			case nameof(IInspectionClient.TryGetSymbol):
				{
					string expression = ((SymbolExpression) arguments[0]!).Value;
					if (!Symbols.TryGetValue(expression, out SymbolInfo symbol))
					{
						return Fail(arguments, 2, CheatEngineFailureKind.NotFound, default(SymbolInfo));
					}

					arguments[1] = symbol;
					arguments[2] = default(CheatEngineFailure);
					return true;
				}
			case nameof(IInspectionClient.RegisterSymbol):
				return (Register ?? throw new NotSupportedException("No symbol registration was configured."))(
					(SymbolRegistration) arguments[0]!);
			default:
				throw new NotSupportedException($"Unexpected inspection call {method.Name}.");
		}
	}

	private static bool Fail(object?[] arguments, int index, CheatEngineFailureKind kind, object? value)
	{
		// A DispatchProxy copies every out argument back, so a struct out value must be set even on failure.
		arguments[index - 1] = value;
		arguments[index] = new CheatEngineFailure(kind, "Inspection", $"The simulated call failed with {kind}.",
			hostEffect: CheatEngineHostEffect.NotStarted);
		return false;
	}

	private MemoryBytesReadOutcome Read(MemoryBytesReadRequest request)
	{
		BeforeRead?.Invoke(request);
		ulong start = request.Address.ToUInt64();
		int confirmed = 0;
		while (confirmed < request.Length)
		{
			ulong address = start + (ulong) confirmed;
			if (address < MemoryBase || address - MemoryBase >= (ulong) Memory.Length ||
				UnreadablePages.Contains(address & ~0xFFFUL))
			{
				break;
			}

			confirmed++;
		}

		ImmutableArray<byte> bytes = confirmed == 0
			? []
			: [.. Memory.AsSpan((int) (start - MemoryBase), confirmed)];
		return confirmed == request.Length
			? new MemoryBytesReadOutcome(request.Length, bytes, null)
			: new MemoryBytesReadOutcome(request.Length, bytes,
				new CheatEngineFailure(CheatEngineFailureKind.MemoryReadFailed, "Memory.ReadBytes",
					"The simulated page is unreadable.", hostEffect: CheatEngineHostEffect.Completed));
	}

	private void Count(MethodInfo method)
	{
		_calls[method.Name] = _calls.GetValueOrDefault(method.Name) + 1;
	}
}
