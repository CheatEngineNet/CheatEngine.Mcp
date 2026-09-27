using System.Collections.Immutable;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

using CheatEngine.Client;
using CheatEngine.Client.Inspection;
using CheatEngine.Client.Lua;
using CheatEngine.Client.Memory;
using CheatEngine.Client.Results;
using CheatEngine.Mcp.Core.Features;
using CheatEngine.Mcp.Core.Values;
using CheatEngine.Mcp.Tests.Support;
using CheatEngine.Mcp.Tools.Structures;
using CheatEngine.SDK.Engine.Inspection;
using CheatEngine.SDK.Engine.Values;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace CheatEngine.Mcp.Tests.Tools.Structures;

/// <summary>One fixed script the structure tools dispatched: its body is recognized by the end of its source.</summary>
/// <param name="Operation">The operation name.</param>
/// <param name="Source">The complete script, whose first line holds the encoded <c>a</c> table.</param>
internal sealed record StructureLuaCall(string Operation, string Source)
{
	internal bool Runs(string body)
	{
		return Source.EndsWith(body, StringComparison.Ordinal);
	}

	/// <summary>The first line: runtime values, the encoded arguments and the prelude.</summary>
	internal string Arguments => Source[..Source.IndexOf('\n', StringComparison.Ordinal)];
}

/// <summary>
///     A Client double for the structure tools: scripted Lua results, a flat fake memory, hexadecimal address
///     resolution and a dispatcher that counts admissions. Any other Client call fails the test.
/// </summary>
internal sealed class StructureToolHarness
{
	private readonly Dictionary<ulong, byte> _memory = [];

	internal StructureToolHarness()
	{
		ILuaClient lua = ClientTestDouble.Create<ILuaClient>((method, arguments) =>
		{
			Assert.Equal(nameof(ILuaClient.Execute), method.Name);
			object operation = arguments![0]!;
			Type type = operation.GetType();
			string name = (string) type.GetProperty("Operation")!.GetValue(operation)!;
			string source = (string) type.GetProperty("Source")!.GetValue(operation)!;
			JsonTypeInfo resultType = (JsonTypeInfo) type.GetProperty("ResultType")!.GetValue(operation)!;
			StructureLuaCall call = new(name, source);
			LuaCalls.Add(call);
			if (NativeLua is not null)
			{
				return NativeLua(method, operation);
			}

			Type resultShape = typeof(LuaJsonResult<>).MakeGenericType(resultType.Type);
			object reply = Lua?.Invoke(call) ??
						   throw new InvalidOperationException($"No Lua result was scripted for {name}.");
			return reply is LuaScriptError error
				? Activator.CreateInstance(resultShape, null, error, 0)
				: Activator.CreateInstance(resultShape,
					JsonSerializer.Deserialize((string) reply, resultType), null, 0);
		});
		IMemoryClient memory = ClientTestDouble.Create<IMemoryClient>((method, arguments) =>
		{
			switch (method.Name)
			{
				case nameof(IMemoryClient.ReadBytesDetailed):
					MemoryBytesReadRequest read = (MemoryBytesReadRequest) arguments![0]!;
					Reads.Add((read.Address.ToUInt64(), read.Length));
					return Read(read.Address.ToUInt64(), read.Length);
				case nameof(IMemoryClient.WriteBytes):
					MemoryBytesWriteRequest write = (MemoryBytesWriteRequest) arguments![0]!;
					byte[] bytes = write.Bytes.ToArray();
					Writes.Add((write.Address.ToUInt64(), bytes));
					Map(write.Address.ToUInt64(), bytes);
					return null;
				default:
					throw new NotSupportedException($"The structure tools must not call Memory.{method.Name}.");
			}
		});
		IInspectionClient inspection = ClientTestDouble.Create<IInspectionClient>((method, arguments) =>
		{
			Assert.Equal(nameof(IInspectionClient.TryResolveAddress), method.Name);
			string expression = ((SymbolExpression) arguments![0]!).Value;
			Resolutions.Add(expression);
			if (Symbols.TryGetValue(expression, out ulong symbol) || HexParse.TryAddress(expression, out symbol))
			{
				arguments[2] = new Address(symbol);
				arguments[3] = default(CheatEngineFailure);
				return true;
			}

			arguments[2] = default(Address);
			arguments[3] = new CheatEngineFailure(CheatEngineFailureKind.NotFound, "Inspection.ResolveAddress",
				"The expression does not resolve.", null, CheatEngineHostEffect.NotStarted);
			return false;
		});
		Client = ClientTestDouble.Client(Dispatcher.Dispatcher, CancellationToken.None,
			(nameof(ICheatEngineClient.Lua), lua), (nameof(ICheatEngineClient.Memory), memory),
			(nameof(ICheatEngineClient.Inspection), inspection));
		IOptions<McpExecutionOptions> execution = Options.Create(new McpExecutionOptions());
		Dispatch = new ToolDispatch(Client, new McpFeatureGate(Options.Create(new McpFeatureOptions())), execution,
			new DispatchStatistics(execution), TimeProvider.System, new RecordingLogger<ToolDispatch>());
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

	/// <summary>Answers each fixed script with its JSON result or a declared <see cref="LuaScriptError" />.</summary>
	internal Func<StructureLuaCall, object?>? Lua
	{
		get;
		set;
	}

	/// <summary>Runs each fixed script on a real Lua state instead; receives the Client's Execute method and the operation.</summary>
	internal Func<MethodInfo, object, object?>? NativeLua
	{
		get;
		set;
	}

	internal List<StructureLuaCall> LuaCalls
	{
		get;
	} = [];

	internal List<(ulong Address, int Length)> Reads
	{
		get;
	} = [];

	internal List<(ulong Address, byte[] Bytes)> Writes
	{
		get;
	} = [];

	internal List<string> Resolutions
	{
		get;
	} = [];

	internal Dictionary<string, ulong> Symbols
	{
		get;
	} = new(StringComparer.Ordinal);

	internal StructureTools Structures => new(Dispatch);

	internal StructureElementTools Elements => new(Dispatch);

	internal StructureValueTools Values => new(Dispatch);

	internal StructureCompareTools Comparisons => new(Dispatch);

	/// <summary>Places bytes in the fake memory.</summary>
	internal void Map(ulong address, params byte[] bytes)
	{
		for (int index = 0; index < bytes.Length; index++)
		{
			_memory[address + (ulong) index] = bytes[index];
		}
	}

	/// <summary>Declares a script failure as <c>mcp.err</c> would.</summary>
	internal static LuaScriptError Declared(string kind, string hostEffect = "not_started")
	{
		return new LuaScriptError(kind, "declared by the script", hostEffect, "hint");
	}

	/// <summary>Composes the structure tools as an activation, for calls through a real MCP pipeline.</summary>
	internal async Task<(ServiceProvider Root, AsyncServiceScope Scope, TestMcpPipeline Pipeline)> ServeAsync()
	{
		ServiceCollection activation = new();
		activation.AddSingleton(Client);
		activation.AddLogging();
		new CheatEngineMcpBuilder(activation, CheatEngineMcpMode.Backend).AddExecutionServices().AddStructureTools();
		activation.AddOptions<CheatEngineMcpPrimitiveOptions>();
		ServiceProvider root = activation.BuildServiceProvider(new ServiceProviderOptions
		{
			ValidateOnBuild = true,
			ValidateScopes = true
		});
		AsyncServiceScope scope = root.CreateAsyncScope();
		CheatEngineMcpPrimitiveOptions manifest =
			root.GetRequiredService<IOptions<CheatEngineMcpPrimitiveOptions>>().Value;
		McpPrimitiveTargets targets = McpPrimitiveTargets.Resolve(scope.ServiceProvider, manifest);
		TestMcpPipeline pipeline =
			await TestMcpPipeline.StartAsync(manifest, McpPrimitiveBinding.FromTargets(targets));
		return (root, scope, pipeline);
	}

	private MemoryBytesReadOutcome Read(ulong address, int length)
	{
		List<byte> confirmed = [];
		for (int index = 0; index < length && _memory.TryGetValue(address + (ulong) index, out byte value); index++)
		{
			confirmed.Add(value);
		}

		return confirmed.Count == length
			? new MemoryBytesReadOutcome(length, [.. confirmed], null)
			: new MemoryBytesReadOutcome(length, [.. confirmed],
				new CheatEngineFailure(CheatEngineFailureKind.MemoryReadFailed, "Memory.ReadBytesDetailed",
					"Part of the range is unreadable.", null, CheatEngineHostEffect.Completed));
	}

	/// <summary>The JSON a fixed script returns for one element.</summary>
	internal static string Element(int index, long offset, string name, int vartype, string? display, int byteSize,
		string? child = null)
	{
		string displayJson = display is null ? "null" : $"\"{display}\"";
		string childJson = child is null ? string.Empty : $",\"childStructure\":\"{child}\",\"childStructureStart\":8";
		return
			$"{{\"index\":{index},\"offset\":{offset},\"name\":\"{name}\",\"vartype\":{vartype},\"display\":{displayJson},\"byteSize\":{byteSize}{childJson}}}";
	}

	/// <summary>The JSON of a structure page as the element script returns it.</summary>
	internal static string Definition(string name, int total, params string[] elements)
	{
		return
			$"{{\"name\":\"{name}\",\"size\":64,\"elementCount\":{total},\"internal\":false,\"elements\":[{string.Join(',', elements)}],\"total\":{total}}}";
	}

	internal static ImmutableArray<byte> Bytes(params byte[] bytes)
	{
		return [.. bytes];
	}

	internal static IEnumerable<FieldInfo> ScriptFields()
	{
		return typeof(StructureLuaScripts).GetFields(BindingFlags.Static | BindingFlags.NonPublic)
			.Where(static field => field is { IsLiteral: true, FieldType.Name: nameof(String) });
	}
}
