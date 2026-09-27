using System.Collections.Immutable;
using System.Globalization;
using System.Text;

using CheatEngine.Client;
using CheatEngine.Client.Inspection;
using CheatEngine.Client.Processes;
using CheatEngine.Mcp.Core.Contract;
using CheatEngine.Mcp.Core.Values;
using CheatEngine.SDK.Engine.Inspection;
using CheatEngine.SDK.Engine.Values;

namespace CheatEngine.Mcp.Tools.Pointer;

/// <summary>Argument checks, address resolution and formatting shared by the pointer tools.</summary>
internal static class PointerSupport
{
	/// <summary>The most regions one capture or listing copies.</summary>
	internal const int RegionLimit = 16_384;

	/// <summary>The most modules one capture or listing copies.</summary>
	internal const int ModuleLimit = 4_096;

	internal const string TargetChangedHint = "Re-attach the process, then start again; never reuse a discarded result.";

	/// <summary>Checks a map or scan name before any Cheat Engine call.</summary>
	/// <param name="name">The name.</param>
	/// <param name="parameter">The parameter name.</param>
	/// <returns>The name.</returns>
	/// <exception cref="CheatEngineToolException"><c>invalid_argument</c>.</exception>
	internal static string Name(string? name, string parameter)
	{
		if (string.IsNullOrWhiteSpace(name) || name.Length > PointerStore.MaximumNameLength ||
			name.Any(char.IsControl))
		{
			throw CheatEngineToolException.InvalidArgument(parameter, string.Create(CultureInfo.InvariantCulture,
				$"must be 1 to {PointerStore.MaximumNameLength} printable characters."));
		}

		return name;
	}

	/// <summary>Checks an integer range before any Cheat Engine call.</summary>
	/// <param name="value">The value.</param>
	/// <param name="minimum">The smallest allowed value.</param>
	/// <param name="maximum">The largest allowed value.</param>
	/// <param name="parameter">The parameter name.</param>
	/// <exception cref="CheatEngineToolException"><c>invalid_argument</c> below, <c>limit_exceeded</c> above.</exception>
	internal static void Range(int value, int minimum, int maximum, string parameter)
	{
		if (value < minimum)
		{
			throw CheatEngineToolException.InvalidArgument(parameter, string.Create(CultureInfo.InvariantCulture,
				$"must be between {minimum} and {maximum}."));
		}

		if (value > maximum)
		{
			throw CheatEngineToolException.LimitExceeded(parameter, string.Create(CultureInfo.InvariantCulture,
				$"must be at most {maximum}."));
		}
	}

	/// <summary>Checks an address expression before any Cheat Engine call.</summary>
	/// <param name="expression">The expression.</param>
	/// <param name="parameter">The parameter name.</param>
	/// <returns>The trimmed expression.</returns>
	/// <exception cref="CheatEngineToolException"><c>invalid_argument</c> when it is empty or too long.</exception>
	internal static string Expression(string? expression, string parameter)
	{
		if (string.IsNullOrWhiteSpace(expression) || expression.Length > 1024)
		{
			throw CheatEngineToolException.InvalidArgument(parameter,
				"must be an address, symbol or Cheat Engine address expression of at most 1024 characters.");
		}

		return expression.Trim();
	}

	/// <summary>Resolves an address expression inside the current dispatch.</summary>
	/// <param name="client">The activation's Client.</param>
	/// <param name="expression">The checked expression.</param>
	/// <param name="cancellationToken">The dispatch body's token.</param>
	/// <returns>The address.</returns>
	internal static ulong Resolve(ICheatEngineClient client, string expression, CancellationToken cancellationToken)
	{
		return client.Inspection.ResolveAddress(new SymbolExpression(expression), AddressResolutionMode.Default,
			cancellationToken).ToUInt64();
	}

	/// <summary>The pointer width of the selected process, inside the current dispatch.</summary>
	/// <param name="process">The selected process.</param>
	/// <returns>4 or 8.</returns>
	/// <exception cref="CheatEngineToolException"><c>unsupported</c> when Cheat Engine does not report its bitness.</exception>
	internal static int Width(ProcessSnapshot process)
	{
		return process.Bitness.IsKnown && process.Bitness.Bytes is 4 or 8
			? process.Bitness.Bytes
			: throw CheatEngineToolException.Unsupported(
				"Cheat Engine does not report the target's pointer width; pointer tools need a 32- or 64-bit process.");
	}

	/// <summary>The modules of the selected process, sized by their image or, failing that, by their regions.</summary>
	/// <param name="client">The activation's Client.</param>
	/// <param name="regions">The process's regions, used when a module reports no image size.</param>
	/// <param name="cancellationToken">The dispatch body's token.</param>
	/// <returns>The modules with a known size.</returns>
	internal static PointerModule[] Modules(ICheatEngineClient client, ImmutableArray<MemoryRegionInfo>? regions,
		CancellationToken cancellationToken)
	{
		return Modules(client, regions, cancellationToken, out _);
	}

	/// <summary>
	///     The modules of the selected process, including whether the bounded inspection result may omit modules.
	/// </summary>
	/// <param name="client">The activation's Client.</param>
	/// <param name="regions">The process's regions, used when a module reports no image size.</param>
	/// <param name="cancellationToken">The dispatch body's token.</param>
	/// <param name="truncated">
	///     <see langword="true" /> when the inspection result filled the requested limit, so the host may have more
	///     modules that were not returned.
	/// </param>
	/// <returns>The modules with a known size.</returns>
	internal static PointerModule[] Modules(ICheatEngineClient client, ImmutableArray<MemoryRegionInfo>? regions,
		CancellationToken cancellationToken, out bool truncated)
	{
		ImmutableArray<ModuleInfo> modules =
			client.Inspection.GetModules(new InspectionCollectionRequest(ModuleLimit), null, cancellationToken);
		truncated = modules.Length >= ModuleLimit;
		List<PointerModule> sized = [];
		foreach (ModuleInfo module in modules)
		{
			ulong size = module.ImageSize?.Value ?? SizeFromRegions(module.BaseAddress.ToUInt64(), regions);
			if (size != 0 && !string.IsNullOrEmpty(module.Name))
			{
				sized.Add(new PointerModule(module.Name, module.BaseAddress.ToUInt64(), size));
			}
		}

		return [.. sized];
	}

	/// <summary>A module-relative name, such as <c>game.exe+1A2B30</c>, or <see langword="null" /> outside modules.</summary>
	/// <param name="modules">The modules.</param>
	/// <param name="address">The address.</param>
	/// <returns>The name.</returns>
	internal static string? Symbol(IEnumerable<PointerModule> modules, ulong address)
	{
		PointerModule? module = modules.FirstOrDefault(module => module.Contains(address));
		return module is null ? null : $"{module.Name}+{HexFormat.Address(address - module.BaseAddress)}";
	}

	/// <summary>
	///     A chain as a Cheat Engine address expression: one bracket pair per dereference, so offsets
	///     <c>[10, 4C8]</c> on <c>root</c> read <c>[[root]+10]+4C8</c>.
	/// </summary>
	/// <param name="root">The root expression.</param>
	/// <param name="offsets">The offsets in dereference order.</param>
	/// <returns>The expression.</returns>
	internal static string ChainExpression(string root, IReadOnlyList<long> offsets)
	{
		StringBuilder text = new();
		text.Append('[', offsets.Count).Append(root);
		foreach (long offset in offsets)
		{
			text.Append(']').Append(offset < 0 ? '-' : '+').Append(HexFormat.Offset(offset).TrimStart('-'));
		}

		return text.ToString();
	}

	/// <summary>The root of a stored path: <c>"game.exe"+1A2B30</c>, or an absolute address.</summary>
	/// <param name="path">The path.</param>
	/// <returns>The root expression.</returns>
	internal static string Root(PointerPath path)
	{
		return path.Module is null
			? HexFormat.Address(path.BaseAddress)
			: $"\"{path.Module}\"+{HexFormat.Address(path.ModuleOffset)}";
	}

	/// <summary>The <c>target_changed</c> error of a job or chunked tool whose process selection changed.</summary>
	/// <param name="message">What was discarded.</param>
	/// <returns>The exception to throw.</returns>
	internal static CheatEngineToolException TargetChanged(string message)
	{
		return new CheatEngineToolException(new ToolError(ToolErrorKind.TargetChanged, message, null,
			ToolHostEffect.NotStarted, false, TargetChangedHint));
	}

	private static ulong SizeFromRegions(ulong moduleBase, ImmutableArray<MemoryRegionInfo>? regions)
	{
		if (regions is not { } known)
		{
			return 0;
		}

		ulong size = 0;
		foreach (MemoryRegionInfo region in known)
		{
			ulong start = region.BaseAddress.ToUInt64();
			ulong length = region.Size.Value;
			if (region.AllocationBase.ToUInt64() == moduleBase && start >= moduleBase &&
				start - moduleBase <= ulong.MaxValue - length)
			{
				size = Math.Max(size, start - moduleBase + length);
			}
		}

		return size;
	}
}
