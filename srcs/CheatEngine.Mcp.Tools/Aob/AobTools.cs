using System.Collections.Immutable;
using System.ComponentModel;
using System.Globalization;

using CheatEngine.Client;
using CheatEngine.Client.Inspection;
using CheatEngine.Client.Results;
using CheatEngine.Client.Scanning;
using CheatEngine.Mcp.Core.Contract;
using CheatEngine.Mcp.Core.Values;
using CheatEngine.Mcp.Tools.Memory;
using CheatEngine.Mcp.Tools.Scan;
using CheatEngine.SDK.Engine.Inspection;
using CheatEngine.SDK.Engine.Values;

using ModelContextProtocol.Server;

using ClientAobPattern = CheatEngine.Client.Scanning.AobPattern;
using CorePattern = CheatEngine.Mcp.Core.Values.AobPattern;

namespace CheatEngine.Mcp.Tools.Aob;

/// <summary>The <c>aob_*</c> tools: byte-pattern scans and signature generation.</summary>
/// <remarks>
///     Each <c>aob_find</c> and <c>aob_find_value</c> call scans in one dispatch, through the activation's
///     <see cref="MappedMemoryOverride" />, which <c>scan_first</c> shares: with <c>includeMapped</c> every scan of the
///     call runs inside one Cheat Engine <c>MEM_MAPPED</c> scan override, which the same dispatch ends on every path,
///     and otherwise outside any override. A call that runs nested in the wait of another scan through that owner
///     (<c>scan_first</c>, <c>aob_find</c> or <c>aob_find_value</c>) is refused as <c>busy</c> when the two disagree on
///     <c>includeMapped</c>. A module-scoped call, which cannot include mapped memory, is not guarded: a module's image
///     holds only <c>MEM_IMAGE</c> regions, which the override never changes, as for <c>aob_generate_signature</c>.
/// </remarks>
[McpServerToolType]
public sealed class AobTools
{
	/// <summary>The most patterns one <c>aob_find</c> call scans.</summary>
	internal const int MaximumPatterns = 8;

	/// <summary>The most matches listed per pattern.</summary>
	internal const int MaximumLimit = 10000;

	/// <summary>
	///     The largest module whose signature Cheat Engine's <c>getUniqueAOB</c> generates; a larger module uses
	///     <see cref="ManagedSignatureGenerator" />.
	/// </summary>
	internal const long MaximumSignatureModuleBytes = 64 * 1024 * 1024;

	private const string IncludeMappedDescription =
		"Whether this call also scans mapped memory (MEM_MAPPED: file views and shared sections, such as an emulator's guest RAM), which Cheat Engine skips unless its scan settings include it. " +
		MappedMemoryOverride.IncludeMappedEffect + " Not with module, whose image is never mapped memory.";

	private const string OutsideModuleHint =
		"Pass an address inside a loaded module (see module_list); Cheat Engine would otherwise scan the whole address space on its main thread.";

	private readonly ToolDispatch _dispatch;
	private readonly MappedMemoryOverride _mappedMemory;

	/// <summary>Creates the tools; the constructor does no Cheat Engine work.</summary>
	/// <param name="dispatch">The activation's dispatch facade.</param>
	/// <param name="mappedMemory">
	///     The activation's <c>MEM_MAPPED</c> override owner, shared with <c>scan_first</c>; without one, this
	///     container keeps its own.
	/// </param>
	public AobTools(ToolDispatch dispatch, MappedMemoryOverride? mappedMemory = null)
	{
		ArgumentNullException.ThrowIfNull(dispatch);
		_dispatch = dispatch;
		_mappedMemory = mappedMemory ?? new MappedMemoryOverride(dispatch);
	}

	/// <summary>
	///     Finds up to 8 byte patterns in one dispatch, one bounded Cheat Engine scan per pattern, so the call holds
	///     Cheat Engine's main thread until its last scan ends.
	/// </summary>
	[McpServerTool(Name = CheatEngineToolNames.AobFind, Title = "Find byte patterns", ReadOnly = true,
		Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
	[McpMeta(McpDispatchClass.MetaKey, McpDispatchClass.HostScan)]
	[Description(
		"Find up to 8 array-of-bytes patterns (hex bytes and ?? wildcards) and report each pattern's matches, whether the count is exact and whether it is unique. The patterns are scanned one after another in one Cheat Engine call that blocks Cheat Engine until the last scan ends: without module or startAddress/endAddress each pattern scans the whole target, so scope the call, and use executable=required for code. Uniqueness needs limit 2 or more. Mapped memory, such as emulator RAM, is skipped unless Cheat Engine's scan settings include it or includeMapped is true.")]
	public AobFindResult Find(
		[Description("The patterns, 1 to 8, each up to 4096 byte positions, such as 48 8B ?? ?? ?? ?? 89 43 10.")]
		string[] patterns,
		[Description(
			"Scan only this loaded module's image, such as game.exe; faster and cannot match in another module.")]
		string? module = null,
		[Description("The first allowed match address or expression; requires endAddress.")]
		string? startAddress = null,
		[Description("The last allowed match address or expression; requires startAddress.")]
		string? endAddress = null,
		[Description("Whether matches must be in writable memory: required, excluded or any (default).")]
		ProtectionRequirement writable = ProtectionRequirement.Any,
		[Description("Whether matches must be in executable memory: required, excluded or any (default).")]
		ProtectionRequirement executable = ProtectionRequirement.Any,
		[Description("Whether matches must be in copy-on-write memory: required, excluded or any (default).")]
		ProtectionRequirement copyOnWrite = ProtectionRequirement.Any,
		[Description("Only match addresses divisible by this number, such as 4; not with lastDigits.")]
		int? alignment = null,
		[Description("Only match addresses whose hexadecimal form ends with these 1 to 16 digits; not with alignment.")]
		string? lastDigits = null,
		[Description("The most matches to list per pattern, 1 to 10000.")]
		int limit = 1000,
		[Description(IncludeMappedDescription)]
		bool includeMapped = false,
		CancellationToken cancellationToken = default)
	{
		if (patterns is null || patterns.Length == 0)
		{
			throw CheatEngineToolException.InvalidArgument("patterns", "must list at least one pattern.");
		}

		if (patterns.Length > MaximumPatterns)
		{
			throw CheatEngineToolException.LimitExceeded("patterns", $"accepts at most {MaximumPatterns} patterns.");
		}

		ClientAobPattern[] scanned = new ClientAobPattern[patterns.Length];
		for (int index = 0; index < patterns.Length; index++)
		{
			string parameter = $"patterns[{index.ToString(CultureInfo.InvariantCulture)}]";
			scanned[index] = new ClientAobPattern(CorePattern.Normalize(patterns[index], parameter));
		}

		ScanBounds scope = Scope(module, startAddress, endAddress, includeMapped);
		MemoryTargets.RequireRange(limit, "limit", 1, MaximumLimit);
		ScanProtectionFilter protection = new(Requirement(executable), Requirement(copyOnWrite),
			Requirement(writable));
		ScanAlignment rule = Alignment(alignment, lastDigits);

		// One dispatch scans every pattern, so one MEM_MAPPED override, or the check that none is on, covers them all.
		AobPatternResult[] results = _dispatch.Run(CheatEngineToolNames.AobFind, token =>
		{
			ICheatEngineClient client = _dispatch.Client;
			AobScanRange? range = Range(client, scope, token);
			return Scans(CheatEngineToolNames.AobFind, includeMapped, scope.Module, () =>
			{
				AobPatternResult[] found = new AobPatternResult[scanned.Length];
				for (int index = 0; index < scanned.Length; index++)
				{
					token.ThrowIfCancellationRequested();
					found[index] = Scan(client, scanned[index], scope.Module, range, limit, protection, rule, token);
				}

				return found;
			}, token);
		}, cancellationToken);
		return new AobFindResult(results);
	}

	/// <summary>Encodes one typed value as the bytes the target would hold and finds them in one scan.</summary>
	[McpServerTool(Name = CheatEngineToolNames.AobFindValue, Title = "Find value bytes", ReadOnly = true,
		Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
	[McpMeta(McpDispatchClass.MetaKey, McpDispatchClass.HostScan)]
	[Description(
		"Encode one typed value as the little-endian bytes the target would hold (integers in two's complement, float and double by their exact bits, a pointer at the target's pointer size, string as UTF-8 and wstring as UTF-16 without a terminator) and find those bytes with one array-of-bytes scan, without creating scanner state. Returns the pattern used and its matches. Only exact bit patterns match: for a rounded or approximate value use scan_first, and for raw bytes or wildcards use aob_find. The scan blocks Cheat Engine while it runs: without module or startAddress/endAddress it scans the whole target, so scope it. alignment defaults to the value's size up to 4 for numbers and pointers; 1-byte integers and strings match at any address. Mapped memory is skipped unless Cheat Engine's scan settings include it or includeMapped is true. Uniqueness needs limit 2 or more.")]
	public AobValueResult FindValue(
		[Description(
			"The value type to encode: int8 to uint64, float, double, pointer, string (UTF-8) or wstring (UTF-16); bytes is refused, use aob_find.")]
		McpValueType valueType,
		[Description(
			"The value as text: decimal or 0x hexadecimal integers, invariant floats (NaN, Infinity), a hexadecimal pointer or the text of a string, at most 4096 encoded bytes.")]
		string value,
		[Description(
			"Scan only this loaded module's image, such as game.exe; faster and cannot match in another module.")]
		string? module = null,
		[Description("The first allowed match address or expression; requires endAddress.")]
		string? startAddress = null,
		[Description("The last allowed match address or expression; requires startAddress.")]
		string? endAddress = null,
		[Description("Whether matches must be in writable memory: required, excluded or any (default).")]
		ProtectionRequirement writable = ProtectionRequirement.Any,
		[Description("Whether matches must be in executable memory: required, excluded or any (default).")]
		ProtectionRequirement executable = ProtectionRequirement.Any,
		[Description("Whether matches must be in copy-on-write memory: required, excluded or any (default).")]
		ProtectionRequirement copyOnWrite = ProtectionRequirement.Any,
		[Description(
			"Only match addresses divisible by this number, 1 to 65536; defaults to the value's size up to 4 for numbers and pointers, and to no alignment filter (any address) for 1-byte integers and strings.")]
		int? alignment = null,
		[Description("The most matches to list, 1 to 10000.")]
		int limit = 100,
		[Description(IncludeMappedDescription)]
		bool includeMapped = false,
		CancellationToken cancellationToken = default)
	{
		MemoryTargets.RequireType(valueType, "valueType");
		if (valueType is McpValueType.Bytes)
		{
			throw CheatEngineToolException.InvalidArgument("valueType",
				"bytes is not an encoded value; find byte patterns with aob_find.");
		}

		if (value is null)
		{
			throw CheatEngineToolException.InvalidArgument("value", "is required.");
		}

		// A pointer is checked here as 64-bit and sized once the target's pointer size is known.
		byte[] encoded = Encoded(valueType, value, 8);
		ScanBounds scope = Scope(module, startAddress, endAddress, includeMapped);
		MemoryTargets.RequireRange(limit, "limit", 1, MaximumLimit);
		ScanProtectionFilter protection = new(Requirement(executable), Requirement(copyOnWrite),
			Requirement(writable));
		ScanAlignment rule = alignment is null ? DefaultAlignment(valueType) : Alignment(alignment, null);
		AobPatternResult result = _dispatch.Run(CheatEngineToolNames.AobFindValue, token =>
		{
			ICheatEngineClient client = _dispatch.Client;
			byte[] bytes = valueType is McpValueType.Pointer
				? Encoded(valueType, value, MemoryTargets.PointerBytes(client, token))
				: encoded;
			ClientAobPattern pattern = new(HexFormat.Bytes(bytes));
			AobScanRange? range = Range(client, scope, token);
			return Scans(CheatEngineToolNames.AobFindValue, includeMapped, scope.Module,
				() => [Scan(client, pattern, scope.Module, range, limit, protection, rule, token)], token)[0];
		}, cancellationToken);
		return new AobValueResult(valueType, value, result);
	}

	/// <summary>Generates a unique signature for an address inside a module and verifies it.</summary>
	[McpServerTool(Name = CheatEngineToolNames.AobGenerateSignature, Title = "Generate an AOB signature",
		ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
	[McpMeta(McpDispatchClass.MetaKey, McpDispatchClass.HostScan)]
	[Description(
		"Generate an array-of-bytes signature that finds an address inside a loaded module and prove it with bounded scans of that module; generator says which ran. In a module of at most 64 MiB, Cheat Engine's getUniqueAOB builds it from up to 20 bytes on each side of the address (see offset), then a separate scan verifies it. In a larger module, where getUniqueAOB would list and read every match of the instruction bytes, a managed generator starts at the address and adds whole instructions, wildcarding RIP-relative, absolute and branch displacements and 4- or 8-byte values of at least 0x10000, until a module scan finds it once, for at most 64 bytes and 6 scans. Blocks Cheat Engine while it scans; review the pattern before relying on it.")]
	public AobSignature GenerateSignature(
		[Description("An address or Cheat Engine address expression inside a loaded module, such as game.exe+1C0.")]
		string address,
		[Description("The module that must contain the address; defaults to the module that contains it.")]
		string? module = null,
		[Description(
			"Whether to verify Cheat Engine's signature with a separate bounded scan of the module; a managed signature is always verified, because its scans build it.")]
		bool verify = true,
		CancellationToken cancellationToken = default)
	{
		string expression = MemoryTargets.RequireExpression(address, "address");
		string? moduleName = MemoryTargets.OptionalModule(module);
		return _dispatch.Run(CheatEngineToolNames.AobGenerateSignature, token =>
		{
			ICheatEngineClient client = _dispatch.Client;
			Address target = MemoryTargets.Resolve(client, expression, "address", token);
			ModuleInfo owner = ContainingModule(client, target, moduleName, token);
			return owner.ImageSize!.Value.Value <= MaximumSignatureModuleBytes
				? CheatEngineSignature(client, target, owner, verify, token)
				: ManagedSignatureGenerator.Generate(client, target, owner, token);
		}, cancellationToken);
	}

	/// <summary>
	///     Scans one module for a pattern with limit 2, inside a dispatch. A scan that was not bounded to the module or
	///     not confirmed in one target incarnation is refused, since it cannot prove uniqueness.
	/// </summary>
	/// <param name="client">The activation's Client.</param>
	/// <param name="pattern">A normalized pattern.</param>
	/// <param name="owner">The module to scan.</param>
	/// <param name="exact">Whether the listed matches are every match in the module.</param>
	/// <param name="cancellationToken">The token of the enclosing dispatch body.</param>
	/// <returns>The copied matches, at most 2.</returns>
	internal static AobScanResult ModuleScan(ICheatEngineClient client, string pattern, ModuleInfo owner,
		out bool exact, CancellationToken cancellationToken)
	{
		PatternScanOutcome outcome = client.Patterns.ScanDetailed(
			new AobScanRequest(new ClientAobPattern(pattern), 2, new ModuleName(owner.Name)), cancellationToken);
		if (!outcome.IsSuccess)
		{
			throw CheatEngineToolException.FromFailure(outcome.Failure!.Value,
				client.Stopping.IsCancellationRequested);
		}

		AobScanResult result = outcome.Result!.Value;
		PatternScanMetrics metrics = outcome.Metrics!.Value;
		if (metrics.Scope is not PatternScanScope.HostBoundedRange || !outcome.TargetIdentityVerified)
		{
			throw new CheatEngineToolException(new ToolError(ToolErrorKind.TargetChanged,
				"Cheat Engine could not verify the signature in one confirmed target incarnation with a bounded module scan.",
				CheatEngineToolNames.AobGenerateSignature, ToolHostEffect.Completed, false,
				"Attach to a qualified local target, then repeat the signature generation."));
		}

		exact = SignatureBuilder.IsExact(result, metrics);
		return result;
	}

	/// <summary>Runs Cheat Engine's <c>getUniqueAOB</c> and verifies its signature. Inside a dispatch.</summary>
	private AobSignature CheatEngineSignature(ICheatEngineClient client, Address target, ModuleInfo owner,
		bool verify, CancellationToken token)
	{
		UniqueAobProbe probe = _dispatch.ExecuteLua(CheatEngineToolNames.AobGenerateSignature,
			AobScripts.UniqueAob, AobJsonContext.Default.UniqueAobProbe, token, target.ToUInt64());
		string resolved = HexFormat.Address(target);
		if (!probe.Found)
		{
			return new AobSignature(resolved, owner.Name, AobSignatureGenerator.CheatEngine, false, false,
				TriedPattern: SignatureBuilder.NormalizeCheatEnginePattern(probe.Tried) ?? probe.Tried);
		}

		string pattern = SignatureBuilder.NormalizeCheatEnginePattern(probe.Pattern) ??
						 throw new CheatEngineToolException(new ToolError(ToolErrorKind.HostRefused,
							 "Cheat Engine returned a signature that is not a valid AOB pattern.",
							 CheatEngineToolNames.AobGenerateSignature, ToolHostEffect.Completed, false,
							 "Build the pattern by hand from code_disassemble and prove it with aob_find."));
		int length = SignatureBuilder.ByteLength(pattern);
		long offset = probe.Offset ?? throw InvalidSignature(
			"Cheat Engine returned a signature without an offset.");
		if (offset < 0 || offset >= length || (ulong) offset > target.ToUInt64())
		{
			throw InvalidSignature(
				"Cheat Engine returned a signature whose offset does not place the address inside it.");
		}

		Address start = target - offset;
		if (!verify)
		{
			return new AobSignature(resolved, owner.Name, AobSignatureGenerator.CheatEngine, true, false, pattern,
				HexFormat.Address(start), (int) offset, length);
		}

		AobScanResult result = ModuleScan(client, pattern, owner, out bool exact, token);
		ImmutableArray<Address> matches = result.Matches;
		bool unique = matches.Length == 1 && matches[0] == start && exact;
		return new AobSignature(resolved, owner.Name, AobSignatureGenerator.CheatEngine, unique, true, pattern,
			HexFormat.Address(start), (int) offset, length, matches.Length);
	}

	private static CheatEngineToolException InvalidSignature(string message)
	{
		return new CheatEngineToolException(new ToolError(ToolErrorKind.HostRefused, message,
			CheatEngineToolNames.AobGenerateSignature, ToolHostEffect.Completed, false,
			"Build the pattern by hand from code_disassemble and prove it with aob_find."));
	}

	private static ModuleInfo ContainingModule(ICheatEngineClient client, Address target, string? moduleName,
		CancellationToken cancellationToken)
	{
		ImmutableArray<ModuleInfo> modules = client.Inspection.GetModules(
			new InspectionCollectionRequest(MemoryTargets.MaximumModules), null, cancellationToken);
		if (moduleName is null)
		{
			return MemoryTargets.ContainingModule(modules, target) ??
				   throw CheatEngineToolException.InvalidArgument("address",
					   $"{HexFormat.Address(target)} is not inside a loaded module of known size.", OutsideModuleHint);
		}

		ModuleInfo owner = MemoryTargets.FindModule(modules, moduleName) ??
						   throw CheatEngineToolException.NotFound($"No loaded module is named '{moduleName}'.",
							   "List the loaded modules with module_list.");
		if (owner.ImageSize is null)
		{
			throw CheatEngineToolException.Unsupported(
				$"Cheat Engine does not report the size of '{owner.Name}', so the address cannot be proven inside it.");
		}

		return MemoryTargets.Contains(owner, target)
			? owner
			: throw CheatEngineToolException.InvalidArgument("module",
				$"'{owner.Name}' does not contain the address {HexFormat.Address(target)}.", OutsideModuleHint);
	}

	/// <summary>Checks the module, range and mapped-memory arguments of the scans, before any dispatch.</summary>
	private static ScanBounds Scope(string? module, string? startAddress, string? endAddress, bool includeMapped)
	{
		string? moduleName = MemoryTargets.OptionalModule(module);
		if (includeMapped && moduleName is not null)
		{
			throw CheatEngineToolException.InvalidArgument("includeMapped",
				"cannot be combined with module: a module's image is never mapped memory.",
				"Scope the scan with startAddress and endAddress, or leave module, startAddress and endAddress out.");
		}

		if ((startAddress is null) != (endAddress is null))
		{
			throw CheatEngineToolException.InvalidArgument(startAddress is null ? "startAddress" : "endAddress",
				"startAddress and endAddress must be given together.");
		}

		string? start = startAddress is null ? null : MemoryTargets.RequireExpression(startAddress, "startAddress");
		string? end = endAddress is null ? null : MemoryTargets.RequireExpression(endAddress, "endAddress");
		return new ScanBounds(moduleName, start, end);
	}

	/// <summary>
	///     Runs the scans of one call inside the current dispatch through the activation's
	///     <see cref="MappedMemoryOverride" />: with <paramref name="includeMapped" /> inside Cheat Engine's
	///     <c>MEM_MAPPED</c> scan override, which it ends on every path before the dispatch returns, and otherwise
	///     outside any override. A call nested in a scan that disagrees on <c>includeMapped</c> is refused as
	///     <c>busy</c>. A module-scoped call runs its scans directly.
	/// </summary>
	/// <param name="operation">The tool name, used in the Lua chunk names and errors.</param>
	/// <param name="includeMapped">Whether the scans also cover mapped memory.</param>
	/// <param name="module">The module whose image is scanned, or <see langword="null" />.</param>
	/// <param name="scan">The scans, one result per scan in the order of the tool's output.</param>
	/// <param name="token">The token of the enclosing dispatch body.</param>
	/// <returns>The scans' results.</returns>
	private AobPatternResult[] Scans(string operation, bool includeMapped, string? module,
		Func<AobPatternResult[]> scan, CancellationToken token)
	{
		if (module is not null)
		{
			// Scope refused includeMapped with a module. A module's image holds only MEM_IMAGE regions, so the
			// MEM_MAPPED override cannot change these scans, whether or not they nest in a scan with includeMapped,
			// like the module scans of aob_generate_signature.
			return scan();
		}

		return includeMapped
			? _mappedMemory.Include(operation, scan,
				static results => new AobMappedOverrideFailure(results is not null, results),
				AobJsonContext.Default.AobMappedOverrideFailure, token)
			: _mappedMemory.Follow(operation, scan);
	}

	/// <summary>
	///     Resolves the range of a call's scans once, inside the dispatch and before any scan-region override, so an
	///     unresolvable expression leaves Cheat Engine untouched.
	/// </summary>
	private static AobScanRange? Range(ICheatEngineClient client, ScanBounds scope, CancellationToken token)
	{
		if (scope.Start is null || scope.End is null)
		{
			return null;
		}

		Address first = MemoryTargets.Resolve(client, scope.Start, "startAddress", token);
		Address last = MemoryTargets.Resolve(client, scope.End, "endAddress", token);
		return first <= last
			? new AobScanRange(first, last)
			: throw CheatEngineToolException.InvalidArgument("endAddress", "must not be below startAddress.");
	}

	/// <summary>Runs one bounded pattern scan and describes its matches. Inside a dispatch.</summary>
	private static AobPatternResult Scan(ICheatEngineClient client, ClientAobPattern pattern, string? module,
		AobScanRange? range, int limit, ScanProtectionFilter protection, ScanAlignment rule, CancellationToken token)
	{
		ModuleName? moduleScope = module is null ? null : new ModuleName(module);
		PatternScanOutcome outcome = client.Patterns.ScanDetailed(
			new AobScanRequest(pattern, limit, moduleScope, range, protection, rule), token);
		return Describe(client, pattern, outcome, moduleScope is null && range is null);
	}

	/// <summary>Encodes a value for <c>aob_find_value</c>, refusing an empty or overlong encoding.</summary>
	private static byte[] Encoded(McpValueType valueType, string value, int pointerBytes)
	{
		byte[] bytes = McpValueCodec.Encode(valueType, value, pointerBytes, "value");
		if (bytes.Length == 0)
		{
			throw CheatEngineToolException.InvalidArgument("value", "must not be an empty string.");
		}

		return bytes.Length <= CorePattern.MaxBytes
			? bytes
			: throw CheatEngineToolException.LimitExceeded("value",
				$"encodes to {bytes.Length.ToString(CultureInfo.InvariantCulture)} bytes; a pattern holds at most {CorePattern.MaxBytes}.");
	}

	/// <summary>The natural alignment of a value type, capped at 4; strings are not aligned.</summary>
	private static ScanAlignment DefaultAlignment(McpValueType valueType)
	{
		return valueType switch
		{
			McpValueType.Int16 or McpValueType.UInt16 => ScanAlignment.AlignedTo(2),
			McpValueType.Int32 or McpValueType.UInt32 or McpValueType.Float or McpValueType.Int64
				or McpValueType.UInt64 or McpValueType.Double or McpValueType.Pointer => ScanAlignment.AlignedTo(4),
			_ => ScanAlignment.None
		};
	}

	private static AobPatternResult Describe(ICheatEngineClient client, ClientAobPattern pattern,
		PatternScanOutcome outcome, bool unscoped)
	{
		if (!outcome.IsSuccess)
		{
			CheatEngineFailure failure = outcome.Failure!.Value;
			// A global AOBScan returns nil both for no match and for a failure: report zero, not proven.
			return failure.Kind is CheatEngineFailureKind.IndeterminateHostResult
				? new AobPatternResult(pattern.Value, 0, false, false,
					outcome.Metrics is { } partial ? SignatureBuilder.Scope(partial.Scope) :
					unscoped ? AobScanScope.GlobalScan : AobScanScope.FilteredGlobalScan,
					false, outcome.Metrics is { } timed ? SignatureBuilder.ElapsedMilliseconds(timed) : 0, [])
				: throw CheatEngineToolException.FromFailure(failure, client.Stopping.IsCancellationRequested);
		}

		AobScanResult result = outcome.Result!.Value;
		PatternScanMetrics metrics = outcome.Metrics!.Value;
		string[] matches = [.. result.Matches.Select(static match => HexFormat.Address(match))];
		bool exact = SignatureBuilder.IsExact(result, metrics);
		return new AobPatternResult(pattern.Value, matches.Length, exact, exact && matches.Length == 1,
			SignatureBuilder.Scope(metrics.Scope), outcome.TargetIdentityVerified,
			SignatureBuilder.ElapsedMilliseconds(metrics), matches);
	}

	private static ScanProtectionRequirement Requirement(ProtectionRequirement requirement)
	{
		return requirement switch
		{
			ProtectionRequirement.Required => ScanProtectionRequirement.Required,
			ProtectionRequirement.Excluded => ScanProtectionRequirement.Excluded,
			// Any leaves the flag out of the request, which Cheat Engine documents as either.
			_ => ScanProtectionRequirement.Unspecified
		};
	}

	private static ScanAlignment Alignment(int? alignment, string? lastDigits)
	{
		if (alignment is not null && lastDigits is not null)
		{
			throw CheatEngineToolException.InvalidArgument("lastDigits", "cannot be combined with alignment.");
		}

		if (alignment is { } divisor)
		{
			return ScanAlignment.AlignedTo((int) MemoryTargets.RequireRange(divisor, "alignment", 1, 65536));
		}

		if (lastDigits is null)
		{
			return ScanAlignment.None;
		}

		string digits = lastDigits.Trim();
		if (digits.Length is < 1 or > 16 || !digits.All(char.IsAsciiHexDigit))
		{
			throw CheatEngineToolException.InvalidArgument("lastDigits", "must be 1 to 16 hexadecimal digits.");
		}

		return ScanAlignment.LastDigits(digits);
	}

	/// <summary>The checked module and range arguments of one scan.</summary>
	/// <param name="Module">The module whose image is scanned, or <see langword="null" />.</param>
	/// <param name="Start">The first allowed match expression, or <see langword="null" />.</param>
	/// <param name="End">The last allowed match expression, or <see langword="null" />.</param>
	private sealed record ScanBounds(string? Module, string? Start, string? End);
}
