using System.Collections.Immutable;

using CheatEngine.Client;
using CheatEngine.Client.Inspection;
using CheatEngine.Client.Memory;
using CheatEngine.Client.Results;
using CheatEngine.Mcp.Core.Contract;
using CheatEngine.SDK.Engine.Inspection;
using CheatEngine.SDK.Engine.Values;

namespace CheatEngine.Mcp.Tools.Modules;

/// <summary>Validates module arguments and finds modules inside a dispatch, shared by the <c>module_*</c> tools.</summary>
internal static class ModuleLocator
{
	/// <summary>The longest module argument accepted.</summary>
	internal const int MaximumModuleLength = 256;

	/// <summary>The most modules copied from Cheat Engine; a larger module list is refused.</summary>
	internal const int MaximumModules = 65536;

	private const int FirstModuleCapacity = 4096;

	private const string ListHint = "List the modules with module_list.";

	/// <summary>Checks a module argument before any dispatch.</summary>
	/// <param name="module">The caller's module name or address expression.</param>
	/// <returns>The trimmed argument.</returns>
	/// <exception cref="CheatEngineToolException"><c>invalid_argument</c> for an empty or overlong argument.</exception>
	internal static string RequireModule(string? module)
	{
		string trimmed = module?.Trim() ?? string.Empty;
		if (trimmed.Length is 0 or > MaximumModuleLength || trimmed.Any(char.IsControl))
		{
			throw CheatEngineToolException.InvalidArgument("module",
				$"must be a module name or an address inside a module, 1 to {MaximumModuleLength} characters.");
		}

		return trimmed;
	}

	/// <summary>Checks an optional filter before any dispatch.</summary>
	/// <param name="value">The filter.</param>
	/// <param name="parameter">The parameter name.</param>
	/// <returns>The filter, or <see langword="null" /> for none.</returns>
	/// <exception cref="CheatEngineToolException"><c>invalid_argument</c> for an overlong filter.</exception>
	internal static string? OptionalFilter(string? value, string parameter)
	{
		if (string.IsNullOrEmpty(value))
		{
			return null;
		}

		if (value.Length > MaximumModuleLength)
		{
			throw CheatEngineToolException.InvalidArgument(parameter,
				$"must be at most {MaximumModuleLength} characters.");
		}

		return value;
	}

	/// <summary>Copies the module list of a process, retrying once with the larger capacity. Inside a dispatch.</summary>
	/// <param name="client">The activation's Client.</param>
	/// <param name="processId">The process, or <see langword="null" /> for the attached one.</param>
	/// <param name="token">The dispatch body's token.</param>
	/// <returns>The modules in Cheat Engine's order.</returns>
	internal static ImmutableArray<ModuleInfo> GetModules(ICheatEngineClient client, TargetProcessId? processId,
		CancellationToken token)
	{
		IInspectionClient inspection = client.Inspection;
		if (inspection.TryGetModules(new InspectionCollectionRequest(FirstModuleCapacity), processId,
				out ImmutableArray<ModuleInfo> modules, out CheatEngineFailure failure, token))
		{
			return modules;
		}

		if (failure.Kind is CheatEngineFailureKind.ResultLimitExceeded &&
			inspection.TryGetModules(new InspectionCollectionRequest(MaximumModules), processId, out modules,
				out failure, token))
		{
			return modules;
		}

		throw CheatEngineToolException.FromFailure(failure, client.Stopping.IsCancellationRequested);
	}

	/// <summary>
	///     Finds the attached process's module named <paramref name="module" /> (case-insensitive), or else the module
	///     that contains the address the argument resolves to. Inside a dispatch.
	/// </summary>
	/// <param name="client">The activation's Client.</param>
	/// <param name="module">A checked module argument.</param>
	/// <param name="token">The dispatch body's token.</param>
	/// <returns>The module.</returns>
	/// <exception cref="CheatEngineToolException">
	///     <c>not_attached</c> without a process, <c>not_found</c> when no module matches.
	/// </exception>
	internal static ModuleInfo Find(ICheatEngineClient client, string module, CancellationToken token)
	{
		return Find(client, module, out _, token);
	}

	/// <summary>
	///     Finds a module as <see cref="Find(ICheatEngineClient, string, CancellationToken)" /> does and also returns the
	///     module list it searched, so a tool can place other addresses without copying the list again.
	/// </summary>
	/// <param name="client">The activation's Client.</param>
	/// <param name="module">A checked module argument.</param>
	/// <param name="modules">Receives the attached process's modules in Cheat Engine's order.</param>
	/// <param name="token">The dispatch body's token.</param>
	/// <returns>The module.</returns>
	/// <exception cref="CheatEngineToolException">
	///     <c>not_attached</c> without a process, <c>not_found</c> when no module matches.
	/// </exception>
	internal static ModuleInfo Find(ICheatEngineClient client, string module, out ImmutableArray<ModuleInfo> modules,
		CancellationToken token)
	{
		// Cheat Engine enumerates its own modules when nothing is attached; refuse that first.
		client.Processes.GetCurrentProcess(token);
		modules = GetModules(client, null, token);
		foreach (ModuleInfo candidate in modules)
		{
			if (string.Equals(candidate.Name, module, StringComparison.OrdinalIgnoreCase))
			{
				return candidate;
			}
		}

		if (client.Inspection.TryResolveAddress(new SymbolExpression(module), AddressResolutionMode.Default,
				out Address address, out CheatEngineFailure failure, token))
		{
			foreach (ModuleInfo candidate in modules)
			{
				if (Contains(candidate, address.ToUInt64()))
				{
					return candidate;
				}
			}
		}
		else if (failure.Kind is not CheatEngineFailureKind.NotFound)
		{
			throw CheatEngineToolException.FromFailure(failure, client.Stopping.IsCancellationRequested);
		}

		throw CheatEngineToolException.NotFound($"No module is named {module} or contains the address it names.",
			ListHint);
	}

	/// <summary>Whether a module's mapped range contains an address; a module without a size contains its base only.</summary>
	/// <param name="module">The module.</param>
	/// <param name="address">The address.</param>
	/// <returns><see langword="true" /> when the address is inside the module.</returns>
	internal static bool Contains(ModuleInfo module, ulong address)
	{
		ulong start = module.BaseAddress.ToUInt64();
		ulong size = module.ImageSize?.Value ?? 1;
		return address >= start && address - start < size;
	}

	/// <summary>The mapped size used to bound reads: Cheat Engine's size, else the header's, else the headers alone.</summary>
	/// <param name="module">The module.</param>
	/// <param name="headerSize">The SizeOfImage field of the mapped header, when it was parsed.</param>
	/// <returns>The size in bytes.</returns>
	internal static ulong MappedSize(ModuleInfo module, uint? headerSize = null)
	{
		return module.ImageSize?.Value ?? headerSize ?? PeImageReader.MaximumHeaderBytes;
	}

	/// <summary>Reads the mapped PE header of a module, at most 4 KiB. Inside a dispatch.</summary>
	/// <param name="client">The activation's Client.</param>
	/// <param name="module">The module.</param>
	/// <param name="token">The dispatch body's token.</param>
	/// <returns>The confirmed header bytes; fewer than requested when the rest is unreadable.</returns>
	internal static byte[] ReadHeaderBytes(ICheatEngineClient client, ModuleInfo module, CancellationToken token)
	{
		int length = (int) Math.Min(MappedSize(module), PeImageReader.MaximumHeaderBytes);
		MemoryBytesReadOutcome outcome = client.Memory.ReadBytesDetailed(
			new MemoryBytesReadRequest(module.BaseAddress, length), token);
		if (outcome.Failure is { } failure && failure.Kind is not CheatEngineFailureKind.MemoryReadFailed)
		{
			throw CheatEngineToolException.FromFailure(failure, client.Stopping.IsCancellationRequested);
		}

		return outcome.Bytes.IsDefault ? [] : [.. outcome.Bytes];
	}
}
