using System.Collections.Immutable;

using CheatEngine.Client.Inspection;
using CheatEngine.Client.Results;
using CheatEngine.Client.Scanning;
using CheatEngine.Mcp.Core.Contract;
using CheatEngine.Mcp.Tests.Tools.Memory;
using CheatEngine.Mcp.Tools.Aob;
using CheatEngine.Mcp.Tools.Memory;
using CheatEngine.SDK.Engine.Inspection;
using CheatEngine.SDK.Engine.Values;

using Xunit.Sdk;

namespace CheatEngine.Mcp.Tests.Tools.Aob;

/// <summary>AOB scans and signatures: counts derived from the scan metrics, and the module guard of getUniqueAOB.</summary>
public sealed class AobToolsTests
{
	private static CancellationToken Token => TestContext.Current.CancellationToken;

	private static readonly ModuleInfo Game =
		new("game.exe", new Address(0x400000), new MemorySize(0x10000), true, @"C:\game\game.exe");

	[Theory]
	[InlineData(1, 1UL, 0UL, false, true, true)]
	[InlineData(1, 2UL, 1UL, true, false, false)]
	[InlineData(1, 2UL, 0UL, true, false, false)]
	[InlineData(2, 2UL, 0UL, false, true, false)]
	[InlineData(0, 0UL, 0UL, false, true, false)]
	public void Find_ScanMetrics_DeriveExactAndUnique(int matches, ulong hostRows, ulong unread, bool truncated,
		bool exact, bool unique)
	{
		TargetDouble target = new();
		target.Patterns = (_, _) => Outcome(matches, hostRows, unread, truncated);

		AobPatternResult result = Assert.Single(new AobTools(target.Dispatch)
			.Find(["48 8B 05"], "game.exe", limit: Math.Max(matches, 1), cancellationToken: Token).Results);

		Assert.Equal((matches, exact, unique), (result.Count, result.Exact, result.Unique));
		Assert.Equal((AobScanScope.BoundedScan, true, 3), (result.Scope, result.TargetVerified, result.ElapsedMs));
	}

	[Fact]
	public void Find_Options_ReachTheClientRequestAndEachPatternIsItsOwnDispatch()
	{
		TargetDouble target = new();
		List<AobScanRequest> requests = [];
		target.Patterns = (method, arguments) =>
		{
			Assert.Equal(nameof(IPatternScanner.ScanDetailed), method.Name);
			requests.Add((AobScanRequest) arguments[0]!);
			return Outcome(1, 1, 0, false);
		};

		AobFindResult result = new AobTools(target.Dispatch).Find(["48 8b ? 05", "90 *"], "game.exe", "401000",
			"402000", executable: ProtectionRequirement.Required, writable: ProtectionRequirement.Excluded,
			alignment: 4, limit: 2, cancellationToken: Token);

		Assert.Equal(["48 8B ?? 05", "90 ??"], result.Results.Select(static item => item.Pattern));
		AobScanRequest first = requests[0];
		Assert.Equal(("game.exe", 2), (first.Module!.Value.Value, first.MaximumResults));
		Assert.Equal((new Address(0x401000), new Address(0x402000)), (first.Range!.Value.Start, first.Range.Value.End));
		Assert.Equal((ScanProtectionRequirement.Required, ScanProtectionRequirement.Excluded,
				ScanProtectionRequirement.Unspecified),
			(first.Protection.Executable, first.Protection.Writable, first.Protection.CopyOnWrite));
		Assert.Equal((ScanAlignmentMode.AlignedTo, 4), (first.Alignment.Mode, first.Alignment.Divisor));
		Assert.Equal(2, target.Dispatcher.Calls);
	}

	[Fact]
	public void Find_GlobalScanWithoutAResultList_ReportsZeroMatchesNotProven()
	{
		TargetDouble target = new();
		target.Patterns = (_, _) => new PatternScanOutcome(null,
			new CheatEngineFailure(CheatEngineFailureKind.IndeterminateHostResult, "Patterns.Scan",
				"AOBScan returned nil.", hostEffect: CheatEngineHostEffect.Completed), null,
			PatternScanHostOutcomeKind.NoResult, PatternScanRouteReason.UnscopedRequest, false);

		AobPatternResult result = Assert.Single(new AobTools(target.Dispatch).Find(["CC CC CC"], cancellationToken: Token).Results);

		Assert.Equal((0, false, false, AobScanScope.GlobalScan), (result.Count, result.Exact, result.Unique,
			result.Scope));
		Assert.Empty(result.Matches);
	}

	[Fact]
	public void Find_OtherScanFailure_IsTheClientFailure()
	{
		TargetDouble target = new();
		target.Patterns = (_, _) => new PatternScanOutcome(null,
			new CheatEngineFailure(CheatEngineFailureKind.TargetChanged, "Patterns.Scan", "Another target.",
				hostEffect: CheatEngineHostEffect.Completed), null, PatternScanHostOutcomeKind.TargetChanged,
			PatternScanRouteReason.ScopedRequestOnQualifiedTarget, false);

		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() =>
			new AobTools(target.Dispatch).Find(["48 8B"], "game.exe", cancellationToken: Token));

		Assert.Equal(ToolErrorKind.TargetChanged, exception.Error.Kind);
	}

	[Theory]
	[InlineData(9, "90", null, null, null, null, 10, ToolErrorKind.LimitExceeded)]
	[InlineData(1, "4G", null, null, null, null, 10, ToolErrorKind.InvalidArgument)]
	[InlineData(1, "4? 90", null, null, null, null, 10, ToolErrorKind.InvalidArgument)]
	[InlineData(1, "?? ??", null, null, null, null, 10, ToolErrorKind.InvalidArgument)]
	[InlineData(1, "90", "401000", null, null, null, 10, ToolErrorKind.InvalidArgument)]
	[InlineData(1, "90", null, null, 4, "0", 10, ToolErrorKind.InvalidArgument)]
	[InlineData(1, "90", null, null, null, "XYZ", 10, ToolErrorKind.InvalidArgument)]
	[InlineData(1, "90", null, null, null, null, 10001, ToolErrorKind.LimitExceeded)]
	public void Find_InvalidArguments_RefuseBeforeAnyScan(int count, string pattern, string? start, string? end,
		int? alignment, string? lastDigits, int limit, ToolErrorKind kind)
	{
		TargetDouble target = new();

		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() =>
			new AobTools(target.Dispatch).Find([.. Enumerable.Repeat(pattern, count)], startAddress: start,
				endAddress: end, alignment: alignment, lastDigits: lastDigits, limit: limit, cancellationToken: Token));

		Assert.Equal(kind, exception.Error.Kind);
		Assert.Equal(0, target.Dispatcher.Calls);
	}

	[Fact]
	public void GenerateSignature_AddressOutsideAnyModule_IsRefusedBeforeCheatEngineScans()
	{
		TargetDouble target = new()
		{
			Inspection = Modules(Game)
		};

		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() =>
			new AobTools(target.Dispatch).GenerateSignature("900000", cancellationToken: Token));

		Assert.Equal(ToolErrorKind.InvalidArgument, exception.Error.Kind);
		Assert.StartsWith("address", exception.Error.Message, StringComparison.Ordinal);
		Assert.Equal(0, target.LuaCalls);
		Assert.Empty(target.CallsTo("Patterns"));
	}

	[Fact]
	public void GenerateSignature_ModuleAbove64MiB_IsLimitExceededBeforeCheatEngineScans()
	{
		ModuleInfo huge = new("huge.dll", new Address(0x10000000), new MemorySize(80UL * 1024 * 1024), true,
			@"C:\game\huge.dll");
		TargetDouble target = new()
		{
			Inspection = Modules(Game, huge)
		};

		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() =>
			new AobTools(target.Dispatch).GenerateSignature("10000100", cancellationToken: Token));

		Assert.Equal(ToolErrorKind.LimitExceeded, exception.Error.Kind);
		Assert.Equal(0, target.LuaCalls);
	}

	[Fact]
	public void GenerateSignature_ModuleThatDoesNotContainTheAddress_IsRefused()
	{
		ModuleInfo other = new("other.dll", new Address(0x500000), new MemorySize(0x1000), true, @"C:\other.dll");
		TargetDouble target = new()
		{
			Inspection = Modules(Game, other)
		};

		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() =>
			new AobTools(target.Dispatch).GenerateSignature("401000", "other.dll", cancellationToken: Token));

		Assert.Equal(ToolErrorKind.InvalidArgument, exception.Error.Kind);
		Assert.StartsWith("module", exception.Error.Message, StringComparison.Ordinal);
		Assert.Equal(0, target.LuaCalls);
	}

	[Fact]
	public void GenerateSignature_UniquePattern_NormalizesWildcardsAndVerifiesTheMatchStart()
	{
		TargetDouble target = new()
		{
			Inspection = Modules(Game)
		};
		string? source = null;
		target.LuaResult = script =>
		{
			source = script;
			return new UniqueAobProbe(true, "48 8B ** 05", 2);
		};
		AobScanRequest? verification = null;
		target.Patterns = (_, arguments) =>
		{
			verification = (AobScanRequest) arguments[0]!;
			return Outcome(1, 1, 0, false, 0x401000);
		};

		AobSignature signature = new AobTools(target.Dispatch).GenerateSignature("401002", cancellationToken: Token);

		Assert.Equal(new AobSignature("401002", "game.exe", true, true, "48 8B ?? 05", "401000", 2, 4, 1),
			signature);
		Assert.Contains("[1] = 0x401002", source, StringComparison.Ordinal);
		Assert.Equal(("game.exe", 2, "48 8B ?? 05"), (verification!.Value.Module!.Value.Value, verification.Value.MaximumResults,
			verification.Value.Pattern.Value));
		Assert.Equal(1, target.Dispatcher.Calls);
	}

	[Fact]
	public void GenerateSignature_VerificationFindsTwoMatches_IsNotUnique()
	{
		TargetDouble target = new()
		{
			Inspection = Modules(Game)
		};
		target.LuaResult = _ => new UniqueAobProbe(true, "48 8B 05", 0);
		target.Patterns = (_, _) => Outcome(2, 3, 0, true, 0x401000);

		AobSignature signature = new AobTools(target.Dispatch).GenerateSignature("401000", cancellationToken: Token);

		Assert.Equal((false, true, 2), (signature.Unique, signature.Verified, signature.MatchCount));
	}

	[Fact]
	public void GenerateSignature_FilteredGlobalVerification_IsRefused()
	{
		TargetDouble target = new()
		{
			Inspection = Modules(Game),
			LuaResult = _ => new UniqueAobProbe(true, "48 8B 05", 0),
			Patterns = (_, _) => new PatternScanOutcome(
				new AobScanResult([new Address(0x401000)], false), null,
				new PatternScanMetrics(PatternScanScope.GlobalHostScanWithManagedFilter, 1, 1, 0, 1, 0, 0, 0,
					true, TimeSpan.FromMilliseconds(2), TimeSpan.FromMilliseconds(1)),
				PatternScanHostOutcomeKind.Matches, PatternScanRouteReason.TargetIdentityNotQualified, false)
		};

		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() =>
			new AobTools(target.Dispatch).GenerateSignature("401000", cancellationToken: Token));

		Assert.Equal((ToolErrorKind.TargetChanged, ToolHostEffect.Completed),
			(exception.Error.Kind, exception.Error.HostEffect));
	}

	[Theory]
	[InlineData(null)]
	[InlineData(-1L)]
	[InlineData(3L)]
	public void GenerateSignature_InvalidOffset_IsRefusedBeforeVerification(long? offset)
	{
		TargetDouble target = new()
		{
			Inspection = Modules(Game),
			LuaResult = _ => new UniqueAobProbe(true, "48 8B 05", offset)
		};

		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() =>
			new AobTools(target.Dispatch).GenerateSignature("401000", verify: false, cancellationToken: Token));

		Assert.Equal((ToolErrorKind.HostRefused, ToolHostEffect.Completed),
			(exception.Error.Kind, exception.Error.HostEffect));
		Assert.Empty(target.CallsTo("Patterns"));
	}

	[Fact]
	public void GenerateSignature_OffsetBeforeAddressSpace_IsRefusedBeforeVerification()
	{
		ModuleInfo zeroBased = new("zero.exe", new Address(0), new MemorySize(0x100), true, @"C:\zero.exe");
		TargetDouble target = new()
		{
			Inspection = Modules(zeroBased),
			LuaResult = _ => new UniqueAobProbe(true, "48 8B 05", 2)
		};

		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() =>
			new AobTools(target.Dispatch).GenerateSignature("1", verify: false, cancellationToken: Token));

		Assert.Equal((ToolErrorKind.HostRefused, ToolHostEffect.Completed),
			(exception.Error.Kind, exception.Error.HostEffect));
		Assert.Empty(target.CallsTo("Patterns"));
	}

	[Fact]
	public void GenerateSignature_CheatEngineFindsNone_ReportsItsLastAttempt()
	{
		TargetDouble target = new()
		{
			Inspection = Modules(Game)
		};
		target.LuaResult = _ => new UniqueAobProbe(false, Tried: "48 8B * 05");

		AobSignature signature = new AobTools(target.Dispatch).GenerateSignature("401000", cancellationToken: Token);

		Assert.Equal(new AobSignature("401000", "game.exe", false, false, TriedPattern: "48 8B ?? 05"), signature);
		Assert.Empty(target.CallsTo("Patterns"));
	}

	[Fact]
	public void GenerateSignature_WithoutVerify_TakesCheatEngineAtItsWord()
	{
		TargetDouble target = new()
		{
			Inspection = Modules(Game)
		};
		target.LuaResult = _ => new UniqueAobProbe(true, "48 8B 05", 0);

		AobSignature signature = new AobTools(target.Dispatch).GenerateSignature("401000", verify: false, cancellationToken: Token);

		Assert.Equal((true, false, null), (signature.Unique, signature.Verified, signature.MatchCount));
		Assert.Empty(target.CallsTo("Patterns"));
	}

	private static Func<System.Reflection.MethodInfo, object?[], object?> Modules(params ModuleInfo[] modules)
	{
		return (method, _) => method.Name == nameof(IInspectionClient.GetModules)
			? modules.ToImmutableArray()
			: throw new XunitException($"Unexpected inspection call {method.Name}.");
	}

	private static PatternScanOutcome Outcome(int matches, ulong hostRows, ulong unread, bool truncated,
		ulong first = 0x401000)
	{
		ImmutableArray<Address> addresses =
			[.. Enumerable.Range(0, matches).Select(index => new Address(first + ((ulong) index * 0x100)))];
		PatternScanMetrics metrics = new(PatternScanScope.HostBoundedRange, hostRows, hostRows - unread, 0, matches,
			0, 0, unread, unread == 0, TimeSpan.FromMilliseconds(2), TimeSpan.FromMilliseconds(1));
		return new PatternScanOutcome(new AobScanResult(addresses, truncated), null, metrics,
			matches == 0 ? PatternScanHostOutcomeKind.NoMatches : PatternScanHostOutcomeKind.Matches,
			PatternScanRouteReason.ScopedRequestOnQualifiedTarget, true);
	}
}
