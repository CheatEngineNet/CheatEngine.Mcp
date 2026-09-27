using System.Collections.Concurrent;
using System.Reflection;

using CheatEngine.Client;
using CheatEngine.Client.Allocations;
using CheatEngine.Client.Inspection;
using CheatEngine.Client.Lua;
using CheatEngine.Client.Memory;
using CheatEngine.Client.Processes;
using CheatEngine.Client.Results;
using CheatEngine.Client.Scanning;
using CheatEngine.Mcp.Core.Features;
using CheatEngine.Mcp.Core.Lua;
using CheatEngine.Mcp.Core.Values;
using CheatEngine.Mcp.Tests.Support;
using CheatEngine.SDK.Engine.Inspection;
using CheatEngine.SDK.Engine.Runtime;
using CheatEngine.SDK.Engine.Values;

using Microsoft.Extensions.Options;

using Xunit.Sdk;

namespace CheatEngine.Mcp.Tests.Tools.Memory;

/// <summary>
///     A Client double for the memory and AOB tools: it records every Client call, resolves hexadecimal expressions and
///     named symbols, answers the selected process, and fails the test on any call no handler was given for.
/// </summary>
internal sealed class TargetDouble
{
	private static readonly string[] MutatingPrefixes =
	[
		"Memory.Write", "Memory.TryWrite", "Allocations.Allocate", "Allocations.TryAllocate", "Lease.Release",
		"Lua.Execute"
	];

	internal TargetDouble(CancellationToken stopping = default)
	{
		Client = ClientTestDouble.Client(Dispatcher.Dispatcher, stopping,
			(nameof(ICheatEngineClient.Memory), Recorded<IMemoryClient>("Memory", () => Memory)),
			(nameof(ICheatEngineClient.Inspection), Recorded<IInspectionClient>("Inspection", () => Inspection)),
			(nameof(ICheatEngineClient.Processes), Recorded<IProcessClient>("Processes", Process)),
			(nameof(ICheatEngineClient.Allocations), Recorded<IAllocationClient>("Allocations", () => Allocations)),
			(nameof(ICheatEngineClient.Patterns), Recorded<IPatternScanner>("Patterns", () => Patterns)),
			(nameof(ICheatEngineClient.Lua), Recorded<ILuaClient>("Lua", static () => null)));
		IOptions<McpExecutionOptions> execution = Options.Create(new McpExecutionOptions());
		Dispatch = new ToolDispatch(Client, new McpFeatureGate(Options.Create(new McpFeatureOptions())), execution,
			new DispatchStatistics(execution), TimeProvider.System, new RecordingLogger<ToolDispatch>());
	}

	/// <summary>The dispatcher, which counts every dispatch.</summary>
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

	/// <summary>Every Client call in order, such as <c>Memory.ReadPrimitive&lt;Int32&gt;</c>.</summary>
	internal ConcurrentQueue<string> Calls
	{
		get;
	} = [];

	/// <summary>The named symbols the inspection double resolves besides hexadecimal addresses.</summary>
	internal Dictionary<string, ulong> Symbols
	{
		get;
	} = new(StringComparer.Ordinal);

	internal Func<MethodInfo, object?[], object?>? Memory
	{
		get;
		set;
	}

	/// <summary>Inspection calls other than <c>TryResolveAddress</c>, which the double answers itself.</summary>
	internal Func<MethodInfo, object?[], object?>? Inspection
	{
		get;
		set;
	}

	internal Func<MethodInfo, object?[], object?>? Allocations
	{
		get;
		set;
	}

	internal Func<MethodInfo, object?[], object?>? Patterns
	{
		get;
		set;
	}

	/// <summary>The fixed Lua double: receives the operation's source and returns the typed result value.</summary>
	internal Func<string, object?>? LuaResult
	{
		get;
		set;
	}

	/// <summary>The target-selection epoch reported by each successive process observation.</summary>
	internal Queue<long> Epochs
	{
		get;
	} = [];

	internal PointerSize Bitness
	{
		get;
		set;
	} = PointerSize.Bit64;

	/// <summary>How many calls could change the target or Cheat Engine: writes, allocations, releases and Lua.</summary>
	internal int Mutations => Calls.Count(static call =>
		Array.Exists(MutatingPrefixes, prefix => call.StartsWith(prefix, StringComparison.Ordinal)));

	internal int LuaCalls => Calls.Count(static call => call.StartsWith("Lua.", StringComparison.Ordinal));

	/// <summary>The recorded calls of one service.</summary>
	internal string[] CallsTo(string service)
	{
		return [.. Calls.Where(call => call.StartsWith(service + ".", StringComparison.Ordinal))];
	}

	/// <summary>A lease double that records its releases and returns the queued outcomes.</summary>
	internal ITargetMemoryLease Lease(ulong address, long size, params LeaseReleaseOutcome[] outcomes)
	{
		Queue<LeaseReleaseOutcome> pending = new(outcomes);
		bool released = false;
		return ClientTestDouble.Create<ITargetMemoryLease>((method, _) =>
		{
			switch (method.Name)
			{
				case "get_Address":
					return new Address(address);
				case "get_Size":
					return size;
				case "get_IsReleased":
					return released;
				case "get_RequiresManualRecovery":
					return false;
				case "Release":
					Calls.Enqueue("Lease.Release");
					LeaseReleaseOutcome outcome = pending.Count > 0
						? pending.Dequeue()
						: new LeaseReleaseOutcome(LeaseReleaseKind.Released, CheatEngineHostEffect.Completed);
					released = outcome.IsComplete;
					return outcome;
				default:
					throw new XunitException($"Unexpected lease member {method.Name}.");
			}
		});
	}

	private object? Process(MethodInfo method, object?[] arguments)
	{
		if (method.Name != nameof(IProcessClient.GetCurrentProcess))
		{
			throw new XunitException($"Unexpected process call {method.Name}.");
		}

		long epoch = Epochs.Count > 1 ? Epochs.Dequeue() : Epochs.Count == 1 ? Epochs.Peek() : 1;
		return new ProcessSnapshot(new TargetProcessId(42), null, null, TargetBackend.LocalProcess,
			Bitness.Bytes == 4 ? CheatEngineArchitecture.X86 : CheatEngineArchitecture.X64, Bitness, Bitness.Bytes, null,
			epoch);
	}

	private bool Resolve(object?[] arguments)
	{
		string expression = ((SymbolExpression) arguments[0]!).Value;
		if (Symbols.TryGetValue(expression, out ulong symbol) || HexParse.TryAddress(expression, out symbol))
		{
			arguments[2] = new Address(symbol);
			arguments[3] = default(CheatEngineFailure);
			return true;
		}

		arguments[2] = default(Address);
		arguments[3] = new CheatEngineFailure(CheatEngineFailureKind.NotFound, "Inspection.ResolveAddress",
			$"'{expression}' is not a symbol.", hostEffect: CheatEngineHostEffect.Completed);
		return false;
	}

	private T Recorded<T>(string service, Func<Func<MethodInfo, object?[], object?>?> handler) where T : class
	{
		return Recorded<T>(service, (method, arguments) => handler() is { } configured
			? configured(method, arguments)
			: throw new XunitException($"Unexpected call {service}.{method.Name}."));
	}

	private T Recorded<T>(string service, Func<MethodInfo, object?[], object?> handler) where T : class
	{
		return ClientTestDouble.Create<T>((method, arguments) =>
		{
			string generic = method.IsGenericMethod
				? "<" + string.Join(",", method.GetGenericArguments().Select(static type => type.Name)) + ">"
				: string.Empty;
			Calls.Enqueue($"{service}.{method.Name}{generic}");
			object?[] values = arguments ?? [];
			if (service == "Inspection" && method.Name == nameof(IInspectionClient.TryResolveAddress))
			{
				return Resolve(values);
			}

			if (service == "Lua")
			{
				return ExecuteLua(method, values);
			}

			return handler(method, values);
		});
	}

	private object? ExecuteLua(MethodInfo method, object?[] arguments)
	{
		if (LuaResult is null)
		{
			throw new XunitException("Unexpected fixed Lua call.");
		}

		// The operation is LuaJsonOperation<T>; its result type is LuaJsonResult<T>.
		string source = (string) arguments[0]!.GetType().GetProperty("Source")!.GetValue(arguments[0])!;
		object? value = LuaResult(source);
		return value is LuaScriptError error
			? Activator.CreateInstance(method.ReturnType, null, error, 0)
			: Activator.CreateInstance(method.ReturnType, value, null, 0);
	}
}
