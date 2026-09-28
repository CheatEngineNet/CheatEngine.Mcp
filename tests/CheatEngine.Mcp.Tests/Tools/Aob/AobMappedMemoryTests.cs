using System.ComponentModel;
using System.Reflection;
using System.Text.Json;

using CheatEngine.Client;
using CheatEngine.Client.Lua;
using CheatEngine.Client.Results;
using CheatEngine.Client.Scanning;
using CheatEngine.Mcp.Core.Contract;
using CheatEngine.Mcp.Core.Features;
using CheatEngine.Mcp.Core.Values;
using CheatEngine.Mcp.Tests.Support;
using CheatEngine.Mcp.Tests.Tools.Memory;
using CheatEngine.Mcp.Tools.Aob;
using CheatEngine.Mcp.Tools.Scan;
using CheatEngine.SDK.Engine.Runtime;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace CheatEngine.Mcp.Tests.Tools.Aob;

/// <summary>
///     <c>includeMapped</c> on <c>aob_find</c> and <c>aob_find_value</c>: each call scans in one dispatch through the
///     activation's shared <see cref="MappedMemoryOverride" />, with the same override, errors and nested-scan guard as
///     <c>scan_first</c>, except a module-scoped call, which the override cannot change, over a fixed Lua double that
///     tracks Cheat Engine's <c>MEM_MAPPED</c> override.
/// </summary>
public sealed class AobMappedMemoryTests
{
	private const string Hint =
		"Omit includeMapped, or ask the user to tick MEM_MAPPED under Edit > Settings > Scan Settings.";

	private static CancellationToken Token => TestContext.Current.CancellationToken;

	[Fact]
	public void Find_IncludeMapped_ScansEveryPatternInsideOneOverrideThatEndsBeforeReturning()
	{
		OverrideDouble lua = new();
		TargetDouble target = new()
		{
			LuaResult = lua.Answer
		};
		target.Patterns = (_, _) =>
		{
			lua.Steps.Add(lua.Active ? "scan in override" : "scan");
			return AobToolsTests.Outcome(1, 1, 0, false);
		};

		AobFindResult result = new AobTools(target.Dispatch).Find(["48 8B", "90 90"], startAddress: "401000",
			endAddress: "402000", includeMapped: true, cancellationToken: Token);

		Assert.Equal(2, result.Results.Length);
		Assert.Equal(["set", "scan in override", "scan in override", "end"], lua.Steps);
		Assert.False(lua.Active);
		// One dispatch scans every pattern, and the range resolves once, before the override.
		Assert.Equal(1, target.Dispatcher.Calls);
		Assert.Equal(2, target.CallsTo("Inspection").Length);
	}

	[Fact]
	public void FindValue_IncludeMapped_ScansInsideTheOverrideAndEndsIt()
	{
		OverrideDouble lua = new();
		TargetDouble target = new()
		{
			LuaResult = lua.Answer
		};
		target.Patterns = (_, _) =>
		{
			lua.Steps.Add(lua.Active ? "scan in override" : "scan");
			return AobToolsTests.Outcome(1, 1, 0, false);
		};

		AobValueResult result = new AobTools(target.Dispatch).FindValue(McpValueType.Int32, "100",
			includeMapped: true, cancellationToken: Token);

		Assert.Equal(["401000"], result.Result.Matches);
		Assert.Equal(["set", "scan in override", "end"], lua.Steps);
		Assert.Equal(1, target.Dispatcher.Calls);
	}

	[Fact]
	public void Find_WithoutIncludeMapped_RunsNoFixedLua()
	{
		TargetDouble target = new();
		target.Patterns = (_, _) => AobToolsTests.Outcome(1, 1, 0, false);

		new AobTools(target.Dispatch).Find(["48 8B", "90"], "game.exe", cancellationToken: Token);

		Assert.Equal(0, target.LuaCalls);
		Assert.Equal(1, target.Dispatcher.Calls);
	}

	[Fact]
	public void Find_IncludeMappedScanFails_EndsTheOverrideAndReportsTheScanFailure()
	{
		OverrideDouble lua = new();
		TargetDouble target = new()
		{
			LuaResult = lua.Answer
		};
		int scans = 0;
		target.Patterns = (_, _) => ++scans == 1 ? AobToolsTests.Outcome(1, 1, 0, false) : TargetChanged();

		CheatEngineToolException failed = Assert.Throws<CheatEngineToolException>(() =>
			new AobTools(target.Dispatch).Find(["48 8B", "90", "CC"], includeMapped: true, cancellationToken: Token));

		// The dispatch reports the scan's own failure once the override has ended; the last pattern never ran.
		Assert.Equal((ToolErrorKind.TargetChanged, "Another target."), (failed.Error.Kind, failed.Error.Message));
		Assert.Equal(["set", "end"], lua.Steps);
		Assert.Equal(2, scans);
	}

	[Fact]
	public void Find_IncludeMappedScanCancelled_EndsTheOverrideWithAnUncancelledCall()
	{
		using CancellationTokenSource request = new();
		List<(string Script, bool Cancelled)> calls = [];
		IPatternScanner patterns = ClientTestDouble.Create<IPatternScanner>((_, _) =>
		{
			request.Cancel();
			throw new OperationCanceledException(request.Token);
		});

		Assert.ThrowsAny<OperationCanceledException>(() => new AobTools(CancellableDispatch(calls, patterns))
			.Find(["48 8B"], includeMapped: true, cancellationToken: request.Token));

		Assert.Equal([("set", false), ("end", false)], calls);
	}

	[Theory]
	[InlineData(true)]
	[InlineData(false)]
	public void Find_CancelledBetweenPatterns_ScansNoFurtherPatternAndEndsTheOverrideUncancelled(bool includeMapped)
	{
		using CancellationTokenSource request = new();
		List<(string Script, bool Cancelled)> calls = [];
		List<string> scanned = [];
		IPatternScanner patterns = ClientTestDouble.Create<IPatternScanner>((_, arguments) =>
		{
			// The caller cancels while the first scan runs, and that scan still completes.
			scanned.Add(((AobScanRequest) arguments![0]!).Pattern.Value);
			request.Cancel();
			return AobToolsTests.Outcome(1, 1, 0, false);
		});

		Assert.ThrowsAny<OperationCanceledException>(() => new AobTools(CancellableDispatch(calls, patterns))
			.Find(["48 8B", "90", "CC"], includeMapped: includeMapped, cancellationToken: request.Token));

		// The call checks the caller's token before each further pattern, so Cheat Engine scans no more of them.
		Assert.Equal(["48 8B"], scanned);
		(string, bool)[] expected = includeMapped ? [("set", false), ("end", false)] : [];
		Assert.Equal(expected, calls);
	}

	[Fact]
	public void IncludeMapped_WithoutTheOverrideInCheatEngine_IsUnsupportedBeforeAnyScan()
	{
		OverrideDouble lua = new()
		{
			SetError = new LuaScriptError("unsupported",
				"This Cheat Engine has no setSpecialScanOptionsOverride, so a scan cannot include mapped memory.",
				"not_started", Hint)
		};
		TargetDouble target = new()
		{
			LuaResult = lua.Answer
		};

		CheatEngineToolException refused = Assert.Throws<CheatEngineToolException>(() =>
			new AobTools(target.Dispatch).Find(["48 8B"], includeMapped: true, cancellationToken: Token));

		Assert.Equal((ToolErrorKind.Unsupported, ToolHostEffect.NotStarted, false, Hint),
			(refused.Error.Kind, refused.Error.HostEffect, refused.Error.Retryable, refused.Error.Hint));
		// A refusal before the script started proves that nothing is set: no end, no scan.
		Assert.Equal(["set"], lua.Steps);
		Assert.Empty(target.CallsTo("Patterns"));
	}

	[Fact]
	public void IncludeMapped_WhoseOverrideFailedAfterItMayHaveStarted_EndsItWithoutScanning()
	{
		OverrideDouble lua = new()
		{
			SetError = new LuaScriptError("host_refused",
				"Cheat Engine refused the MEM_MAPPED scan override: denied", "unknown", Hint)
		};
		TargetDouble target = new()
		{
			LuaResult = lua.Answer
		};

		CheatEngineToolException failed = Assert.Throws<CheatEngineToolException>(() =>
			new AobTools(target.Dispatch).FindValue(McpValueType.Int32, "1", includeMapped: true,
				cancellationToken: Token));

		Assert.Equal((ToolErrorKind.HostRefused, ToolHostEffect.Unknown),
			(failed.Error.Kind, failed.Error.HostEffect));
		Assert.Equal(["set", "end"], lua.Steps);
		Assert.False(lua.Active);
		Assert.Empty(target.CallsTo("Patterns"));
	}

	[Fact]
	public void Find_IncludeMappedOverrideCannotBeRemoved_IsPartialEffectThatKeepsTheResults()
	{
		OverrideDouble lua = new()
		{
			EndError = new LuaScriptError("host_refused",
				"Cheat Engine did not end the scan-region override: locked", "unknown", null)
		};
		TargetDouble target = new()
		{
			LuaResult = lua.Answer
		};
		target.Patterns = (_, _) => AobToolsTests.Outcome(1, 1, 0, false);

		CheatEngineToolException failed = Assert.Throws<CheatEngineToolException>(() =>
			new AobTools(target.Dispatch).Find(["48 8B", "90"], includeMapped: true, cancellationToken: Token));

		Assert.Equal((ToolErrorKind.PartialEffect, ToolHostEffect.CleanupUnconfirmed, false),
			(failed.Error.Kind, failed.Error.HostEffect, failed.Error.Retryable));
		Assert.Equal(
			"The scan completed, but Cheat Engine's MEM_MAPPED scan override could not be removed: Cheat Engine did not end the scan-region override: locked",
			failed.Error.Message);
		Assert.Contains("includeMapped=true", failed.Error.Hint, StringComparison.Ordinal);
		AobMappedOverrideFailure details = Assert.IsType<AobMappedOverrideFailure>(
			failed.Error.Details!.Value.Deserialize(AobJsonContext.Default.AobMappedOverrideFailure));
		Assert.True(details.ScanCompleted);
		Assert.Equal(["48 8B", "90"], details.Results!.Select(static result => result.Pattern));
		Assert.Equal(["401000"], details.Results![1].Matches);
	}

	[Fact]
	public void FindValue_WhenTheScanAndTheEndFail_ReportsTheScanErrorInThePartialEffect()
	{
		OverrideDouble lua = new()
		{
			EndError = new LuaScriptError("host_refused", "locked", "unknown", null)
		};
		TargetDouble target = new()
		{
			LuaResult = lua.Answer
		};
		target.Patterns = (_, _) => TargetChanged();

		CheatEngineToolException failed = Assert.Throws<CheatEngineToolException>(() =>
			new AobTools(target.Dispatch).FindValue(McpValueType.Int32, "1", includeMapped: true,
				cancellationToken: Token));

		Assert.Equal((ToolErrorKind.PartialEffect, ToolHostEffect.CleanupUnconfirmed),
			(failed.Error.Kind, failed.Error.HostEffect));
		Assert.Equal(
			"The scan failed (Another target.), and Cheat Engine's MEM_MAPPED scan override could not be removed: locked",
			failed.Error.Message);
		Assert.Equal(ToolErrorKind.TargetChanged,
			Assert.IsType<CheatEngineToolException>(failed.InnerException).Error.Kind);
		// Without completed scans the details carry no results.
		Assert.Equal("{\"scanCompleted\":false}", failed.Error.Details!.Value.GetRawText());
	}

	[Theory]
	[InlineData("missing.dll", "500000", ToolErrorKind.NotFound)]
	[InlineData("402000", "401000", ToolErrorKind.InvalidArgument)]
	public void IncludeMapped_RangeRefusals_ComeBeforeTheOverride(string start, string end, ToolErrorKind expected)
	{
		OverrideDouble lua = new();
		TargetDouble target = new()
		{
			LuaResult = lua.Answer
		};

		CheatEngineToolException refused = Assert.Throws<CheatEngineToolException>(() =>
			new AobTools(target.Dispatch).Find(["48 8B"], startAddress: start, endAddress: end, includeMapped: true,
				cancellationToken: Token));

		Assert.Equal(expected, refused.Error.Kind);
		// No override was set or ended, so a Lua script's own override stays untouched.
		Assert.Empty(lua.Steps);
		Assert.Empty(target.CallsTo("Patterns"));
	}

	[Fact]
	public void FindValue_PointerAbove4GiBOnA32BitTarget_IsRefusedBeforeTheOverride()
	{
		OverrideDouble lua = new();
		TargetDouble target = new()
		{
			Bitness = PointerSize.Bit32,
			LuaResult = lua.Answer
		};

		CheatEngineToolException refused = Assert.Throws<CheatEngineToolException>(() =>
			new AobTools(target.Dispatch).FindValue(McpValueType.Pointer, "7FF612345678", includeMapped: true,
				cancellationToken: Token));

		Assert.Equal(ToolErrorKind.InvalidArgument, refused.Error.Kind);
		Assert.Empty(lua.Steps);
	}

	[Fact]
	public void IncludeMappedWithModule_IsRefusedBeforeAnyDispatch()
	{
		TargetDouble target = new();
		AobTools tools = new(target.Dispatch);

		CheatEngineToolException find = Assert.Throws<CheatEngineToolException>(() =>
			tools.Find(["48 8B"], "game.exe", includeMapped: true, cancellationToken: Token));
		CheatEngineToolException value = Assert.Throws<CheatEngineToolException>(() =>
			tools.FindValue(McpValueType.Int32, "1", "game.exe", includeMapped: true, cancellationToken: Token));

		Assert.All([find, value], static exception =>
		{
			Assert.Equal(ToolErrorKind.InvalidArgument, exception.Error.Kind);
			Assert.Equal("includeMapped", exception.Error.Details!.Value.GetProperty("parameter").GetString());
		});
		Assert.Equal(0, target.Dispatcher.Calls);
	}

	[Fact]
	public void NestedIncludeMappedScans_ShareOneOverrideThatTheOutermostCallEnds()
	{
		OverrideDouble lua = new();
		TargetDouble target = new()
		{
			LuaResult = lua.Answer
		};
		AobTools tools = new(target.Dispatch);
		AobValueResult? inner = null;
		target.Patterns = (_, _) =>
		{
			// A dispatch queued meanwhile runs inside the running scan's wait.
			if (inner is null && lua.Steps.Count == 1)
			{
				lua.Steps.Add("inner");
				inner = tools.FindValue(McpValueType.Int32, "5", includeMapped: true, cancellationToken: Token);
			}

			lua.Steps.Add(lua.Active ? "scan in override" : "scan");
			return AobToolsTests.Outcome(1, 1, 0, false);
		};

		tools.Find(["48 8B"], includeMapped: true, cancellationToken: Token);

		Assert.NotNull(inner);
		// The inner call neither set the override again nor ended it under the outer scan.
		Assert.Equal(["set", "inner", "scan in override", "scan in override", "end"], lua.Steps);
	}

	[Theory]
	[InlineData(true)]
	[InlineData(false)]
	public void NestedScansThatDisagreeOnIncludeMapped_AreRefusedAsBusyBeforeAnyEffect(bool outerMapped)
	{
		OverrideDouble lua = new();
		TargetDouble target = new()
		{
			LuaResult = lua.Answer
		};
		AobTools tools = new(target.Dispatch);
		CheatEngineToolException? refused = null;
		target.Patterns = (_, _) =>
		{
			refused ??= Assert.Throws<CheatEngineToolException>(() => tools.FindValue(McpValueType.Int32, "5",
				includeMapped: !outerMapped, cancellationToken: Token));
			return AobToolsTests.Outcome(1, 1, 0, false);
		};

		tools.Find(["48 8B"], includeMapped: outerMapped, cancellationToken: Token);

		Assert.NotNull(refused);
		Assert.Equal((ToolErrorKind.Busy, ToolHostEffect.NotStarted, true),
			(refused.Error.Kind, refused.Error.HostEffect, refused.Error.Retryable));
		Assert.EndsWith("aob_find_value was not started.", refused.Error.Message, StringComparison.Ordinal);
		Assert.Equal("Repeat the call after the running scan returns.", refused.Error.Hint);
		Assert.Single(target.CallsTo("Patterns"));
		string[] steps = outerMapped ? ["set", "end"] : [];
		Assert.Equal(steps, lua.Steps);
	}

	[Theory]
	[InlineData(true)]
	[InlineData(false)]
	public void AobScansNestedInScanFirst_FollowItsIncludeMapped(bool innerMapped)
	{
		// scan_first with includeMapped holds the same owner while its named scan waits.
		OverrideDouble lua = new();
		TargetDouble target = new()
		{
			LuaResult = lua.Answer
		};
		target.Patterns = (_, _) => AobToolsTests.Outcome(1, 1, 0, false);
		MappedMemoryOverride shared = new(target.Dispatch);
		AobTools tools = new(target.Dispatch, shared);
		CheatEngineToolException? refused = null;

		target.Dispatch.Run(CheatEngineToolNames.ScanFirst, token => shared.Include(CheatEngineToolNames.ScanFirst,
			() =>
			{
				refused = Xunit.Record.Exception(() => tools.Find(["48 8B"], includeMapped: innerMapped,
					cancellationToken: Token)) as CheatEngineToolException;
				return "scanned";
			}, static _ => new AobMappedOverrideFailure(false), AobJsonContext.Default.AobMappedOverrideFailure,
			token), Token);

		ToolErrorKind? expected = innerMapped ? null : ToolErrorKind.Busy;
		Assert.Equal(expected, refused?.Error.Kind);
		Assert.Equal(innerMapped ? 1 : 0, target.CallsTo("Patterns").Length);
		// Only scan_first's outermost scan set and ended the override.
		Assert.Equal(["set", "end"], lua.Steps);
	}

	[Fact]
	public void ModuleScopedScans_NestedInAScanWithIncludeMapped_RunWithoutTheBusyGuard()
	{
		// A module's image holds only MEM_IMAGE regions, which scan_first's MEM_MAPPED override cannot change.
		OverrideDouble lua = new();
		TargetDouble target = new()
		{
			LuaResult = lua.Answer
		};
		target.Patterns = (_, _) =>
		{
			lua.Steps.Add(lua.Active ? "scan in override" : "scan");
			return AobToolsTests.Outcome(1, 1, 0, false);
		};
		MappedMemoryOverride shared = new(target.Dispatch);
		AobTools tools = new(target.Dispatch, shared);
		AobFindResult? find = null;
		AobValueResult? value = null;

		target.Dispatch.Run(CheatEngineToolNames.ScanFirst, token => shared.Include(CheatEngineToolNames.ScanFirst,
			() =>
			{
				find = tools.Find(["48 8B"], "game.exe", cancellationToken: Token);
				value = tools.FindValue(McpValueType.Int32, "5", "game.exe", cancellationToken: Token);
				return "scanned";
			}, static _ => new AobMappedOverrideFailure(false), AobJsonContext.Default.AobMappedOverrideFailure,
			token), Token);

		Assert.Equal(["401000"], Assert.Single(find!.Results).Matches);
		Assert.Equal(["401000"], value!.Result.Matches);
		// The module scans neither set nor ended the override that scan_first holds.
		Assert.Equal(["set", "scan in override", "scan in override", "end"], lua.Steps);
	}

	[Fact]
	public void IncludeMappedScan_NestedInAModuleScopedScan_IsNotRefused()
	{
		OverrideDouble lua = new();
		TargetDouble target = new()
		{
			LuaResult = lua.Answer
		};
		AobTools tools = new(target.Dispatch);
		AobValueResult? inner = null;
		target.Patterns = (_, arguments) =>
		{
			// The module scan runs a dispatch queued meanwhile, which includes mapped memory.
			if (((AobScanRequest) arguments[0]!).Module is not null)
			{
				inner = tools.FindValue(McpValueType.Int32, "5", includeMapped: true, cancellationToken: Token);
			}

			lua.Steps.Add(lua.Active ? "scan in override" : "scan");
			return AobToolsTests.Outcome(1, 1, 0, false);
		};

		tools.Find(["48 8B"], "game.exe", cancellationToken: Token);

		Assert.Equal(["401000"], inner!.Result.Matches);
		// The nested call set and ended its own override; the module scan held no guard against it.
		Assert.Equal(["set", "scan in override", "end", "scan"], lua.Steps);
	}

	[Fact]
	public void Composition_AobToolsUseTheActivationsSharedOverrideOwner()
	{
		foreach (bool withScanTools in (bool[]) [false, true])
		{
			ServiceCollection services = new();
			services.AddSingleton(ClientTestDouble.Client());
			services.AddLogging();
			ICheatEngineMcpBuilder builder = new CheatEngineMcpBuilder(services, CheatEngineMcpMode.Backend)
				.AddExecutionServices().AddAobTools();
			if (withScanTools)
			{
				builder.AddScanTools();
			}

			using ServiceProvider root = services.BuildServiceProvider(new ServiceProviderOptions
			{
				ValidateOnBuild = true,
				ValidateScopes = true
			});
			using IServiceScope scope = root.CreateScope();
			AobTools tools = scope.ServiceProvider.GetRequiredService<AobTools>();
			MappedMemoryOverride shared = scope.ServiceProvider.GetRequiredService<MappedMemoryOverride>();

			// While scan_first's scan without includeMapped holds the shared owner, this call is refused.
			CheatEngineToolException refused = shared.Follow(CheatEngineToolNames.ScanFirst,
				() => Assert.Throws<CheatEngineToolException>(() =>
					tools.Find(["90"], includeMapped: true, cancellationToken: Token)));

			Assert.Equal(ToolErrorKind.Busy, refused.Error.Kind);
			Assert.EndsWith("aob_find was not started.", refused.Error.Message, StringComparison.Ordinal);
		}
	}

	[Fact]
	public void IncludeMapped_DefaultsToFalseAndSharesScanFirstsOverrideDescription()
	{
		foreach (string name in (string[]) [nameof(AobTools.Find), nameof(AobTools.FindValue)])
		{
			ParameterInfo parameter = typeof(AobTools).GetMethod(name)!.GetParameters()
				.Single(static parameter => parameter.Name == "includeMapped");
			string description = parameter.GetCustomAttribute<DescriptionAttribute>()!.Description;

			Assert.Equal((typeof(bool), false), (parameter.ParameterType, parameter.DefaultValue));
			Assert.Contains(MappedMemoryOverride.IncludeMappedEffect, description, StringComparison.Ordinal);
			Assert.EndsWith(" Not with module, whose image is never mapped memory.", description,
				StringComparison.Ordinal);
		}
	}

	/// <summary>
	///     A dispatch whose Lua double answers the shared override scripts and records whether the token of each call
	///     was cancelled, and whose pattern scanner is <paramref name="patterns" />.
	/// </summary>
	private static ToolDispatch CancellableDispatch(List<(string Script, bool Cancelled)> calls,
		IPatternScanner patterns)
	{
		ILuaClient lua = ClientTestDouble.Create<ILuaClient>((method, arguments) =>
		{
			Assert.Equal(nameof(ILuaClient.Execute), method.Name);
			string source = (string) arguments![0]!.GetType().GetProperty("Source")!.GetValue(arguments[0])!;
			string script = source.EndsWith(MappedMemoryOverride.SetScript, StringComparison.Ordinal) ? "set" : "end";
			calls.Add((script, ((CancellationToken) arguments[1]!).IsCancellationRequested));
			return Activator.CreateInstance(method.ReturnType, true, null, 0);
		});
		ICheatEngineClient client = ClientTestDouble.Client(new RecordingDispatcher().Dispatcher,
			CancellationToken.None, (nameof(ICheatEngineClient.Lua), lua),
			(nameof(ICheatEngineClient.Patterns), patterns));
		IOptions<McpExecutionOptions> execution = Options.Create(new McpExecutionOptions());
		return new ToolDispatch(client, new McpFeatureGate(Options.Create(new McpFeatureOptions())), execution,
			new DispatchStatistics(execution), TimeProvider.System, new RecordingLogger<ToolDispatch>(),
			new PluginFixedLuaExecutor(client));
	}

	private static PatternScanOutcome TargetChanged()
	{
		return new PatternScanOutcome(null,
			new CheatEngineFailure(CheatEngineFailureKind.TargetChanged, "Patterns.Scan", "Another target.",
				hostEffect: CheatEngineHostEffect.Completed), null, PatternScanHostOutcomeKind.TargetChanged,
			PatternScanRouteReason.ScopedRequestOnQualifiedTarget, false);
	}

	/// <summary>
	///     Answers the shared override scripts through <see cref="TargetDouble.LuaResult" /> and tracks Cheat Engine's
	///     <c>MEM_MAPPED</c> override like Cheat Engine 7.7.
	/// </summary>
	private sealed class OverrideDouble
	{
		/// <summary>The scripts and scans in order: <c>set</c>, <c>end</c> and whatever the test adds.</summary>
		public List<string> Steps
		{
			get;
		} = [];

		public bool Active
		{
			get;
			private set;
		}

		/// <summary>The failure the set script declares, with its host effect.</summary>
		public LuaScriptError? SetError
		{
			get;
			init;
		}

		/// <summary>The failure the end script declares.</summary>
		public LuaScriptError? EndError
		{
			get;
			init;
		}

		public object Answer(string source)
		{
			if (source.EndsWith(MappedMemoryOverride.SetScript, StringComparison.Ordinal))
			{
				Steps.Add("set");
				Active = SetError?.HostEffect != "not_started";
				return SetError ?? (object) true;
			}

			Assert.EndsWith(MappedMemoryOverride.EndScript, source, StringComparison.Ordinal);
			Steps.Add("end");
			Active = Active && EndError is not null;
			return EndError ?? (object) true;
		}
	}
}
