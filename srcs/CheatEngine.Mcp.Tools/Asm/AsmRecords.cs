using System.ComponentModel;

namespace CheatEngine.Mcp.Tools.Asm;

/// <summary>One instruction assembled at its explicit origin.</summary>
public sealed record AsmInstruction(
	[property: Description("The origin used for this instruction, uppercase hexadecimal without 0x.")]
	string Address,
	[property: Description("The submitted instruction source.")]
	string Source,
	[property: Description("The assembled bytes, uppercase hexadecimal without separators.")]
	string Bytes,
	[property: Description("The assembled byte count.")]
	int Size);

/// <summary>The result of assembling a bounded sequence of instructions.</summary>
public sealed record AsmAssembleResult(
	[property: Description("The instructions in source order; each next origin follows the previous assembled bytes.")]
	AsmInstruction[] Instructions,
	[property: Description("The concatenated bytes, uppercase hexadecimal without separators.")]
	string Bytes,
	[property: Description("The total assembled byte count.")]
	int Size);

/// <summary>Cheat Engine's syntax-check result for an Auto Assembler script.</summary>
public sealed record AsmCheckResult(
	[property: Description("Whether Cheat Engine accepted the ENABLE section. A true result does not apply the patch.")]
	bool Accepted,
	[property: Description("Cheat Engine's bounded, unparsed diagnostic text when it returned any.")]
	string? HostMessages = null,
	[property: Description("Whether hostMessages was truncated by CheatEngine.Client.")]
	bool HostMessagesTruncated = false);

/// <summary>A Client-owned Auto Assembler patch held by this MCP activation.</summary>
public sealed record AsmPatchInfo(
	[property: Description("The patch id for asm_release_patch.")]
	string PatchId,
	[property: Description("The optional diagnostic patch name.")]
	string? Name,
	[property: Description("The Client selection epoch where the patch was applied.")]
	long SelectionEpoch,
	[property: Description("Whether the Client observed a changed selection immediately after applying the patch.")]
	bool AppliedAfterTargetChange,
	[property: Description("Whether the Client can still run the original DISABLE section.")]
	bool CanDisable,
	[property: Description("Whether the lease already ended.")]
	bool Released,
	[property: Description("Whether the patch may need manual recovery.")]
	bool RequiresManualRecovery,
	[property: Description("The bounded Auto Assembler warnings Cheat Engine returned, when any.")]
	string? HostWarnings = null,
	[property: Description("Whether hostWarnings was truncated by CheatEngine.Client.")]
	bool HostWarningsTruncated = false);

/// <summary>The outcome of applying a patch while retaining its Client lease.</summary>
public sealed record AsmPatchApplied(
	[property: Description("Whether the patch was applied and retained by this MCP activation.")]
	bool Retained,
	[property:
		Description(
			"The patch id when retained, or the failed attempt id when Client ownership could not be retained.")]
	string PatchId,
	[property: Description("The diagnostic patch name.")]
	string? Name,
	[property: Description("Whether the selected target changed during application.")]
	bool AppliedAfterTargetChange,
	[property: Description("Whether recovery in Cheat Engine may be needed before changing the target.")]
	bool RequiresManualRecovery,
	[property: Description("The release outcome when ownership could not be retained.")]
	string? Release = null,
	[property: Description("The actual host effect of that release attempt.")]
	string? HostEffect = null,
	[property: Description("The bounded Auto Assembler warnings, when any.")]
	string? HostWarnings = null,
	[property: Description("Whether hostWarnings was truncated by CheatEngine.Client.")]
	bool HostWarningsTruncated = false);

/// <summary>The result of releasing one Client-owned patch.</summary>
public sealed record AsmPatchReleased(
	[property: Description("The released patch id.")]
	string PatchId,
	[property: Description("Whether the DISABLE path completed.")]
	bool Released,
	[property: Description("The Client release outcome.")]
	string Release,
	[property: Description("The confirmed host effect.")]
	string HostEffect,
	[property: Description("Whether retrying the release can still help.")]
	bool Retryable,
	[property: Description("Whether the patch may remain and needs manual recovery.")]
	bool RequiresManualRecovery);

/// <summary>The patches currently retained by this activation.</summary>
public sealed record AsmPatchList(
	[property: Description("Client-owned patches held by this MCP activation, ordered by patch id.")]
	AsmPatchInfo[] Patches);

/// <summary>An Auto Assembler script generated but never applied by the tool.</summary>
public sealed record AsmGeneratedScript(
	[property: Description("The generated Auto Assembler source. Review, check and explicitly apply it separately.")]
	string Script,
	[property: Description(
		"The validated original bytes asserted before an injection writes its jump; empty when Cheat Engine generated an API-hook script.")]
	string ExpectedBytes,
	[property: Description("The symbol the script registers while enabled, when generated as an injection.")]
	string? SymbolName = null);

internal sealed record AsmLuaScript(string Script);
