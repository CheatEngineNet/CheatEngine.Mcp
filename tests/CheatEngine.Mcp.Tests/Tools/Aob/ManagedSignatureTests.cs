using System.Collections.Immutable;
using System.Globalization;
using System.Reflection;

using CheatEngine.Client;
using CheatEngine.Client.Assembly;
using CheatEngine.Client.Inspection;
using CheatEngine.Client.Processes;
using CheatEngine.Client.Results;
using CheatEngine.Client.Scanning;
using CheatEngine.Mcp.Core.Contract;
using CheatEngine.Mcp.Core.Features;
using CheatEngine.Mcp.Core.Values;
using CheatEngine.Mcp.Tests.Support;
using CheatEngine.Mcp.Tools.Aob;
using CheatEngine.SDK.Engine.Inspection;
using CheatEngine.SDK.Engine.Runtime;
using CheatEngine.SDK.Engine.Values;

using Microsoft.Extensions.Options;

using Xunit.Sdk;

namespace CheatEngine.Mcp.Tests.Tools.Aob;

/// <summary>
///     <c>aob_generate_signature</c> in a module over 64 MiB: the managed generator decodes whole instructions forward
///     from the address, masks their position-dependent bytes and grows the pattern until a module scan with limit 2
///     proves it unique, without Cheat Engine's <c>getUniqueAOB</c>.
/// </summary>
public sealed class ManagedSignatureTests
{
	private const ulong Base = 0x10000000;
	private const ulong ImageSize = 80UL * 1024 * 1024;

	private static CancellationToken Token => TestContext.Current.CancellationToken;

	[Fact]
	public void UniqueAfterTheFirstScan_ReturnsAVerifiedPatternAtTheAddress()
	{
		CodeImage image = new();
		image.Add(Base + 0x100, "48 8B 05 10 20 30 00", "48 89 43 10", "C3");
		image.Add(Base + 0x400, "48 8B 05 AA BB CC 00", "48 89 43 18", "C3");

		AobSignature signature = image.Generate("10000100");

		Assert.Equal(
			new AobSignature("10000100", "huge.dll", AobSignatureGenerator.Managed, true, true,
				"48 8B 05 ?? ?? ?? ?? 48 89 43 10", "10000100", 0, 11, 1), signature);
		Assert.Equal(1, image.Scans);
	}

	[Fact]
	public void SharedPrefix_GrowsByWholeInstructionsUntilTheScanIsUnique()
	{
		CodeImage image = new();
		image.Add(Base + 0x100, "48 8B 05 10 20 30 00", "48 89 43 10", "E8 00 01 00 00", "84 C0", "74 05",
			"48 8B CB", "C3");
		image.Add(Base + 0x400, "48 8B 05 AA BB CC 00", "48 89 43 10", "E8 00 02 00 00", "84 C0", "75 05",
			"48 8B CB", "C3");

		AobSignature signature = image.Generate("10000100");

		Assert.Equal("48 8B 05 ?? ?? ?? ?? 48 89 43 10 E8 ?? ?? ?? ?? 84 C0 74 05 48 8B CB C3", signature.Pattern);
		Assert.Equal((true, true, 24, 0, 1), (signature.Unique, signature.Verified, signature.Length,
			signature.Offset, signature.MatchCount));
		Assert.Equal(3, image.Scans);
	}

	[Fact]
	public void MatchesBeforeTheAddress_ACutListIsNotUniqueAndGenerationContinues()
	{
		CodeImage image = new();
		image.Add(Base + 0x100, "48 8B 05 AA BB CC 00", "48 89 43 10", "E8 00 02 00 00", "75 05", "C3");
		image.Add(Base + 0x200, "48 8B 05 AA BB CC 00", "48 89 43 10", "E8 00 02 00 00", "75 05", "C3");
		image.Add(Base + 0x1000, "48 8B 05 10 20 30 00", "48 89 43 10", "E8 00 01 00 00", "74 05", "C3");

		AobSignature signature = image.Generate("10001000");

		Assert.Equal("48 8B 05 ?? ?? ?? ?? 48 89 43 10 E8 ?? ?? ?? ?? 74 05 C3", signature.Pattern);
		Assert.True(signature.Unique);
		Assert.Equal(3, image.Scans);
	}

	[Fact]
	public void NeverUniqueWithin64Bytes_ReportsTheLongestPatternAfterSixScans()
	{
		CodeImage image = new();
		string[] repeated = [.. Enumerable.Repeat("48 89 43 10", 20)];
		image.Add(Base + 0x100, repeated);
		image.Add(Base + 0x1000, repeated);

		AobSignature signature = image.Generate("10000100");

		Assert.Equal(
			new AobSignature("10000100", "huge.dll", AobSignatureGenerator.Managed, false, false,
				TriedPattern: string.Join(' ', Enumerable.Repeat("48 89 43 10", 16))), signature);
		Assert.Equal(6, image.Scans);
	}

	[Fact]
	public void CodeEndsBeforeAUniquePattern_StopsWhenThePatternCannotGrow()
	{
		CodeImage image = new();
		image.Add(Base + 0x100, "48 8B 05 10 20 30 00", "48 89 43 10", "C3");
		image.Add(Base + 0x400, "48 8B 05 AA BB CC 00", "48 89 43 10", "C3");

		AobSignature signature = image.Generate("10000100");

		// Both runs end after 12 bytes that match alike, so the second scan is the last one.
		Assert.Equal((false, false, "48 8B 05 ?? ?? ?? ?? 48 89 43 10 C3"),
			(signature.Unique, signature.Verified, signature.TriedPattern));
		Assert.Equal(2, image.Scans);
	}

	[Fact]
	public void InstructionPastTheModuleEnd_EndsThePattern()
	{
		CodeImage image = new();
		ulong start = Base + ImageSize - 7;
		image.Add(start, "48 8B 05 10 20 30 00", "48 89 43 10");

		AobSignature signature = image.Generate(start.ToString("X", CultureInfo.InvariantCulture));

		Assert.Equal(("48 8B 05 ?? ?? ?? ??", 7, true), (signature.Pattern, signature.Length, signature.Unique));
		Assert.Equal(1, image.Scans);
	}

	[Fact]
	public void Target32Bit_MasksAbsoluteAddresses()
	{
		CodeImage image = new(false);
		image.Add(Base + 0x100, "A1 00 10 40 00", "8B 0D 00 20 40 00", "C3");

		AobSignature signature = image.Generate("10000100");

		Assert.Equal("A1 ?? ?? ?? ?? 8B 0D ?? ?? ?? ??", signature.Pattern);
	}

	[Fact]
	public void VerifyFalse_StillVerifiesAManagedSignature()
	{
		CodeImage image = new();
		image.Add(Base + 0x100, "48 8B 05 10 20 30 00", "48 89 43 10", "C3");

		AobSignature signature = new AobTools(image.Dispatch).GenerateSignature("10000100", verify: false,
			cancellationToken: Token);

		Assert.Equal((AobSignatureGenerator.Managed, true, true), (signature.Generator, signature.Unique,
			signature.Verified));
		Assert.Equal(1, image.Scans);
	}

	[Fact]
	public void CompleteListWithoutTheAddress_IsAHostRefusal()
	{
		CodeImage image = new();
		image.Add(Base + 0x100, true, "48 8B 05 10 20 30 00", "48 89 43 10", "C3");
		image.Add(Base + 0x400, "48 8B 05 AA BB CC 00", "48 89 43 10", "C3");

		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() =>
			image.Generate("10000100"));

		Assert.Equal((ToolErrorKind.HostRefused, ToolHostEffect.Completed),
			(exception.Error.Kind, exception.Error.HostEffect));
		Assert.Contains("did not find the bytes just read at 10000100", exception.Error.Message,
			StringComparison.Ordinal);
	}

	[Fact]
	public void UnreadableAddress_IsTheClientFailureBeforeAnyScan()
	{
		CodeImage image = new();

		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() =>
			image.Generate("10000100"));

		Assert.NotEqual(ToolErrorKind.Internal, exception.Error.Kind);
		Assert.Equal(0, image.Scans);
	}

	[Fact]
	public void ScanNotBoundedToTheModule_IsTargetChanged()
	{
		CodeImage image = new()
		{
			Scope = PatternScanScope.GlobalHostScanWithManagedFilter
		};
		image.Add(Base + 0x100, "48 8B 05 10 20 30 00", "48 89 43 10", "C3");

		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() =>
			image.Generate("10000100"));

		Assert.Equal(ToolErrorKind.TargetChanged, exception.Error.Kind);
	}

	/// <summary>
	///     A module over 64 MiB whose code is a few instruction runs: the assembly double decodes them and the pattern
	///     double scans them (a hidden run decodes but is never found). The dispatch has no fixed Lua executor, so any
	///     Lua call of the managed generator would fail the test.
	/// </summary>
	private sealed class CodeImage
	{
		private readonly bool _is64;
		private readonly List<(ulong Start, bool Hidden, byte[][] Instructions)> _runs = [];

		internal CodeImage(bool is64 = true)
		{
			_is64 = is64;
			ModuleInfo module = new("huge.dll", new Address(Base), new MemorySize(ImageSize), true,
				@"C:\game\huge.dll");
			ICheatEngineClient client = ClientTestDouble.Client(new RecordingDispatcher().Dispatcher,
				CancellationToken.None,
				(nameof(ICheatEngineClient.Inspection), ClientTestDouble.Create<IInspectionClient>((method, arguments) =>
					Inspect(method, arguments!, module))),
				(nameof(ICheatEngineClient.Processes), ClientTestDouble.Create<IProcessClient>((_, _) => Process())),
				(nameof(ICheatEngineClient.Assembly),
					ClientTestDouble.Create<IAssemblyClient>((method, arguments) => Decode(method, arguments!))),
				(nameof(ICheatEngineClient.Patterns),
					ClientTestDouble.Create<IPatternScanner>((_, arguments) => Scan((AobScanRequest) arguments![0]!))));
			IOptions<McpExecutionOptions> execution = Options.Create(new McpExecutionOptions());
			Dispatch = new ToolDispatch(client, new McpFeatureGate(Options.Create(new McpFeatureOptions())),
				execution, new DispatchStatistics(execution), TimeProvider.System,
				new RecordingLogger<ToolDispatch>());
		}

		internal ToolDispatch Dispatch
		{
			get;
		}

		internal PatternScanScope Scope
		{
			get;
			init;
		} = PatternScanScope.HostBoundedRange;

		internal int Scans
		{
			get;
			private set;
		}

		internal void Add(ulong start, params string[] instructions)
		{
			Add(start, false, instructions);
		}

		internal void Add(ulong start, bool hidden, params string[] instructions)
		{
			_runs.Add((start, hidden,
			[
				.. instructions.Select(static text =>
					Convert.FromHexString(text.Replace(" ", string.Empty, StringComparison.Ordinal)))
			]));
		}

		internal AobSignature Generate(string address)
		{
			return new AobTools(Dispatch).GenerateSignature(address, cancellationToken: Token);
		}

		private static object? Inspect(MethodInfo method, object?[] arguments, ModuleInfo module)
		{
			if (method.Name == nameof(IInspectionClient.GetModules))
			{
				return ImmutableArray.Create(module);
			}

			if (method.Name != nameof(IInspectionClient.TryResolveAddress))
			{
				throw new XunitException($"Unexpected inspection call {method.Name}.");
			}

			Assert.True(HexParse.TryAddress(((SymbolExpression) arguments[0]!).Value, out ulong value));
			arguments[2] = new Address(value);
			arguments[3] = default(CheatEngineFailure);
			return true;
		}

		private ProcessSnapshot Process()
		{
			PointerSize bitness = _is64 ? PointerSize.Bit64 : PointerSize.Bit32;
			return new ProcessSnapshot(new TargetProcessId(42), null, null, TargetBackend.LocalProcess,
				_is64 ? CheatEngineArchitecture.X64 : CheatEngineArchitecture.X86, bitness, bitness.Bytes, null, 1);
		}

		private bool Decode(MethodInfo method, object?[] arguments)
		{
			Assert.Equal(nameof(IAssemblyClient.TryDisassemble), method.Name);
			ulong address = ((Address) arguments[0]!).ToUInt64();
			foreach ((ulong start, bool _, byte[][] instructions) in _runs)
			{
				ulong current = start;
				foreach (byte[] instruction in instructions)
				{
					if (current == address)
					{
						arguments[1] = new AssemblyInstructionSnapshot(new Address(address), instruction.Length,
							address.ToString("X", CultureInfo.InvariantCulture), "instruction", string.Empty,
							instruction);
						arguments[2] = default(CheatEngineFailure);
						return true;
					}

					current += (ulong) instruction.Length;
				}
			}

			arguments[1] = default(AssemblyInstructionSnapshot);
			arguments[2] = new CheatEngineFailure(CheatEngineFailureKind.MemoryReadFailed, "Assembly.Disassemble",
				"The memory cannot be read.", hostEffect: CheatEngineHostEffect.Completed);
			return false;
		}

		private PatternScanOutcome Scan(AobScanRequest request)
		{
			Scans++;
			Assert.Equal(("huge.dll", 2), (request.Module!.Value.Value, request.MaximumResults));
			string[] tokens = request.Pattern.Value.Split(' ');
			List<Address> matches = [];
			foreach ((ulong start, bool hidden, byte[][] instructions) in _runs.OrderBy(static run => run.Start))
			{
				if (hidden)
				{
					continue;
				}

				byte[] memory = [.. instructions.SelectMany(static instruction => instruction)];
				for (int offset = 0; offset + tokens.Length <= memory.Length; offset++)
				{
					if (Matches(memory, offset, tokens))
					{
						matches.Add(new Address(start + (ulong) offset));
					}
				}
			}

			ImmutableArray<Address> listed = [.. matches.Take(request.MaximumResults)];
			PatternScanMetrics metrics = new(Scope, (ulong) matches.Count, (ulong) matches.Count, 0, listed.Length,
				0, 0, 0, true, TimeSpan.FromMilliseconds(2), TimeSpan.FromMilliseconds(1));
			return new PatternScanOutcome(new AobScanResult(listed, matches.Count > listed.Length), null, metrics,
				listed.IsEmpty ? PatternScanHostOutcomeKind.NoMatches : PatternScanHostOutcomeKind.Matches,
				PatternScanRouteReason.ScopedRequestOnQualifiedTarget, true);
		}

		private static bool Matches(byte[] memory, int offset, string[] tokens)
		{
			for (int index = 0; index < tokens.Length; index++)
			{
				if (tokens[index] != "??" &&
					memory[offset + index] != byte.Parse(tokens[index], NumberStyles.HexNumber,
						CultureInfo.InvariantCulture))
				{
					return false;
				}
			}

			return true;
		}
	}
}
