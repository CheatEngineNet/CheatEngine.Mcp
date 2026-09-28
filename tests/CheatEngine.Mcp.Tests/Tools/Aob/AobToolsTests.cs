using System.Collections.Immutable;
using System.ComponentModel;
using System.Reflection;
using System.Text.Json;

using CheatEngine.Client.Inspection;
using CheatEngine.Client.Results;
using CheatEngine.Client.Scanning;
using CheatEngine.Mcp.Core.Contract;
using CheatEngine.Mcp.Core.Values;
using CheatEngine.Mcp.Tests.Tools.Memory;
using CheatEngine.Mcp.Tools.Aob;
using CheatEngine.Mcp.Tools.Memory;
using CheatEngine.SDK.Engine.Inspection;
using CheatEngine.SDK.Engine.Runtime;
using CheatEngine.SDK.Engine.Values;

using Xunit.Sdk;

namespace CheatEngine.Mcp.Tests.Tools.Aob;

/// <summary>
///     AOB scans, value scans and signatures: counts derived from the scan metrics, value encodings, and the module
///     guard of getUniqueAOB.
/// </summary>
public sealed class AobToolsTests
{
	private static readonly ModuleInfo Game =
		new("game.exe", new Address(0x400000), new MemorySize(0x10000), true, @"C:\game\game.exe");

	private static CancellationToken Token => TestContext.Current.CancellationToken;

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
	public void Find_Options_ReachTheClientRequestAndOneDispatchScansEveryPattern()
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
		// Every pattern scans the one range, resolved once in the call's single dispatch.
		Assert.Equal(first.Range, requests[1].Range);
		Assert.Equal(["Inspection.TryResolveAddress", "Inspection.TryResolveAddress"], target.CallsTo("Inspection"));
		Assert.Equal(1, target.Dispatcher.Calls);
	}

	[Fact]
	public void Find_GlobalScanWithoutAResultList_ReportsZeroMatchesNotProven()
	{
		TargetDouble target = new();
		target.Patterns = (_, _) => new PatternScanOutcome(null,
			new CheatEngineFailure(CheatEngineFailureKind.IndeterminateHostResult, "Patterns.Scan",
				"AOBScan returned nil.", hostEffect: CheatEngineHostEffect.Completed), null,
			PatternScanHostOutcomeKind.NoResult, PatternScanRouteReason.UnscopedRequest, false);

		AobPatternResult result =
			Assert.Single(new AobTools(target.Dispatch).Find(["CC CC CC"], cancellationToken: Token).Results);

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

	[Theory]
	[InlineData(McpValueType.Int32, "100", "64 00 00 00", 4)]
	[InlineData(McpValueType.UInt32, "0xDEADBEEF", "EF BE AD DE", 4)]
	[InlineData(McpValueType.Int16, "-2", "FE FF", 2)]
	[InlineData(McpValueType.UInt8, "0x7F", "7F", 1)]
	[InlineData(McpValueType.Int8, "-1", "FF", 1)]
	[InlineData(McpValueType.Int64, "1", "01 00 00 00 00 00 00 00", 4)]
	[InlineData(McpValueType.Float, "1.5", "00 00 C0 3F", 4)]
	[InlineData(McpValueType.Double, "1", "00 00 00 00 00 00 F0 3F", 4)]
	[InlineData(McpValueType.String, "Hi", "48 69", 1)]
	[InlineData(McpValueType.WString, "Hi", "48 00 69 00", 1)]
	public void FindValue_Value_IsEncodedAsTheTargetBytesWithItsDefaultAlignment(McpValueType valueType,
		string value, string pattern, int alignment)
	{
		TargetDouble target = new();
		AobScanRequest? request = null;
		target.Patterns = (_, arguments) =>
		{
			request = (AobScanRequest) arguments[0]!;
			return Outcome(1, 1, 0, false);
		};

		AobValueResult result = new AobTools(target.Dispatch).FindValue(valueType, value, "game.exe",
			cancellationToken: Token);

		Assert.Equal((valueType, value, pattern), (result.ValueType, result.Value, result.Result.Pattern));
		Assert.Equal(pattern, request!.Value.Pattern.Value);
		ScanAlignment sent = request.Value.Alignment;
		Assert.Equal(alignment == 1 ? ScanAlignmentMode.None : ScanAlignmentMode.AlignedTo, sent.Mode);
		Assert.Equal(alignment == 1 ? ScanAlignment.None : ScanAlignment.AlignedTo(alignment), sent);
		Assert.Equal(["401000"], result.Result.Matches);
		Assert.Empty(target.CallsTo("Processes"));
		Assert.Equal(1, target.Dispatcher.Calls);
	}

	[Theory]
	[InlineData(8, "78 56 34 12 00 00 00 00")]
	[InlineData(4, "78 56 34 12")]
	public void FindValue_Pointer_IsSizedByTheTargetInsideTheDispatch(int pointerBytes, string pattern)
	{
		TargetDouble target = new()
		{
			Bitness = pointerBytes == 4 ? PointerSize.Bit32 : PointerSize.Bit64
		};
		AobScanRequest? request = null;
		target.Patterns = (_, arguments) =>
		{
			request = (AobScanRequest) arguments[0]!;
			return Outcome(0, 0, 0, false);
		};

		AobValueResult result = new AobTools(target.Dispatch).FindValue(McpValueType.Pointer, "12345678",
			"game.exe", cancellationToken: Token);

		Assert.Equal(pattern, result.Result.Pattern);
		Assert.Equal((ScanAlignmentMode.AlignedTo, 4), (request!.Value.Alignment.Mode, request.Value.Alignment.Divisor));
		Assert.Equal((0, true), (result.Result.Count, result.Result.Exact));
		Assert.Equal(["Processes.GetCurrentProcess", "Patterns.ScanDetailed"], target.Calls);
	}

	[Fact]
	public void FindValue_PointerAbove4GiBOnA32BitTarget_IsRefusedBeforeTheScan()
	{
		TargetDouble target = new()
		{
			Bitness = PointerSize.Bit32
		};

		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() =>
			new AobTools(target.Dispatch).FindValue(McpValueType.Pointer, "7FF612345678", "game.exe",
				cancellationToken: Token));

		Assert.Equal(ToolErrorKind.InvalidArgument, exception.Error.Kind);
		Assert.StartsWith("value", exception.Error.Message, StringComparison.Ordinal);
		Assert.Empty(target.CallsTo("Patterns"));
	}

	[Fact]
	public void FindValue_Options_ReachTheClientRequest()
	{
		TargetDouble target = new();
		AobScanRequest? request = null;
		target.Patterns = (_, arguments) =>
		{
			request = (AobScanRequest) arguments[0]!;
			return Outcome(2, 2, 0, false);
		};

		AobValueResult result = new AobTools(target.Dispatch).FindValue(McpValueType.Float, "1.5", null, "401000",
			"402000", ProtectionRequirement.Required, ProtectionRequirement.Excluded, ProtectionRequirement.Required,
			1, 2, false, Token);

		AobScanRequest sent = request!.Value;
		Assert.Null(sent.Module);
		Assert.Equal((new Address(0x401000), new Address(0x402000), 2),
			(sent.Range!.Value.Start, sent.Range.Value.End, sent.MaximumResults));
		Assert.Equal((ScanProtectionRequirement.Excluded, ScanProtectionRequirement.Required,
				ScanProtectionRequirement.Required),
			(sent.Protection.Executable, sent.Protection.Writable, sent.Protection.CopyOnWrite));
		Assert.Equal((ScanAlignmentMode.AlignedTo, 1), (sent.Alignment.Mode, sent.Alignment.Divisor));
		Assert.Equal((2, true, false), (result.Result.Count, result.Result.Exact, result.Result.Unique));
	}

	[Fact]
	public void FindValue_UnscopedScanWithoutAResultList_ReportsZeroMatchesNotProven()
	{
		TargetDouble target = new();
		target.Patterns = (_, _) => new PatternScanOutcome(null,
			new CheatEngineFailure(CheatEngineFailureKind.IndeterminateHostResult, "Patterns.Scan",
				"AOBScan returned nil.", hostEffect: CheatEngineHostEffect.Completed), null,
			PatternScanHostOutcomeKind.NoResult, PatternScanRouteReason.UnscopedRequest, false);

		AobValueResult result = new AobTools(target.Dispatch).FindValue(McpValueType.Int32, "7",
			cancellationToken: Token);

		Assert.Equal((0, false, AobScanScope.GlobalScan), (result.Result.Count, result.Result.Exact,
			result.Result.Scope));
	}

	[Theory]
	[InlineData(McpValueType.Bytes, "90 90", null, null, null, 100, ToolErrorKind.InvalidArgument)]
	[InlineData((McpValueType) 99, "1", null, null, null, 100, ToolErrorKind.InvalidArgument)]
	[InlineData(McpValueType.String, "", null, null, null, 100, ToolErrorKind.InvalidArgument)]
	[InlineData(McpValueType.Int32, "abc", null, null, null, 100, ToolErrorKind.InvalidArgument)]
	[InlineData(McpValueType.UInt8, "256", null, null, null, 100, ToolErrorKind.InvalidArgument)]
	[InlineData(McpValueType.Pointer, "not an address", null, null, null, 100, ToolErrorKind.InvalidArgument)]
	[InlineData(McpValueType.Int32, "1", "401000", null, null, 100, ToolErrorKind.InvalidArgument)]
	[InlineData(McpValueType.Int32, "1", null, null, 0, 100, ToolErrorKind.InvalidArgument)]
	[InlineData(McpValueType.Int32, "1", null, null, 65537, 100, ToolErrorKind.LimitExceeded)]
	[InlineData(McpValueType.Int32, "1", null, null, null, 0, ToolErrorKind.InvalidArgument)]
	[InlineData(McpValueType.Int32, "1", null, null, null, 10001, ToolErrorKind.LimitExceeded)]
	public void FindValue_InvalidArguments_RefuseBeforeAnyDispatch(McpValueType valueType, string value,
		string? start, string? end, int? alignment, int limit, ToolErrorKind kind)
	{
		TargetDouble target = new();

		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() =>
			new AobTools(target.Dispatch).FindValue(valueType, value, startAddress: start, endAddress: end,
				alignment: alignment, limit: limit, cancellationToken: Token));

		Assert.Equal(kind, exception.Error.Kind);
		Assert.Equal(0, target.Dispatcher.Calls);
	}

	[Theory]
	[InlineData(McpValueType.String, 4097)]
	[InlineData(McpValueType.WString, 2049)]
	public void FindValue_ValueEncodingPastThePatternLimit_IsLimitExceeded(McpValueType valueType, int length)
	{
		TargetDouble target = new();

		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() =>
			new AobTools(target.Dispatch).FindValue(valueType, new string('a', length), "game.exe",
				cancellationToken: Token));

		Assert.Equal(ToolErrorKind.LimitExceeded, exception.Error.Kind);
		Assert.StartsWith("value", exception.Error.Message, StringComparison.Ordinal);
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
			return Outcome(1, 1, 0, false);
		};

		AobSignature signature = new AobTools(target.Dispatch).GenerateSignature("401002", cancellationToken: Token);

		Assert.Equal(
			new AobSignature("401002", "game.exe", AobSignatureGenerator.CheatEngine, true, true, "48 8B ?? 05",
				"401000", 2, 4, 1), signature);
		Assert.Contains("[1] = 0x401002", source, StringComparison.Ordinal);
		Assert.Equal(("game.exe", 2, "48 8B ?? 05"), (verification!.Value.Module!.Value.Value,
			verification.Value.MaximumResults,
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
		target.Patterns = (_, _) => Outcome(2, 3, 0, true);

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

		Assert.Equal(
			new AobSignature("401000", "game.exe", AobSignatureGenerator.CheatEngine, false, false,
				TriedPattern: "48 8B ?? 05"), signature);
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

		AobSignature signature =
			new AobTools(target.Dispatch).GenerateSignature("401000", verify: false, cancellationToken: Token);

		Assert.Equal((true, false, null), (signature.Unique, signature.Verified, signature.MatchCount));
		Assert.Empty(target.CallsTo("Patterns"));
	}

	[Fact]
	public void GenerateSignature_ModuleOfExactly64MiB_StillUsesCheatEngine()
	{
		ModuleInfo limit = new("limit.dll", new Address(0x10000000), new MemorySize(64UL * 1024 * 1024), true,
			@"C:\game\limit.dll");
		TargetDouble target = new()
		{
			Inspection = Modules(Game, limit),
			LuaResult = _ => new UniqueAobProbe(true, "48 8B 05", 0)
		};

		AobSignature signature =
			new AobTools(target.Dispatch).GenerateSignature("10000100", verify: false, cancellationToken: Token);

		Assert.Equal((AobSignatureGenerator.CheatEngine, true), (signature.Generator, signature.Unique));
		Assert.Equal(1, target.LuaCalls);
	}

	[Fact]
	public void GenerateSignature_Generator_IsASnakeCaseContractValue()
	{
		string cheatEngine = JsonSerializer.Serialize(
			new AobSignature("1", "a.dll", AobSignatureGenerator.CheatEngine, false, false),
			AobJsonContext.Default.AobSignature);
		string managed = JsonSerializer.Serialize(
			new AobSignature("1", "a.dll", AobSignatureGenerator.Managed, false, false),
			AobJsonContext.Default.AobSignature);

		Assert.Contains("\"generator\":\"cheat_engine\"", cheatEngine, StringComparison.Ordinal);
		Assert.Contains("\"generator\":\"managed\"", managed, StringComparison.Ordinal);
	}

	[Fact]
	public void FindValue_AlignmentDescription_SaysStringsHaveNoAlignmentFilter()
	{
		MethodInfo method = typeof(AobTools).GetMethod(nameof(AobTools.FindValue))!;
		string tool = method.GetCustomAttribute<DescriptionAttribute>()!.Description;
		string alignment = method.GetParameters().Single(static parameter => parameter.Name == "alignment")
			.GetCustomAttribute<DescriptionAttribute>()!.Description;

		Assert.DoesNotContain("to 1 for strings", tool, StringComparison.Ordinal);
		Assert.Contains("1-byte integers and strings match at any address", tool, StringComparison.Ordinal);
		Assert.Contains("no alignment filter (any address) for 1-byte integers and strings", alignment,
			StringComparison.Ordinal);
	}

	[Fact]
	public void Find_Description_SaysOneCallBlocksCheatEngineUntilItsLastPatternScanEnds()
	{
		string tool = typeof(AobTools).GetMethod(nameof(AobTools.Find))!.GetCustomAttribute<DescriptionAttribute>()!
			.Description;

		// Every pattern scans in the call's one dispatch, so the worst case is the sum of all its scans.
		Assert.Contains("The patterns are scanned one after another in one Cheat Engine call that blocks Cheat " +
			"Engine until the last scan ends", tool, StringComparison.Ordinal);
		Assert.DoesNotContain("Each pattern is one Cheat Engine scan", tool, StringComparison.Ordinal);
	}

	private static Func<MethodInfo, object?[], object?> Modules(params ModuleInfo[] modules)
	{
		return (method, _) => method.Name == nameof(IInspectionClient.GetModules)
			? modules.ToImmutableArray()
			: throw new XunitException($"Unexpected inspection call {method.Name}.");
	}

	/// <summary>A bounded, target-verified scan outcome with <paramref name="matches" /> matches 0x100 apart.</summary>
	internal static PatternScanOutcome Outcome(int matches, ulong hostRows, ulong unread, bool truncated,
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
