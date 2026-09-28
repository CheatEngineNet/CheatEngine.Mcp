using System.Reflection;

using CheatEngine.Client;
using CheatEngine.Client.Assembly;
using CheatEngine.Client.Inspection;
using CheatEngine.Client.Processes;
using CheatEngine.Client.Results;
using CheatEngine.Mcp.Core.Features;
using CheatEngine.Mcp.Core.Values;
using CheatEngine.Mcp.Tests.Support;
using CheatEngine.SDK.Engine.Inspection;
using CheatEngine.SDK.Engine.Runtime;
using CheatEngine.SDK.Engine.Values;

using Microsoft.Extensions.Options;

using Xunit.Sdk;

namespace CheatEngine.Mcp.Tests.Tools.Code;

/// <summary>
///     A Client double for the function graph: a byte map of emitted instructions that the assembly double decodes, a
///     name table, and the selected process with a scripted selection epoch. Any unmapped address fails to decode with
///     <c>MemoryReadFailed</c>, like unreadable memory.
/// </summary>
internal sealed class CodeGraphTarget
{
	private readonly Dictionary<ulong, (byte[] Bytes, string Opcode)> _code = [];

	internal CodeGraphTarget()
	{
		Client = ClientTestDouble.Client(Dispatcher.Dispatcher, CancellationToken.None,
			(nameof(ICheatEngineClient.Inspection), ClientTestDouble.Create<IInspectionClient>(Inspect)),
			(nameof(ICheatEngineClient.Processes), ClientTestDouble.Create<IProcessClient>(Process)),
			(nameof(ICheatEngineClient.Assembly), ClientTestDouble.Create<IAssemblyClient>(Disassemble)));
		IOptions<McpExecutionOptions> execution = Options.Create(new McpExecutionOptions());
		Dispatch = new ToolDispatch(Client, new McpFeatureGate(Options.Create(new McpFeatureOptions())), execution,
			new DispatchStatistics(execution), TimeProvider.System, new RecordingLogger<ToolDispatch>());
	}

	internal ICheatEngineClient Client
	{
		get;
	}

	/// <summary>The dispatcher, which counts every dispatch.</summary>
	internal RecordingDispatcher Dispatcher
	{
		get;
	} = new();

	internal ToolDispatch Dispatch
	{
		get;
	}

	/// <summary>The instruction set the process double reports.</summary>
	internal CheatEngineArchitecture Architecture
	{
		get;
		set;
	} = CheatEngineArchitecture.X64;

	/// <summary>The bitness the process double reports; by default the one of <see cref="Architecture" />.</summary>
	internal PointerSize? Bitness
	{
		get;
		set;
	}

	/// <summary>How the process double says Cheat Engine reaches the target.</summary>
	internal TargetBackend Backend
	{
		get;
		set;
	} = TargetBackend.LocalProcess;

	/// <summary>The symbol names the inspection double returns; any other address is named by its hexadecimal text.</summary>
	internal Dictionary<ulong, string> Names
	{
		get;
	} = [];

	/// <summary>The target-selection epoch of each successive process observation; the last one repeats.</summary>
	internal Queue<long> Epochs
	{
		get;
	} = [];

	/// <summary>Every address the assembly double was asked to decode, in order.</summary>
	internal List<ulong> Decoded
	{
		get;
	} = [];

	/// <summary>Every address the inspection double was asked to name, in order.</summary>
	internal List<ulong> Named
	{
		get;
	} = [];

	/// <summary>Emits one instruction into the byte map.</summary>
	/// <param name="address">The instruction's address.</param>
	/// <param name="opcode">Its display text.</param>
	/// <param name="bytes">Its exact bytes.</param>
	/// <returns>The address that follows it.</returns>
	internal ulong Emit(ulong address, string opcode, params byte[] bytes)
	{
		_code[address] = (bytes, opcode);
		return address + (ulong) bytes.Length;
	}

	private object? Disassemble(MethodInfo method, object?[]? arguments)
	{
		if (method.Name != nameof(IAssemblyClient.TryDisassemble))
		{
			throw new XunitException($"Unexpected assembly call {method.Name}.");
		}

		ulong address = ((Address) arguments![0]!).ToUInt64();
		Decoded.Add(address);
		if (_code.TryGetValue(address, out (byte[] Bytes, string Opcode) code))
		{
			arguments[1] = new AssemblyInstructionSnapshot(new Address(address), code.Bytes.Length,
				HexFormat.Address(address), code.Opcode, string.Empty, code.Bytes);
			arguments[2] = default(CheatEngineFailure);
			return true;
		}

		arguments[1] = default(AssemblyInstructionSnapshot);
		arguments[2] = new CheatEngineFailure(CheatEngineFailureKind.MemoryReadFailed, "Assembly.Disassemble",
			"The instruction bytes could not be read.", hostEffect: CheatEngineHostEffect.Completed);
		return false;
	}

	private object? Inspect(MethodInfo method, object?[]? arguments)
	{
		object?[] values = arguments!;
		switch (method.Name)
		{
			case nameof(IInspectionClient.TryResolveAddress):
				string expression = ((SymbolExpression) values[0]!).Value;
				if (HexParse.TryAddress(expression, out ulong resolved))
				{
					values[2] = new Address(resolved);
					values[3] = default(CheatEngineFailure);
					return true;
				}

				values[2] = default(Address);
				values[3] = new CheatEngineFailure(CheatEngineFailureKind.NotFound, "Inspection.ResolveAddress",
					$"'{expression}' is not a symbol.", hostEffect: CheatEngineHostEffect.Completed);
				return false;
			case nameof(IInspectionClient.TryResolveName):
				ulong address = ((Address) values[0]!).ToUInt64();
				Named.Add(address);
				values[1] = Names.TryGetValue(address, out string? name) ? name : HexFormat.Address(address);
				values[2] = default(CheatEngineFailure);
				return true;
			default:
				throw new XunitException($"Unexpected inspection call {method.Name}.");
		}
	}

	private object? Process(MethodInfo method, object?[]? arguments)
	{
		if (method.Name != nameof(IProcessClient.GetCurrentProcess))
		{
			throw new XunitException($"Unexpected process call {method.Name}.");
		}

		long epoch = Epochs.Count > 1 ? Epochs.Dequeue() : Epochs.Count == 1 ? Epochs.Peek() : 1;
		PointerSize bitness = Bitness ?? Architecture switch
		{
			CheatEngineArchitecture.X86 or CheatEngineArchitecture.Arm32 => PointerSize.Bit32,
			CheatEngineArchitecture.Unknown => PointerSize.Unknown,
			_ => PointerSize.Bit64
		};
		return new ProcessSnapshot(new TargetProcessId(42), null, null, Backend, Architecture,
			bitness, bitness.IsKnown ? bitness.Bytes : null, null, epoch);
	}
}
