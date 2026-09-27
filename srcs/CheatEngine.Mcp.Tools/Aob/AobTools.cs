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
using CheatEngine.SDK.Engine.Inspection;
using CheatEngine.SDK.Engine.Values;

using ModelContextProtocol.Server;

using ClientAobPattern = CheatEngine.Client.Scanning.AobPattern;
using CorePattern = CheatEngine.Mcp.Core.Values.AobPattern;

namespace CheatEngine.Mcp.Tools.Aob;

/// <summary>The <c>aob_*</c> tools: byte-pattern scans and signature generation.</summary>
[McpServerToolType]
public sealed class AobTools
{
	/// <summary>The most patterns one <c>aob_find</c> call scans.</summary>
	internal const int MaximumPatterns = 8;

	/// <summary>The most matches listed per pattern.</summary>
	internal const int MaximumLimit = 10000;

	/// <summary>The largest module <c>aob_generate_signature</c> scans.</summary>
	internal const long MaximumSignatureModuleBytes = 64 * 1024 * 1024;

	private const string OutsideModuleHint =
		"Pass an address inside a loaded module (see module_list); Cheat Engine would otherwise scan the whole address space on its main thread.";

	private readonly ToolDispatch _dispatch;

	/// <summary>Creates the tools; the constructor does no Cheat Engine work.</summary>
	/// <param name="dispatch">The activation's dispatch facade.</param>
	public AobTools(ToolDispatch dispatch)
	{
		ArgumentNullException.ThrowIfNull(dispatch);
		_dispatch = dispatch;
	}

	/// <summary>Finds up to 8 byte patterns, each in one bounded Cheat Engine scan.</summary>
	[McpServerTool(Name = CheatEngineToolNames.AobFind, Title = "Find byte patterns", ReadOnly = true,
		Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
	[McpMeta(McpDispatchClass.MetaKey, McpDispatchClass.HostScan)]
	[Description(
		"Find up to 8 array-of-bytes patterns (hex bytes and ?? wildcards) and report each pattern's matches, whether the count is exact and whether it is unique. Each pattern is one Cheat Engine scan that blocks Cheat Engine while it runs: without module or startAddress/endAddress it scans the whole target, so scope it, and use executable=required for code. Uniqueness needs limit 2 or more.")]
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

		string? moduleName = MemoryTargets.OptionalModule(module);
		if ((startAddress is null) != (endAddress is null))
		{
			throw CheatEngineToolException.InvalidArgument(startAddress is null ? "startAddress" : "endAddress",
				"startAddress and endAddress must be given together.");
		}

		string? start = startAddress is null ? null : MemoryTargets.RequireExpression(startAddress, "startAddress");
		string? end = endAddress is null ? null : MemoryTargets.RequireExpression(endAddress, "endAddress");
		MemoryTargets.RequireRange(limit, "limit", 1, MaximumLimit);
		ScanProtectionFilter protection = new(Requirement(executable), Requirement(copyOnWrite),
			Requirement(writable));
		ScanAlignment rule = Alignment(alignment, lastDigits);
		AobPatternResult[] results = new AobPatternResult[scanned.Length];
		for (int index = 0; index < scanned.Length; index++)
		{
			ClientAobPattern pattern = scanned[index];
			results[index] = _dispatch.Run(CheatEngineToolNames.AobFind, token =>
			{
				ICheatEngineClient client = _dispatch.Client;
				AobScanRange? range = null;
				if (start is not null && end is not null)
				{
					Address first = MemoryTargets.Resolve(client, start, "startAddress", token);
					Address last = MemoryTargets.Resolve(client, end, "endAddress", token);
					range = first <= last
						? new AobScanRange(first, last)
						: throw CheatEngineToolException.InvalidArgument("endAddress",
							"must not be below startAddress.");
				}

				ModuleName? scope = moduleName is null ? null : new ModuleName(moduleName);
				PatternScanOutcome outcome = client.Patterns.ScanDetailed(
					new AobScanRequest(pattern, limit, scope, range, protection, rule), token);
				return Describe(client, pattern, outcome, scope is null && range is null);
			}, cancellationToken);
		}

		return new AobFindResult(results);
	}

	/// <summary>Generates a unique signature for an address inside a module and verifies it.</summary>
	[McpServerTool(Name = CheatEngineToolNames.AobGenerateSignature, Title = "Generate an AOB signature",
		ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
	[McpMeta(McpDispatchClass.MetaKey, McpDispatchClass.HostScan)]
	[Description(
		"Generate an array-of-bytes signature that finds an address inside a loaded module (Cheat Engine's getUniqueAOB), then verify it with a separate scan of that module. The address must be inside a module of at most 64 MiB; others are refused because Cheat Engine would scan the whole address space. Blocks Cheat Engine while it scans; review the pattern and mask displacements before relying on it.")]
	public AobSignature GenerateSignature(
		[Description("An address or Cheat Engine address expression inside a loaded module, such as game.exe+1C0.")]
		string address,
		[Description("The module that must contain the address; defaults to the module that contains it.")]
		string? module = null,
		[Description("Whether to verify the signature with a separate bounded scan of the module.")]
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
			UniqueAobProbe probe = _dispatch.ExecuteLua(CheatEngineToolNames.AobGenerateSignature,
				AobScripts.UniqueAob, AobJsonContext.Default.UniqueAobProbe, token, target.ToUInt64());
			string resolved = HexFormat.Address(target);
			if (!probe.Found)
			{
				return new AobSignature(resolved, owner.Name, false, false,
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
				return new AobSignature(resolved, owner.Name, true, false, pattern, HexFormat.Address(start),
					(int) offset, length);
			}

			PatternScanOutcome outcome = client.Patterns.ScanDetailed(
				new AobScanRequest(new ClientAobPattern(pattern), 2, new ModuleName(owner.Name)), token);
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

			ImmutableArray<Address> matches = result.Matches;
			bool unique = matches.Length == 1 && matches[0] == start &&
						  SignatureBuilder.IsExact(result, metrics);
			return new AobSignature(resolved, owner.Name, unique, true, pattern, HexFormat.Address(start),
				(int) offset, length, matches.Length);
		}, cancellationToken);
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
		ModuleInfo owner;
		if (moduleName is not null)
		{
			owner = MemoryTargets.FindModule(modules, moduleName) ??
					throw CheatEngineToolException.NotFound($"No loaded module is named '{moduleName}'.",
						"List the loaded modules with module_list.");
			if (owner.ImageSize is null)
			{
				throw CheatEngineToolException.Unsupported(
					$"Cheat Engine does not report the size of '{owner.Name}', so the address cannot be proven inside it.");
			}

			if (!MemoryTargets.Contains(owner, target))
			{
				throw CheatEngineToolException.InvalidArgument("module",
					$"'{owner.Name}' does not contain the address {HexFormat.Address(target)}.", OutsideModuleHint);
			}
		}
		else
		{
			owner = MemoryTargets.ContainingModule(modules, target) ??
					throw CheatEngineToolException.InvalidArgument("address",
						$"{HexFormat.Address(target)} is not inside a loaded module of known size.", OutsideModuleHint);
		}

		return owner.ImageSize!.Value.Value <= MaximumSignatureModuleBytes
			? owner
			: throw CheatEngineToolException.LimitExceeded("module",
				$"'{owner.Name}' is larger than {MaximumSignatureModuleBytes} bytes; build the signature by hand from code_disassemble.");
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
}
