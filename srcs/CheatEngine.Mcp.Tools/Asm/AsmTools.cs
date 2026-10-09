using System.ComponentModel;
using System.Globalization;
using System.Text;

using CheatEngine.Client.Assembly;
using CheatEngine.Client.Results;
using CheatEngine.Mcp.Core.Contract;
using CheatEngine.Mcp.Core.Features;
using CheatEngine.Mcp.Core.Values;
using CheatEngine.Mcp.Tools.Memory;
using CheatEngine.SDK.Engine.Values;

using ModelContextProtocol.Server;

using CorePattern = CheatEngine.Mcp.Core.Values.AobPattern;

namespace CheatEngine.Mcp.Tools.Asm;

/// <summary>Typed assembly and rollback-capable Auto Assembler patch ownership for the <c>asm_*</c> contract.</summary>
[McpServerToolType]
public sealed class AsmTools
{
	internal const int MaximumInstructions = 128;
	internal const int MaximumInstructionLength = 4096;
	internal const int MaximumScriptLength = 1024 * 1024;

	/// <summary>
	///     The UTF-8 size bound of a checked script: the largest string a fixed Lua body receives as an argument.
	/// </summary>
	internal const int MaximumCheckedScriptBytes = 1024 * 1024;

	internal const int MaximumPatchNameLength = 256;
	internal const int MaximumPatchCount = 128;
	internal const int MaximumHostMessageBytes = 4096;

	/// <summary>The longest label suffix <c>asm_generate_api_hook</c> passes to Cheat Engine.</summary>
	internal const int MaximumHookExtensionLength = 64;

	private readonly IAutoAssemblerClient? _autoAssembler;
	private readonly ToolDispatch _dispatch;
	private readonly Lock _patchLock = new();
	private readonly Dictionary<string, IAutoAssemblerPatchLease> _patches = new(StringComparer.Ordinal);
	private readonly TargetResources _resources;

	/// <summary>Creates the assembler tools; the optional Client service exists only after the server enabled patches.</summary>
	public AsmTools(ToolDispatch dispatch, TargetResources resources, IAutoAssemblerClient? autoAssembler = null)
	{
		ArgumentNullException.ThrowIfNull(dispatch);
		ArgumentNullException.ThrowIfNull(resources);
		_dispatch = dispatch;
		_resources = resources;
		_autoAssembler = autoAssembler;
	}

	/// <summary>Assembles a bounded sequence without writing target memory.</summary>
	[McpServerTool(Name = CheatEngineToolNames.AsmAssemble, Title = "Assemble instructions", ReadOnly = true,
		Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
	[McpMeta(McpDispatchClass.MetaKey, McpDispatchClass.Short)]
	[Description(
		"Assemble 1 to 128 instructions with CheatEngine.Client's typed instruction API. Nothing is written to target memory. Each next instruction begins after the bytes produced by the previous one, so relative operands use their actual origin.")]
	public AsmAssembleResult Assemble(
		[Description("The origin address or expression for the first instruction.")]
		string address,
		[Description("The instruction sources, 1 to 128 entries, each at most 4096 characters.")]
		string[] instructions,
		[Description(
			"The preferred encoding for relative jumps and calls as an integer: 0 none (Cheat Engine chooses), 1 short, 2 long or 3 far.")]
		InstructionEncodingPreference preference = InstructionEncodingPreference.None,
		[Description("Whether Cheat Engine skips relative-branch reachability checks.")]
		bool skipRangeCheck = false,
		CancellationToken cancellationToken = default)
	{
		string expression = MemoryTargets.RequireExpression(address, "address");
		if (instructions is null || instructions.Length is < 1 or > MaximumInstructions)
		{
			throw CheatEngineToolException.InvalidArgument("instructions",
				$"must list 1 to {MaximumInstructions} instructions.");
		}

		foreach (string? instruction in instructions)
		{
			if (string.IsNullOrWhiteSpace(instruction) || instruction.Length > MaximumInstructionLength)
			{
				throw CheatEngineToolException.InvalidArgument("instructions",
					$"each instruction must contain 1 to {MaximumInstructionLength} characters.");
			}
		}

		if (!Enum.IsDefined(preference))
		{
			throw CheatEngineToolException.InvalidArgument("preference",
				"must be 0 (none), 1 (short), 2 (long) or 3 (far).");
		}

		return _dispatch.Run(CheatEngineToolNames.AsmAssemble, token =>
		{
			Address current = MemoryTargets.Resolve(_dispatch.Client, expression, "address", token);
			List<AsmInstruction> output = new(instructions.Length);
			List<byte> all = [];
			foreach (string instruction in instructions)
			{
				byte[] bytes =
				[
					.. _dispatch.Client.Assembly.Assemble(
						new AssemblyInstructionRequest(current, instruction, preference, skipRangeCheck), token)
				];
				output.Add(new AsmInstruction(HexFormat.Address(current), instruction, Convert.ToHexString(bytes),
					bytes.Length));
				all.AddRange(bytes);
				current += bytes.Length;
			}

			return new AsmAssembleResult([.. output], Convert.ToHexString([.. all]), all.Count);
		}, cancellationToken);
	}

	/// <summary>Checks both sections of an Auto Assembler source without applying it.</summary>
	[McpServerTool(Name = CheatEngineToolNames.AsmCheck, Title = "Check Auto Assembler", ReadOnly = true,
		Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
	[McpMeta(McpDispatchClass.MetaKey, McpDispatchClass.Short)]
	[RequiresFeature(McpFeature.AutoAssembler)]
	[Description(
		"Check an Auto Assembler script without applying it: Cheat Engine checks ENABLE, then DISABLE if ENABLE passed; failedSection names the rejected section. Cheat Engine runs parts of a script while checking it, so the server refuses, as unsupported, any script that needs a switch beyond auto_assembler (such as {$lua}, {$c}, luacall, loadlibrary, include or a Lua-registered command like USEMONO), uses globalalloc, which allocates memory even during a check, or writes $ before anything but hexadecimal digits, which Cheat Engine evaluates as Lua; review it by reading it. Commands in // and /* */ comments are ignored, as Cheat Engine removes those first. Its symbol handler still resolves addresses and operands, consulting symbol-lookup callbacks that Lua registered, such as the shipped luasymbols.lua, which reads an unknown name as Lua. DISABLE is checked without ENABLE's symbols, so symbol+4 there is rejected although a release would resolve it. Success proves no later apply or release.")]
	public AsmCheckResult Check(
		[Description(
			"A complete Auto Assembler source with ENABLE and DISABLE sections, up to 1048576 characters and " +
			"1048576 UTF-8 bytes.")]
		string script,
		[Description("An optional diagnostic patch name, up to 256 characters.")]
		string? name = null,
		CancellationToken cancellationToken = default)
	{
		ValidateScript(script, name);
		// The DISABLE check passes the script to fixed Lua, which bounds a string argument by its UTF-8 size; refuse a
		// larger script here, before Cheat Engine checks the ENABLE section.
		if (Encoding.UTF8.GetByteCount(script) > MaximumCheckedScriptBytes)
		{
			throw CheatEngineToolException.LimitExceeded("script",
				$"must be at most {MaximumCheckedScriptBytes} bytes when encoded as UTF-8.");
		}

		AsmCheckScreen.Screen(script).Enforce(_dispatch.Features, CheatEngineToolNames.AsmCheck);
		return _dispatch.Run(CheatEngineToolNames.AsmCheck, token =>
		{
			AutoAssemblerCheckResult enable = RequireAutoAssembler(CheatEngineToolNames.AsmCheck)
				.Check(new AutoAssemblerScript(script, name), token);
			if (!enable.IsAccepted)
			{
				return new AsmCheckResult(false, enable.HostMessages, enable.HostMessagesTruncated,
					AsmScriptSection.Enable);
			}

			AsmLuaCheck disable = _dispatch.ExecuteLua(CheatEngineToolNames.AsmCheck, AsmScripts.CheckDisable,
				AsmLuaJsonContext.Default.AsmLuaCheck, token, script, MaximumHostMessageBytes);
			return disable.Accepted
				? new AsmCheckResult(true)
				: new AsmCheckResult(false, disable.HostMessages, disable.HostMessagesTruncated,
					AsmScriptSection.Disable);
		}, cancellationToken);
	}

	/// <summary>Applies a caller-supplied rollback-capable patch and retains the Client lease.</summary>
	[McpServerTool(Name = CheatEngineToolNames.AsmApply, Title = "Apply Auto Assembler patch", ReadOnly = false,
		Destructive = true, Idempotent = false, OpenWorld = false, UseStructuredContent = true)]
	[McpMeta(McpDispatchClass.MetaKey, McpDispatchClass.MayPrompt)]
	[RequiresFeature(McpFeature.AutoAssembler)]
	[Description(
		"Apply a complete Auto Assembler patch and retain the Client-owned DISABLE lease. The script must contain ENABLE and DISABLE sections; the server classifies its commands and enforces the required capability switches before application. A failed apply can have an unknown partial effect, so inspect the target and use asm_release_patch whenever a lease was returned.")]
	public AsmPatchApplied Apply(
		[Description("A complete rollback-capable Auto Assembler script, up to 1048576 characters.")]
		string script,
		[Description("An optional diagnostic patch name, up to 256 characters.")]
		string? name = null,
		CancellationToken cancellationToken = default)
	{
		ValidateScript(script, name);
		AutoAssemblerScriptClassifier.Classify(script).Enforce(_dispatch.Features, CheatEngineToolNames.AsmApply);
		return _dispatch.Run(CheatEngineToolNames.AsmApply,
			token => ApplyCore(CheatEngineToolNames.AsmApply, script, name, token), cancellationToken);
	}

	/// <summary>Generates and applies a byte-for-byte rollback patch with an original-byte assertion.</summary>
	[McpServerTool(Name = CheatEngineToolNames.AsmApplyCodePatch, Title = "Apply code patch", ReadOnly = false,
		Destructive = true, Idempotent = false, OpenWorld = false, UseStructuredContent = true)]
	[McpMeta(McpDispatchClass.MetaKey, McpDispatchClass.MayPrompt)]
	[RequiresFeature(McpFeature.AutoAssembler)]
	[Description(
		"Apply a generated code patch that asserts the expected original bytes before writing replacement bytes, and restores exactly those bytes through the Client-owned DISABLE lease. expectedBytes and replacementBytes must have the same 1 to 64 byte length. Release the returned patch id before selecting another process.")]
	public AsmPatchApplied ApplyCodePatch(
		[Description("The address or expression of the first original byte.")]
		string address,
		[Description("The expected original bytes, hexadecimal with optional spaces, 1 to 64 bytes.")]
		string expectedBytes,
		[Description("The replacement bytes, hexadecimal with optional spaces and the same length as expectedBytes.")]
		string replacementBytes,
		[Description("An optional diagnostic patch name, up to 256 characters.")]
		string? name = null,
		CancellationToken cancellationToken = default)
	{
		string expression = MemoryTargets.RequireExpression(address, "address");
		ValidateName(name);
		byte[] expected = Bytes(expectedBytes, "expectedBytes");
		byte[] replacement = Bytes(replacementBytes, "replacementBytes");
		if (expected.Length != replacement.Length)
		{
			throw CheatEngineToolException.InvalidArgument("replacementBytes",
				"must contain the same byte count as expectedBytes.");
		}

		_dispatch.Features.Require(McpFeature.AutoAssembler, CheatEngineToolNames.AsmApplyCodePatch);
		return _dispatch.Run(CheatEngineToolNames.AsmApplyCodePatch, token =>
		{
			Address target = MemoryTargets.Resolve(_dispatch.Client, expression, "address", token);
			string script = CodePatchScript(target, expected, replacement);
			return ApplyCore(CheatEngineToolNames.AsmApplyCodePatch, script, name, token);
		}, cancellationToken);
	}

	/// <summary>Releases one patch through the Client's original DISABLE information.</summary>
	[McpServerTool(Name = CheatEngineToolNames.AsmReleasePatch, Title = "Release Auto Assembler patch",
		ReadOnly = false,
		Destructive = true, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
	[McpMeta(McpDispatchClass.MetaKey, McpDispatchClass.MayPrompt)]
	[Description(
		"Run the original DISABLE section for one MCP-owned patch. Script execution can block Cheat Engine or show a dialog. A failed or target-changed release can leave a patch in the old target and require manual recovery; retry only when retryable is true. A successful release removes the patch from runtime resources.")]
	public AsmPatchReleased ReleasePatch(
		[Description("The patch id returned by asm_apply or asm_apply_code_patch.")]
		string patchId,
		CancellationToken cancellationToken = default)
	{
		IAutoAssemblerPatchLease lease = Patch(patchId);
		LeaseReleaseOutcome outcome = _dispatch.Run(CheatEngineToolNames.AsmReleasePatch, _ => lease.Release(),
			cancellationToken);
		if (!outcome.IsRetryable)
		{
			_resources.Forget(lease);
			Remove(patchId, lease);
		}

		return new AsmPatchReleased(patchId, outcome.IsComplete, outcome.Kind.ToString(), outcome.HostEffect.ToString(),
			outcome.IsRetryable, outcome.RequiresManualRecovery);
	}

	/// <summary>Lists patches retained by this MCP activation.</summary>
	[McpServerTool(Name = CheatEngineToolNames.AsmListPatches, Title = "List Auto Assembler patches", ReadOnly = true,
		Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
	[McpMeta(McpDispatchClass.MetaKey, McpDispatchClass.Short)]
	[Description(
		"List Auto Assembler patches whose Client leases are retained by this MCP activation. These patches block a process change until released, and never include patches created outside this activation.")]
	public AsmPatchList ListPatches()
	{
		KeyValuePair<string, IAutoAssemblerPatchLease>[] patches;
		lock (_patchLock)
		{
			patches = [.. _patches];
		}

		return new AsmPatchList([
			.. patches.OrderBy(static patch => patch.Key, StringComparer.Ordinal)
				.Select(static patch => Info(patch.Key, patch.Value))
		]);
	}

	/// <summary>Creates a reviewed AOB injection scaffold without applying it.</summary>
	[McpServerTool(Name = CheatEngineToolNames.AsmGenerateInjection, Title = "Generate AOB injection", ReadOnly = true,
		Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
	[McpMeta(McpDispatchClass.MetaKey, McpDispatchClass.Short)]
	[Description(
		"Generate a rollback-capable AOB injection scaffold without applying it. It asserts the original bytes, allocates only while enabled, restores the bytes and releases its allocation and symbol while disabled. signature must be a verified unique module signature from aob_generate_signature; pass its offset too, so the scaffold injects at the match start plus offset, where symbolName is registered. Review the template, then use asm_check before any apply.")]
	public static AsmGeneratedScript GenerateInjection(
		[Description("The loaded module name to scan, such as game.exe.")]
		string module,
		[Description("A verified unique AOB signature from aob_generate_signature.")]
		string signature,
		[Description(
			"The exact original instruction bytes at the injection point, asserted before the jump, at least 5 and at most 64 bytes.")]
		string expectedBytes,
		[Description(
			"The Auto Assembler symbol name registered at the injection point, 1 to 64 ASCII letters, digits or underscores and not starting with a digit.")]
		string symbolName = "mcpInjection",
		[Description(
			"The byte distance from the signature's match start to the injection point, as returned in offset by aob_generate_signature; 0 to the signature length minus 1.")]
		int offset = 0)
	{
		if (string.IsNullOrWhiteSpace(module) || module.Length > 260 ||
			module.Any(character => !(char.IsAsciiLetterOrDigit(character) || character is '.' or '_' or '-')))
		{
			throw CheatEngineToolException.InvalidArgument("module",
				"must be a module file name of at most 260 ASCII letters, digits, dots, underscores or hyphens.");
		}

		string pattern = CorePattern.Normalize(signature, "signature");
		byte[] expected = Bytes(expectedBytes, "expectedBytes", 5);
		ValidateSymbol(symbolName);
		int patternLength = (pattern.Length + 1) / 3;
		if (offset < 0 || offset >= patternLength)
		{
			throw CheatEngineToolException.InvalidArgument("offset",
				$"must be from 0 to {patternLength - 1}, inside the {patternLength}-byte signature.");
		}

		string script = offset == 0
			? InjectionScript(module, pattern, expected, symbolName)
			: OffsetInjectionScript(module, pattern, expected, symbolName, offset);
		return new AsmGeneratedScript(script, Convert.ToHexString(expected), symbolName);
	}

	/// <summary>Asks Cheat Engine to generate an API hook source without applying it.</summary>
	[McpServerTool(Name = CheatEngineToolNames.AsmGenerateApiHook, Title = "Generate API hook", ReadOnly = true,
		Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
	[McpMeta(McpDispatchClass.MetaKey, McpDispatchClass.Short)]
	[Description(
		"Ask Cheat Engine's generateAPIHookScript for an Auto Assembler hook that sends execution at address to jumpTarget, without applying it. The script's ENABLE section allocates originalcall, which holds the instructions the jump overwrites followed by a jump back, adds a jump trampoline on x64, and writes the jump at address; its DISABLE section restores the original bytes and frees what ENABLE allocated. Review the generated source and run asm_check before an explicit asm_apply.")]
	public AsmGeneratedScript GenerateApiHook(
		[Description(
			"The address or expression of the function or instruction to hook; Cheat Engine overwrites the whole instructions that the jump covers.")]
		string address,
		[Description("The address or expression of the replacement code that the hook jumps to.")]
		string jumpTarget,
		[Description(
			"An optional address or symbol where the ENABLE section stores the address of originalcall (dq on x64, dd on x86), so the replacement code can call the original instructions; DISABLE does not restore it. Up to 256 characters.")]
		string? newCallAddress = null,
		[Description(
			"An optional suffix Cheat Engine appends to the script's label and allocation names (originalcall, returnhere, jumptrampoline), so two generated hooks can share one script: up to 64 ASCII letters, digits or underscores.")]
		string? extension = null,
		[Description("Whether Cheat Engine should generate a hook in its own process.")]
		bool targetSelf = false,
		CancellationToken cancellationToken = default)
	{
		ValidateText(address, "address", 1024);
		ValidateText(jumpTarget, "jumpTarget", 1024);
		if (newCallAddress is { Length: > 256 })
		{
			throw CheatEngineToolException.InvalidArgument("newCallAddress", "is longer than the supported bound.");
		}

		if (extension is not null && (extension.Length > MaximumHookExtensionLength ||
									  !extension.All(static character =>
										  char.IsAsciiLetterOrDigit(character) || character == '_')))
		{
			throw CheatEngineToolException.InvalidArgument("extension",
				$"must be at most {MaximumHookExtensionLength} ASCII letters, digits or underscores, because Cheat Engine appends it to label names.");
		}

		AsmLuaScript generated = _dispatch.RunLua(CheatEngineToolNames.AsmGenerateApiHook, AsmScripts.GenerateApiHook,
			AsmLuaJsonContext.Default.AsmLuaScript, cancellationToken, address, jumpTarget, newCallAddress, extension,
			targetSelf);
		ValidateGeneratedApiHook(generated.Script);
		return new AsmGeneratedScript(generated.Script, string.Empty);
	}

	private AsmPatchApplied ApplyCore(string operation, string script, string? name,
		CancellationToken cancellationToken)
	{
		IAutoAssemblerClient autoAssembler = RequireAutoAssembler(operation);
		lock (_patchLock)
		{
			if (_patches.Count >= MaximumPatchCount)
			{
				throw CheatEngineToolException.LimitExceeded("script",
					$"this activation retains at most {MaximumPatchCount} patches; release one first.");
			}
		}

		IAutoAssemblerPatchLease lease =
			autoAssembler.ApplyPatch(new AutoAssemblerScript(script, name), cancellationToken);
		string patchId = Guid.NewGuid().ToString("N");
		if (lease.AppliedAfterTargetChange || lease.IsReleased)
		{
			LeaseReleaseOutcome? release = lease.LastReleaseOutcome;
			return new AsmPatchApplied(false, patchId, lease.Name, lease.AppliedAfterTargetChange,
				lease.RequiresManualRecovery || release?.RequiresManualRecovery == true, release?.Kind.ToString(),
				release?.HostEffect.ToString(), lease.HostWarnings, lease.HostWarningsTruncated);
		}

		lock (_patchLock)
		{
			_patches.Add(patchId, lease);
		}

		_resources.Track(lease, "patch", () => Remove(patchId, lease), lease.Name);
		return new AsmPatchApplied(true, patchId, lease.Name, false, lease.RequiresManualRecovery, null, null,
			lease.HostWarnings, lease.HostWarningsTruncated);
	}

	private IAutoAssemblerClient RequireAutoAssembler(string operation)
	{
		_dispatch.Features.Require(McpFeature.AutoAssembler, operation);
		return _autoAssembler ?? throw CheatEngineToolException.Unsupported(
			"Auto Assembler patches were not registered for this activation.");
	}

	private IAutoAssemblerPatchLease Patch(string patchId)
	{
		if (string.IsNullOrWhiteSpace(patchId))
		{
			throw CheatEngineToolException.InvalidArgument("patchId", "is required.");
		}

		lock (_patchLock)
		{
			return _patches.TryGetValue(patchId, out IAutoAssemblerPatchLease? lease)
				? lease
				: throw CheatEngineToolException.NotFound("The patch id is not retained by this activation.",
					"List retained patches with asm_list_patches.");
		}
	}

	private void Remove(string patchId, IAutoAssemblerPatchLease lease)
	{
		lock (_patchLock)
		{
			if (_patches.TryGetValue(patchId, out IAutoAssemblerPatchLease? current) && ReferenceEquals(current, lease))
			{
				_patches.Remove(patchId);
			}
		}
	}

	private static AsmPatchInfo Info(string patchId, IAutoAssemblerPatchLease lease)
	{
		return new AsmPatchInfo(patchId, lease.Name, lease.SelectionEpoch, lease.AppliedAfterTargetChange,
			lease.CanDisable, lease.IsReleased, lease.RequiresManualRecovery, lease.HostWarnings,
			lease.HostWarningsTruncated);
	}

	private static void ValidateScript(string script, string? name)
	{
		if (string.IsNullOrWhiteSpace(script) || script.Length > MaximumScriptLength)
		{
			throw CheatEngineToolException.InvalidArgument("script",
				$"must contain 1 to {MaximumScriptLength} characters.");
		}

		if (script.IndexOf("[ENABLE]", StringComparison.OrdinalIgnoreCase) < 0 ||
			script.IndexOf("[DISABLE]", StringComparison.OrdinalIgnoreCase) < 0)
		{
			throw CheatEngineToolException.InvalidArgument("script",
				"must contain both [ENABLE] and [DISABLE] sections so its Client lease can roll it back.");
		}

		ValidateName(name);
	}

	private static void ValidateGeneratedApiHook(string script)
	{
		if (string.IsNullOrWhiteSpace(script) || script.Length > MaximumScriptLength ||
			script.IndexOf("[ENABLE]", StringComparison.OrdinalIgnoreCase) < 0 ||
			script.IndexOf("[DISABLE]", StringComparison.OrdinalIgnoreCase) < 0)
		{
			throw new CheatEngineToolException(new ToolError(ToolErrorKind.HostRefused,
				"Cheat Engine generated an incomplete API-hook script.", CheatEngineToolNames.AsmGenerateApiHook,
				ToolHostEffect.Completed, false,
				"Generate the hook again after confirming that Cheat Engine can resolve both addresses."));
		}
	}

	private static void ValidateName(string? name)
	{
		if (name is { Length: > MaximumPatchNameLength })
		{
			throw CheatEngineToolException.InvalidArgument("name",
				$"must contain at most {MaximumPatchNameLength} characters.");
		}
	}

	private static void ValidateText(string? value, string parameter, int maximum)
	{
		if (string.IsNullOrWhiteSpace(value) || value.Length > maximum)
		{
			throw CheatEngineToolException.InvalidArgument(parameter, $"must contain 1 to {maximum} characters.");
		}
	}

	private static byte[] Bytes(string value, string parameter, int minimum = 1)
	{
		if (value is null)
		{
			throw CheatEngineToolException.InvalidArgument(parameter, "is required.");
		}

		StringBuilder normalized = new(value.Length);
		foreach (char character in value)
		{
			if (char.IsWhiteSpace(character))
			{
				continue;
			}

			if (!char.IsAsciiHexDigit(character))
			{
				throw CheatEngineToolException.InvalidArgument(parameter,
					"must contain hexadecimal bytes with optional whitespace.");
			}

			normalized.Append(character);
		}

		if (normalized.Length % 2 != 0)
		{
			throw CheatEngineToolException.InvalidArgument(parameter, "must have an even hexadecimal character count.");
		}

		byte[] bytes;
		try
		{
			bytes = Convert.FromHexString(normalized.ToString());
		}
		catch (FormatException)
		{
			throw CheatEngineToolException.InvalidArgument(parameter, "must contain valid hexadecimal bytes.");
		}

		if (bytes.Length < minimum || bytes.Length > 64)
		{
			throw CheatEngineToolException.InvalidArgument(parameter, $"must contain {minimum} to 64 bytes.");
		}

		return bytes;
	}

	private static string CodePatchScript(Address target, byte[] expected, byte[] replacement)
	{
		string address = HexFormat.Address(target);
		return
			$"[ENABLE]\nassert({address},{Spaced(expected)})\n{address}:\ndb {Spaced(replacement)}\n\n[DISABLE]\n{address}:\ndb {Spaced(expected)}";
	}

	private static string InjectionScript(string module, string pattern, byte[] expected, string symbol)
	{
		string nops = string.Join('\n', Enumerable.Repeat("nop", expected.Length - 5));
		return
			$"[ENABLE]\naobscanmodule({symbol},{module},{pattern})\nassert({symbol},{Spaced(expected)})\nalloc(newmem,2048,{symbol})\nlabel(code)\nlabel(return)\nregistersymbol({symbol})\n\nnewmem:\n// Add reviewed custom code above the original bytes.\ncode:\ndb {Spaced(expected)}\njmp return\n\n{symbol}:\njmp newmem{(nops.Length == 0 ? string.Empty : "\n" + nops)}\nreturn:\n\n[DISABLE]\n{symbol}:\ndb {Spaced(expected)}\nunregistersymbol({symbol})\ndealloc(newmem)";
	}

	/// <summary>
	///     The scaffold for an injection point inside the match: the scan result keeps a script-local name, and
	///     <paramref name="symbol" /> is a registered label at the match start plus <paramref name="offset" />, so the
	///     DISABLE section addresses it by name.
	/// </summary>
	private static string OffsetInjectionScript(string module, string pattern, byte[] expected, string symbol,
		int offset)
	{
		string nops = string.Join('\n', Enumerable.Repeat("nop", expected.Length - 5));
		string scan = symbol + "_aob";
		string at = scan + "+" + offset.ToString("X", CultureInfo.InvariantCulture);
		return
			$"[ENABLE]\naobscanmodule({scan},{module},{pattern})\nassert({at},{Spaced(expected)})\nalloc(newmem,2048,{scan})\nlabel(code)\nlabel(return)\nlabel({symbol})\nregistersymbol({symbol})\n\nnewmem:\n// Add reviewed custom code above the original bytes.\ncode:\ndb {Spaced(expected)}\njmp return\n\n{at}:\n{symbol}:\njmp newmem{(nops.Length == 0 ? string.Empty : "\n" + nops)}\nreturn:\n\n[DISABLE]\n{symbol}:\ndb {Spaced(expected)}\nunregistersymbol({symbol})\ndealloc(newmem)";
	}

	private static string Spaced(IEnumerable<byte> bytes)
	{
		return string.Join(' ', bytes.Select(static value => value.ToString("X2", CultureInfo.InvariantCulture)));
	}

	private static void ValidateSymbol(string value)
	{
		if (string.IsNullOrWhiteSpace(value) || value.Length > 64 ||
			(!char.IsAsciiLetter(value[0]) && value[0] != '_') ||
			value.Any(static character => !(char.IsAsciiLetterOrDigit(character) || character == '_')))
		{
			throw CheatEngineToolException.InvalidArgument("symbolName",
				"must be 1 to 64 ASCII letters, digits or underscores and must not start with a digit.");
		}
	}
}
