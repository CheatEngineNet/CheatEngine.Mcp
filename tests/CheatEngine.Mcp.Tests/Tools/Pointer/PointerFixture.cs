using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Globalization;
using System.Reflection;

using CheatEngine.Client;
using CheatEngine.Client.Dispatching;
using CheatEngine.Client.Inspection;
using CheatEngine.Client.Memory;
using CheatEngine.Client.Processes;
using CheatEngine.Client.Results;
using CheatEngine.Client.Scanning;
using CheatEngine.Mcp.Core.Jobs;
using CheatEngine.Mcp.Tests.Support;
using CheatEngine.Mcp.Tools.Pointer;
using CheatEngine.SDK.Engine.Enums;
using CheatEngine.SDK.Engine.Inspection;
using CheatEngine.SDK.Engine.Runtime;
using CheatEngine.SDK.Engine.Values;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using Xunit.Sdk;

namespace CheatEngine.Mcp.Tests.Tools.Pointer;

/// <summary>
///     One activation of the pointer tools over a Client double of a small x64 process: <c>game.exe</c> at
///     <see cref="ModuleBase" /> holds <c>[[game.exe+100]+10]+20</c> to an int32 at <see cref="Target" />, a heap region
///     holds the objects, a read-only region holds a pointer that writable captures skip, and a guard page must never
///     be read. The double counts every dispatch and Client call and throws on any other member, mutations included.
/// </summary>
internal sealed class PointerFixture : IAsyncDisposable
{
	internal const int ProcessId = 42;
	internal const ulong ModuleBase = 0x10000;
	internal const ulong ModuleSize = 0x1000;
	internal const ulong HeapBase = 0x20000;
	internal const ulong HeapSize = 0x10000;
	internal const ulong ReadOnlyBase = 0x40000;
	internal const ulong GuardBase = 0x50000;
	internal const ulong ObjectA = 0x20000;
	internal const ulong ObjectB = 0x21000;
	internal const ulong Target = 0x21020;
	internal const int TargetValue = 1234;

	private readonly ConcurrentQueue<string> _calls = [];
	private readonly SortedDictionary<ulong, Region> _regions = [];
	private readonly ServiceProvider _root;
	private readonly AsyncServiceScope _scope;
	private readonly CheatEngineArchitecture _architecture;
	private readonly PointerSize _pointerSize;
	private readonly int _pointerWidth;
	private int _dispatches;
	private long _epoch = 1;

	internal PointerFixture(McpExecutionOptions? options = null, ulong? largeRegionSize = null,
		PointerSize? pointerSize = null)
	{
		_pointerSize = pointerSize ?? PointerSize.Bit64;
		_pointerWidth = _pointerSize == PointerSize.Bit32 ? 4 : 8;
		_architecture = _pointerSize == PointerSize.Bit32 ? CheatEngineArchitecture.X86 : CheatEngineArchitecture.X64;
		AddRegion(ModuleBase, ModuleSize, MemoryProtection.ReadWrite);
		AddRegion(HeapBase, HeapSize, MemoryProtection.ReadWrite);
		AddRegion(ReadOnlyBase, 0x1000, MemoryProtection.ReadOnly);
		AddRegion(GuardBase, 0x1000, (MemoryProtection) 0x104);
		if (largeRegionSize is { } size)
		{
			AddRegion(LargeBase, size, MemoryProtection.ReadWrite);
		}

		// [[game.exe+100]+10]+20 reaches Target; game.exe+108 reaches it only with a negative offset.
		PutPointer(ModuleBase + 0x100, ObjectA);
		PutPointer(ModuleBase + 0x108, ObjectA + 0x40);
		PutPointer(ObjectA + 0x10, ObjectB);
		PutInt32(Target, TargetValue);
		PutPointer(ReadOnlyBase + 0x10, ObjectA);

		ICheatEngineDispatcher dispatcher = ClientTestDouble.Create<ICheatEngineDispatcher>((method, arguments) =>
		{
			if (method.Name == "get_IsMainThread")
			{
				// Tool calls and jobs never run on Cheat Engine's main thread, so a stop may wait for a job.
				return false;
			}

			if (method.Name.StartsWith("Invoke", StringComparison.Ordinal) && arguments?[0] is Delegate callback)
			{
				Interlocked.Increment(ref _dispatches);
				return callback.DynamicInvoke();
			}

			throw new NotSupportedException($"No dispatcher behavior was configured for {method.Name}.");
		});
		Client = ClientTestDouble.Client(dispatcher, Stopping.Token,
			(nameof(ICheatEngineClient.Processes), ClientTestDouble.Create<IProcessClient>(Processes)),
			(nameof(ICheatEngineClient.Inspection), ClientTestDouble.Create<IInspectionClient>(Inspection)),
			(nameof(ICheatEngineClient.Memory), ClientTestDouble.Create<IMemoryClient>(Memory)),
			(nameof(ICheatEngineClient.Patterns), ClientTestDouble.Create<IPatternScanner>(Patterns)),
			(nameof(ICheatEngineClient.ValueScans), ClientTestDouble.Create<IValueScanner>(ValueScans)));
		ServiceCollection services = new();
		services.AddSingleton(Client);
		services.AddLogging();
		services.AddSingleton<TimeProvider>(Clock);
		services.AddSingleton(Options.Create(options ?? new McpExecutionOptions()));
		new CheatEngineMcpBuilder(services, CheatEngineMcpMode.Backend).AddExecutionServices().AddPointerTools();
		services.AddOptions<CheatEngineMcpPrimitiveOptions>();
		_root = services.BuildServiceProvider(new ServiceProviderOptions
		{
			ValidateOnBuild = true,
			ValidateScopes = true
		});
		_scope = _root.CreateAsyncScope();
		Manifest = _root.GetRequiredService<IOptions<CheatEngineMcpPrimitiveOptions>>().Value;
		Targets = McpPrimitiveTargets.Resolve(_scope.ServiceProvider, Manifest);
	}

	/// <summary>The base of the optional large region, used to force a capture over several dispatches.</summary>
	internal static ulong LargeBase => 0x1000000;

	internal CancellationTokenSource Stopping
	{
		get;
	} = new();

	internal ManualClock Clock
	{
		get;
	} = new();

	internal ICheatEngineClient Client
	{
		get;
	}

	internal CheatEngineMcpPrimitiveOptions Manifest
	{
		get;
	}

	internal McpPrimitiveTargets Targets
	{
		get;
	}

	internal PointerChainTools Chain => (PointerChainTools) Targets.Get(typeof(PointerChainTools));

	internal PointerReferenceTools References => (PointerReferenceTools) Targets.Get(typeof(PointerReferenceTools));

	internal PointerMapTools Maps => (PointerMapTools) Targets.Get(typeof(PointerMapTools));

	internal PointerScanTools Scans => (PointerScanTools) Targets.Get(typeof(PointerScanTools));

	internal JobRegistry Jobs => _scope.ServiceProvider.GetRequiredService<JobRegistry>();

	internal PointerStore Store => _scope.ServiceProvider.GetRequiredService<PointerStore>();

	internal TargetResources Resources => _scope.ServiceProvider.GetRequiredService<TargetResources>();

	/// <summary>How many dispatches were requested.</summary>
	internal int Dispatches => Volatile.Read(ref _dispatches);

	/// <summary>Every Client member called, in order, as <c>Service.Member</c>.</summary>
	internal IReadOnlyCollection<string> Calls => _calls;

	/// <summary>Runs before every byte read with its address; it may block or change the target.</summary>
	internal Action<ulong>? BeforeRead
	{
		get;
		set;
	}

	/// <summary>Runs after every Client call is recorded, with its <c>Service.Member</c> name.</summary>
	internal Action<string>? OnCall
	{
		get;
		set;
	}

	/// <summary>Addresses from which every byte read fails, to model a partially unreadable region.</summary>
	internal ulong? UnreadableFrom
	{
		get;
		set;
	}

	/// <summary>The index of the address where the next primitive batch re-read fails, if any.</summary>
	internal int? PrimitiveBatchReadFailureIndex
	{
		get;
		set;
	}

	/// <summary>How many modules inspection returns, including the main module.</summary>
	internal int ModuleCount
	{
		get;
		set;
	} = 1;

	/// <summary>What releasing a temporary value scan answers.</summary>
	internal LeaseReleaseOutcome ReleaseOutcome
	{
		get;
		set;
	} = new(LeaseReleaseKind.Released, CheatEngineHostEffect.Completed);

	/// <summary>The last AOB scan requested.</summary>
	internal AobScanRequest? LastAobScan
	{
		get;
		private set;
	}

	/// <summary>The last value scan requested.</summary>
	internal ValueScanFirstRequest? LastValueScan
	{
		get;
		private set;
	}

	/// <summary>How many temporary value scans were released.</summary>
	internal int ValueScanReleases
	{
		get;
		private set;
	}

	public async ValueTask DisposeAsync()
	{
		await Stopping.CancelAsync();
		await _scope.DisposeAsync();
		await _root.DisposeAsync();
		Stopping.Dispose();
	}

	/// <summary>Selects another incarnation of the process, as a re-attach would.</summary>
	internal void ChangeTarget()
	{
		Interlocked.Increment(ref _epoch);
	}

	internal void PutPointer(ulong address, ulong value)
	{
		if (_pointerWidth == 8)
		{
			BinaryPrimitives.WriteUInt64LittleEndian(Span(address, 8), value);
		}
		else
		{
			BinaryPrimitives.WriteUInt32LittleEndian(Span(address, 4), checked((uint) value));
		}
	}

	internal void PutInt32(ulong address, int value)
	{
		BinaryPrimitives.WriteInt32LittleEndian(Span(address, 4), value);
	}

	/// <summary>Serves the pointer tools through a real MCP server with strict source-generated JSON.</summary>
	internal Task<TestMcpPipeline> StartPipelineAsync()
	{
		return TestMcpPipeline.StartAsync(Manifest, McpPrimitiveBinding.FromTargets(Targets), strictJson: true);
	}

	/// <summary>Waits until a map's capture ended.</summary>
	internal PointerMapInfo WaitForMap(string name)
	{
		PointerMapInfo? info = null;
		Assert.True(SpinWait.SpinUntil(() =>
			(info = Maps.ListMaps().Maps.Single(map => map.MapName == name)).State != PointerJobState.Running,
			TimeSpan.FromSeconds(30)), $"The capture of {name} did not end.");
		SpinWait.SpinUntil(() => Store.GetMap(name).Job?.Completion.IsCompleted == true, TimeSpan.FromSeconds(30));
		return info!;
	}

	/// <summary>Waits until a scan's search ended.</summary>
	internal PointerScanInfo WaitForScan(string name)
	{
		PointerScanInfo? info = null;
		Assert.True(SpinWait.SpinUntil(() =>
			(info = Scans.ListScans().Scans.Single(scan => scan.ScanName == name)).State != PointerJobState.Running,
			TimeSpan.FromSeconds(30)), $"The search {name} did not end.");
		SpinWait.SpinUntil(() => Store.GetScan(name).Job?.Completion.IsCompleted == true, TimeSpan.FromSeconds(30));
		return info!;
	}

	private void Record(string call)
	{
		_calls.Enqueue(call);
		OnCall?.Invoke(call);
	}

	private void AddRegion(ulong start, ulong size, MemoryProtection protection)
	{
		_regions[start] = new Region(start, new byte[size], protection);
	}

	private Span<byte> Span(ulong address, int length)
	{
		Region region = _regions.Values.Single(region => region.Contains(address));
		return region.Bytes.AsSpan((int) (address - region.Start), length);
	}

	private object? Processes(MethodInfo method, object?[]? arguments)
	{
		Record($"Processes.{method.Name}");
		return method.Name == nameof(IProcessClient.GetCurrentProcess)
			? new ProcessSnapshot(new TargetProcessId(ProcessId), "game.exe", null, TargetBackend.LocalProcess,
				_architecture, _pointerSize, _pointerWidth, null, Interlocked.Read(ref _epoch))
			: throw new XunitException($"Unexpected Processes.{method.Name}.");
	}

	private object? Inspection(MethodInfo method, object?[]? arguments)
	{
		Record($"Inspection.{method.Name}");
		return method.Name switch
		{
			nameof(IInspectionClient.GetMemoryRegions) => _regions.Values.Select(static region =>
					new MemoryRegionInfo(new Address(region.Start), new Address(region.Start), region.Protection,
						new MemorySize((ulong) region.Bytes.Length), MemoryRegionState.Committed, region.Protection,
						default, null))
				.ToImmutableArray(),
			nameof(IInspectionClient.GetModules) => Modules(),
			nameof(IInspectionClient.ResolveAddress) => new Address(Resolve(((SymbolExpression) arguments![0]!).Value)),
			_ => throw new XunitException($"Unexpected Inspection.{method.Name}.")
		};
	}

	private ImmutableArray<ModuleInfo> Modules()
	{
		ImmutableArray<ModuleInfo>.Builder modules = ImmutableArray.CreateBuilder<ModuleInfo>(ModuleCount);
		modules.Add(new ModuleInfo("game.exe", new Address(ModuleBase), new MemorySize(ModuleSize), true,
			@"C:\game\game.exe"));
		for (int index = 1; index < ModuleCount; index++)
		{
			modules.Add(new ModuleInfo($"module{index}.dll", new Address(0x1_0000_0000UL + ((ulong) index * 0x1000)),
				new MemorySize(0x1000), false, $@"C:\game\module{index}.dll"));
		}

		return modules.MoveToImmutable();
	}

	private object? Memory(MethodInfo method, object?[]? arguments)
	{
		Record($"Memory.{method.Name}");
		switch (method.Name)
		{
			case nameof(IMemoryClient.ReadBytesDetailed):
				{
					MemoryBytesReadRequest request = (MemoryBytesReadRequest) arguments![0]!;
					BeforeRead?.Invoke(request.Address.ToUInt64());
					if (((CancellationToken) arguments[1]!).IsCancellationRequested)
					{
						return new MemoryBytesReadOutcome(request.Length, [],
							new CheatEngineFailure(CheatEngineFailureKind.Cancelled, "Memory.ReadBytes", "cancelled",
								hostEffect: CheatEngineHostEffect.NotStarted));
					}

					byte[] bytes = Read(request.Address.ToUInt64(), request.Length);
					return new MemoryBytesReadOutcome(request.Length, [.. bytes],
						bytes.Length == request.Length ? null : Unreadable("Memory.ReadBytes"));
				}
			case nameof(IMemoryClient.TryReadPrimitive) when method.GetGenericArguments()[0] == typeof(Address):
				{
					byte[] bytes = Read(((Address) arguments![0]!).ToUInt64(), _pointerWidth);
					arguments[1] = bytes.Length == _pointerWidth
						? new Address(_pointerWidth == 8
							? BinaryPrimitives.ReadUInt64LittleEndian(bytes)
							: BinaryPrimitives.ReadUInt32LittleEndian(bytes))
						: default;
					arguments[2] = bytes.Length == _pointerWidth ? default : Unreadable("Memory.ReadPrimitive");
					return bytes.Length == _pointerWidth;
				}
			case nameof(IMemoryClient.ReadPrimitive) when method.GetGenericArguments()[0] == typeof(int):
				{
					byte[] bytes = Read(((Address) arguments![0]!).ToUInt64(), 4);
					return bytes.Length == 4
						? BinaryPrimitives.ReadInt32LittleEndian(bytes)
						: throw Unreadable("Memory.ReadPrimitive").ToException();
				}
			case nameof(IMemoryClient.TryResolvePointerChain):
				{
					PointerChainRequest request = (PointerChainRequest) arguments![0]!;
					ulong address = request.BaseAddress.ToUInt64();
					foreach (long offset in request.Offsets)
					{
						byte[] bytes = Read(address, _pointerWidth);
						if (bytes.Length != _pointerWidth)
						{
							arguments[1] = default(Address);
							arguments[2] = Unreadable("Memory.ResolvePointerChain");
							return false;
						}

						ulong pointer = _pointerWidth == 8
							? BinaryPrimitives.ReadUInt64LittleEndian(bytes)
							: BinaryPrimitives.ReadUInt32LittleEndian(bytes);
						address = unchecked(pointer + (ulong) offset);
					}

					arguments[1] = new Address(address);
					arguments[2] = default(CheatEngineFailure);
					return true;
				}
			case nameof(IMemoryClient.ReadPrimitiveBatchDetailed) when method.GetGenericArguments()[0] == typeof(Address):
				{
					MemoryPrimitiveBatchReadRequest<Address> request = (MemoryPrimitiveBatchReadRequest<Address>) arguments![0]!;
					int completed = Math.Min(PrimitiveBatchReadFailureIndex ?? request.Addresses.Length,
						request.Addresses.Length);
					Address[] values =
					[
						.. request.Addresses.Take(completed).Select(address =>
						new Address(_pointerWidth == 8
							? BinaryPrimitives.ReadUInt64LittleEndian(Read(address.ToUInt64(), _pointerWidth))
							: BinaryPrimitives.ReadUInt32LittleEndian(Read(address.ToUInt64(), _pointerWidth))))
					];
					return PrimitiveBatchReadFailureIndex is { } failed && failed < request.Addresses.Length
						? new MemoryPrimitiveBatchReadOutcome<Address>(request.Addresses.Length, values, failed,
							Unreadable("Memory.ReadPrimitiveBatch"))
						: new MemoryPrimitiveBatchReadOutcome<Address>(values.Length, values, null, null);
				}
			default:
				throw new XunitException($"Unexpected Memory.{method.Name}.");
		}
	}

	private object? Patterns(MethodInfo method, object?[]? arguments)
	{
		Record($"Patterns.{method.Name}");
		if (method.Name != nameof(IPatternScanner.ScanDetailed))
		{
			throw new XunitException($"Unexpected Patterns.{method.Name}.");
		}

		AobScanRequest request = (AobScanRequest) arguments![0]!;
		LastAobScan = request;
		byte[] pattern = [.. request.Pattern.Value.Split(' ').Select(static token => byte.Parse(token, NumberStyles.HexNumber, CultureInfo.InvariantCulture))];
		ulong first = request.Range?.Start.ToUInt64() ?? ModuleBase;
		ulong last = request.Range?.End.ToUInt64() ?? (ModuleBase + ModuleSize - 1);
		Address[] matches =
		[
			.. Aligned(request.Protection.Writable is ScanProtectionRequirement.Required, first, last, pattern.Length)
				.Where(address => Read(address, pattern.Length).AsSpan().SequenceEqual(pattern))
				.Take(request.MaximumResults).Select(static address => new Address(address))
		];
		ulong count = (ulong) matches.Length;
		return new PatternScanOutcome(new AobScanResult([.. matches], false), null,
			new PatternScanMetrics(PatternScanScope.HostBoundedRange, count, count, 0, matches.Length, 0, 0, 0, true,
				TimeSpan.Zero, TimeSpan.Zero),
			matches.Length == 0 ? PatternScanHostOutcomeKind.NoMatches : PatternScanHostOutcomeKind.Matches,
			PatternScanRouteReason.ScopedRequestOnQualifiedTarget, true);
	}

	private object? ValueScans(MethodInfo method, object?[]? arguments)
	{
		Record($"ValueScans.{method.Name}");
		if (method.Name != nameof(IValueScanner.CreateSession))
		{
			throw new XunitException($"Unexpected ValueScans.{method.Name}.");
		}

		ValueScanFirstRequest? request = null;
		bool released = false;
		return ClientTestDouble.Create<IValueScanSession>((member, values) =>
		{
			Record($"ValueScan.{member.Name}");
			switch (member.Name)
			{
				case nameof(IValueScanSession.FirstScan):
					request = (ValueScanFirstRequest) values![0]!;
					LastValueScan = request;
					return null;
				case nameof(IValueScanSession.Read):
					{
						ValueScanReadRequest read = (ValueScanReadRequest) values![0]!;
						ValueScanFirstRequest first = request!.Value;
						ulong low = ulong.Parse(first.Value!.Value.Text!, CultureInfo.InvariantCulture);
						ulong high = ulong.Parse(first.UpperValue!.Value.Text!, CultureInfo.InvariantCulture);
						ulong[] holders =
						[
							.. Aligned(first.Protection.Writable is ScanProtectionRequirement.Required,
							first.StartAddress.ToUInt64(), first.StopAddress.ToUInt64() - 1, 8).Where(address =>
						{
							ulong value = BinaryPrimitives.ReadUInt64LittleEndian(Read(address, 8));
							return value >= low && value <= high;
						})
						];
						return new ValueScanPage(0, (ulong) holders.Length,
							[.. holders.Take(read.MaximumCount).Select(static holder => new ValueScanMatch(new Address(holder), "0"))]);
					}
				case nameof(ICheatEngineLease.Release):
					ValueScanReleases++;
					released = ReleaseOutcome.IsComplete || !ReleaseOutcome.IsRetryable;
					return ReleaseOutcome;
				case "get_IsReleased":
					return released;
				case "get_LastReleaseOutcome":
					return released ? ReleaseOutcome : null;
				case "get_RequiresManualRecovery":
					return ReleaseOutcome.RequiresManualRecovery;
				case "get_SelectionEpoch":
					return Interlocked.Read(ref _epoch);
				default:
					throw new XunitException($"Unexpected ValueScan.{member.Name}.");
			}
		});
	}

	private IEnumerable<ulong> Aligned(bool writableOnly, ulong first, ulong last, int length)
	{
		foreach (Region region in _regions.Values)
		{
			bool writable = region.Protection is MemoryProtection.ReadWrite;
			if ((writableOnly && !writable) || (uint) region.Protection == 0x104)
			{
				continue;
			}

			for (ulong address = (Math.Max(first, region.Start) + 3) & ~3UL;
				 address <= Math.Min(last, region.Start + (ulong) region.Bytes.Length - (ulong) length);
				 address += 4)
			{
				yield return address;
			}
		}
	}

	private byte[] Read(ulong address, int length)
	{
		Region? region = _regions.Values.FirstOrDefault(region => region.Contains(address));
		if (region is null)
		{
			return [];
		}

		if ((uint) region.Protection == 0x104)
		{
			throw new XunitException($"The guard page at {address:X} was read.");
		}

		ulong end = Math.Min(address + (ulong) length, region.Start + (ulong) region.Bytes.Length);
		if (UnreadableFrom is { } unreadable && unreadable >= address && unreadable < end)
		{
			end = unreadable;
		}
		else if (UnreadableFrom is { } from && from <= address && from >= region.Start)
		{
			return [];
		}

		return region.Bytes.AsSpan((int) (address - region.Start), (int) (end - address)).ToArray();
	}

	private static ulong Resolve(string expression)
	{
		if (expression.StartsWith("game.exe+", StringComparison.OrdinalIgnoreCase))
		{
			return ModuleBase + ulong.Parse(expression["game.exe+".Length..], NumberStyles.HexNumber,
				CultureInfo.InvariantCulture);
		}

		string digits = expression.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? expression[2..] : expression;
		return ulong.TryParse(digits, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ulong address)
			? address
			: throw new CheatEngineFailure(CheatEngineFailureKind.NotFound, "Inspection.ResolveAddress",
				$"No symbol named {expression}.", hostEffect: CheatEngineHostEffect.NotStarted).ToException();
	}

	private static CheatEngineFailure Unreadable(string operation)
	{
		return new CheatEngineFailure(CheatEngineFailureKind.MemoryReadFailed, operation, "unreadable",
			hostEffect: CheatEngineHostEffect.NotApplied);
	}

	private sealed record Region(ulong Start, byte[] Bytes, MemoryProtection Protection)
	{
		internal bool Contains(ulong address)
		{
			return address >= Start && address - Start < (ulong) Bytes.Length;
		}
	}
}
