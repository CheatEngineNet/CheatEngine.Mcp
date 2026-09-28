using System.Collections.Immutable;

using CheatEngine.Client;
using CheatEngine.Client.Assembly;
using CheatEngine.Client.Results;
using CheatEngine.Client.Scanning;
using CheatEngine.Mcp.Core.Contract;
using CheatEngine.Mcp.Core.Values;
using CheatEngine.Mcp.Tools.Memory;
using CheatEngine.SDK.Engine.Inspection;
using CheatEngine.SDK.Engine.Values;

namespace CheatEngine.Mcp.Tools.Aob;

/// <summary>
///     Builds a signature for an address in a module too large for Cheat Engine's <c>getUniqueAOB</c>, whose first scan
///     lists every match of the instruction bytes at the address and reads memory around each one on Cheat Engine's
///     main thread (<c>frmautoinjectunit.pas</c> <c>GetUniqueAOB</c>). It decodes whole instructions forward from the
///     address, wildcards their position-dependent bytes (<see cref="InstructionMask" />) and grows the pattern until
///     a module-bounded scan with limit 2 proves it unique.
/// </summary>
/// <remarks>
///     The pattern starts at the address, so its offset is always 0. It grows to the next length of
///     <see cref="ScanLengths" /> by whole instructions, never past <see cref="MaximumBytes" /> or the module's end, and
///     each length costs one scan, so a call runs at most six scans. Everything runs in the caller's dispatch, so Cheat
///     Engine cannot select another target between the decode and the scans.
/// </remarks>
internal static class ManagedSignatureGenerator
{
	/// <summary>The longest managed signature, in bytes.</summary>
	internal const int MaximumBytes = 64;

	/// <summary>The pattern lengths at which a module scan tests uniqueness; each costs one scan.</summary>
	internal static ReadOnlySpan<int> ScanLengths => [8, 16, 24, 32, 48, MaximumBytes];

	/// <summary>Generates and proves a signature inside the current dispatch.</summary>
	/// <param name="client">The activation's Client.</param>
	/// <param name="target">The resolved address.</param>
	/// <param name="owner">The module of known size that contains the address.</param>
	/// <param name="cancellationToken">The token of the enclosing dispatch body.</param>
	/// <returns>A unique, verified signature, or the longest pattern tried when none was unique.</returns>
	internal static AobSignature Generate(ICheatEngineClient client, Address target, ModuleInfo owner,
		CancellationToken cancellationToken)
	{
		bool is64 = MemoryTargets.PointerBytes(client, cancellationToken) == 8;
		ulong end = owner.BaseAddress.ToUInt64() + owner.ImageSize!.Value.Value;
		string resolved = HexFormat.Address(target);
		List<byte> bytes = new(MaximumBytes);
		List<bool> wildcards = new(MaximumBytes);
		Address next = target;
		bool exhausted = false;
		string? tried = null;
		foreach (int length in ScanLengths)
		{
			while (!exhausted && bytes.Count < length)
			{
				exhausted = !TryAppend(client, bytes, wildcards, ref next, end, is64, cancellationToken);
			}

			if (bytes.Count == 0)
			{
				// The instruction at the address runs past the module's end.
				break;
			}

			string pattern = SignatureBuilder.Format(bytes, wildcards);
			if (pattern == tried)
			{
				break;
			}

			AobScanResult result = AobTools.ModuleScan(client, pattern, owner, out bool exact, cancellationToken);
			ImmutableArray<Address> matches = result.Matches;
			if (exact && matches.Length == 1 && matches[0] == target)
			{
				return new AobSignature(resolved, owner.Name, AobSignatureGenerator.Managed, true, true, pattern,
					resolved, 0, bytes.Count, 1);
			}

			// A complete list without the address means the scan did not see the bytes just read; a cut list (more
			// than 2 matches) may simply end before the address.
			if (exact && !matches.Contains(target))
			{
				throw new CheatEngineToolException(new ToolError(ToolErrorKind.HostRefused,
					$"The bounded scan of {owner.Name} did not find the bytes just read at {resolved}; the code changed while the signature was built, or Cheat Engine does not scan that memory.",
					CheatEngineToolNames.AobGenerateSignature, ToolHostEffect.Completed, false,
					"Repeat the call; if it fails again, build the pattern by hand from code_disassemble and prove it with aob_find."));
			}

			tried = pattern;
			if (exhausted)
			{
				break;
			}
		}

		return new AobSignature(resolved, owner.Name, AobSignatureGenerator.Managed, false, false,
			TriedPattern: tried);
	}

	/// <summary>
	///     Decodes the instruction at <paramref name="next" /> and appends it with its wildcards, unless it would pass
	///     <see cref="MaximumBytes" /> or the module's end, or cannot be decoded after the first instruction.
	/// </summary>
	/// <returns><see langword="false" /> when the pattern cannot grow any further.</returns>
	private static bool TryAppend(ICheatEngineClient client, List<byte> bytes, List<bool> wildcards, ref Address next,
		ulong end, bool is64, CancellationToken cancellationToken)
	{
		if (!client.Assembly.TryDisassemble(next, out AssemblyInstructionSnapshot decoded,
				out CheatEngineFailure failure, cancellationToken))
		{
			// The instruction at the address must decode; a later one only ends the pattern.
			return bytes.Count > 0 && failure.Kind is not CheatEngineFailureKind.Cancelled
				? false
				: throw CheatEngineToolException.FromFailure(failure, client.Stopping.IsCancellationRequested);
		}

		ulong start = next.ToUInt64();
		ImmutableArray<byte> instruction = decoded.Bytes;
		if (instruction.Length == 0 || bytes.Count + instruction.Length > MaximumBytes ||
			start + (ulong) instruction.Length > end)
		{
			return false;
		}

		bytes.AddRange(instruction);
		wildcards.AddRange(InstructionMask.Wildcards(instruction.AsSpan(), is64));
		next += instruction.Length;
		return true;
	}
}
