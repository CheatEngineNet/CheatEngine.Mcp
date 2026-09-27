using System.Collections.Immutable;
using System.ComponentModel;
using System.Globalization;

using CheatEngine.Client;
using CheatEngine.Client.Inspection;
using CheatEngine.Client.Results;
using CheatEngine.Mcp.Core.Contract;
using CheatEngine.Mcp.Core.Values;
using CheatEngine.SDK.Engine.Inspection;
using CheatEngine.SDK.Engine.Values;

using ModelContextProtocol.Server;

namespace CheatEngine.Mcp.Tools.Memory;

/// <summary>The <c>memory_*</c> tools that describe addresses and the target's memory map.</summary>
[McpServerToolType]
public sealed class MemoryInfoTools
{
	/// <summary>The most addresses <c>memory_get_address_info</c> describes.</summary>
	internal const int MaximumAddresses = 256;

	/// <summary>The most regions copied from Cheat Engine's region map.</summary>
	internal const int MaximumRegions = 16384;

	/// <summary>The largest page of <c>memory_list_regions</c>.</summary>
	internal const int MaximumPage = 2000;

	private const int MaximumSections = 256;

	private readonly ToolDispatch _dispatch;

	/// <summary>Creates the tools; the constructor does no Cheat Engine work.</summary>
	/// <param name="dispatch">The activation's dispatch facade.</param>
	public MemoryInfoTools(ToolDispatch dispatch)
	{
		ArgumentNullException.ThrowIfNull(dispatch);
		_dispatch = dispatch;
	}

	/// <summary>Describes up to 256 addresses: symbol, module, section, region and optional pointer value and RTTI class.</summary>
	[McpServerTool(Name = CheatEngineToolNames.MemoryGetAddressInfo, Title = "Describe addresses", ReadOnly = true,
		Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
	[McpMeta(McpDispatchClass.MetaKey, McpDispatchClass.Short)]
	[Description(
		"Describe up to 256 addresses: Cheat Engine's symbol name, the containing module and section, whether it is a system module, the memory region (state, protection, backing) and optionally the pointer-sized value stored there and the RTTI class name of the object there. An address that does not resolve reports its own error in band.")]
	public AddressInfoResult GetAddressInfo(
		[Description("The addresses or Cheat Engine address expressions to describe, 1 to 256.")]
		string[] addresses,
		[Description("Whether to look up the RTTI class name of the object at each address (MSVC C++ objects with a vtable).")]
		bool includeRtti = false,
		[Description("Whether to read the pointer-sized value stored at each address.")]
		bool includePointerValue = false,
		CancellationToken cancellationToken = default)
	{
		string[] expressions = CheckAddresses(addresses);
		return _dispatch.Run(CheatEngineToolNames.MemoryGetAddressInfo,
			token => Describe(expressions, includeRtti, includePointerValue, token), cancellationToken);
	}

	/// <summary>Pages the target's memory regions with address, module, state, backing and protection filters.</summary>
	[McpServerTool(Name = CheatEngineToolNames.MemoryListRegions, Title = "List memory regions", ReadOnly = true,
		Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
	[McpMeta(McpDispatchClass.MetaKey, McpDispatchClass.Short)]
	[Description(
		"List the target's memory regions in address order, one page at a time, filtered by address range, module, state (committed by default), backing and the writable, executable and copy-on-write flags. The detailed format adds the allocation base, allocation protection and mapped file.")]
	public RegionList ListRegions(
		[Description("Keep only regions that end after this address or expression; use with endAddress or alone.")]
		string? startAddress = null,
		[Description("Keep only regions that start at or before this address or expression.")]
		string? endAddress = null,
		[Description("Keep only regions inside this loaded module's image, such as game.exe.")]
		string? module = null,
		[Description("The region state to keep: committed (default), reserved, free or any.")]
		RegionStateFilter state = RegionStateFilter.Committed,
		[Description("Whether regions must be writable: required, excluded or any (default).")]
		ProtectionRequirement writable = ProtectionRequirement.Any,
		[Description("Whether regions must be executable: required, excluded or any (default).")]
		ProtectionRequirement executable = ProtectionRequirement.Any,
		[Description("Whether regions must be copy-on-write: required, excluded or any (default).")]
		ProtectionRequirement copyOnWrite = ProtectionRequirement.Any,
		[Description("The backing to keep: image, mapped, private or any (default).")]
		RegionTypeFilter type = RegionTypeFilter.Any,
		[Description("The zero-based index of the first region to return.")]
		int offset = 0,
		[Description("The most regions to return, 1 to 2000.")]
		int limit = 500,
		[Description("concise (default) or detailed, which adds allocation base, allocation protection and mapped file.")]
		ResultFormat format = ResultFormat.Concise,
		CancellationToken cancellationToken = default)
	{
		string? start = startAddress is null ? null : MemoryTargets.RequireExpression(startAddress, "startAddress");
		string? end = endAddress is null ? null : MemoryTargets.RequireExpression(endAddress, "endAddress");
		string? moduleName = MemoryTargets.OptionalModule(module);
		if (offset < 0)
		{
			throw CheatEngineToolException.InvalidArgument("offset", "must be zero or greater.");
		}

		MemoryTargets.RequireRange(limit, "limit", 1, MaximumPage);
		RegionFilter filter = new(state, type, writable, executable, copyOnWrite);
		bool detailed = format is ResultFormat.Detailed;
		MemoryRegionEntry[] regions = _dispatch.Run(CheatEngineToolNames.MemoryListRegions,
			token => Regions(start, end, moduleName, filter, detailed, token), cancellationToken);
		PageSlice<MemoryRegionEntry> page = Paging.Slice(regions, offset, limit, MaximumPage);
		return new RegionList(page.Total, page.NextOffset, page.Truncated, [.. page.Items]);
	}

	private static string[] CheckAddresses(string[]? addresses)
	{
		if (addresses is null || addresses.Length == 0)
		{
			throw CheatEngineToolException.InvalidArgument("addresses", "must list at least one address.");
		}

		if (addresses.Length > MaximumAddresses)
		{
			throw CheatEngineToolException.LimitExceeded("addresses",
				$"accepts at most {MaximumAddresses} addresses.");
		}

		string[] expressions = new string[addresses.Length];
		for (int index = 0; index < addresses.Length; index++)
		{
			expressions[index] = MemoryTargets.RequireExpression(addresses[index],
				$"addresses[{index.ToString(CultureInfo.InvariantCulture)}]");
		}

		return expressions;
	}

	private AddressInfoResult Describe(string[] expressions, bool includeRtti, bool includePointerValue,
		CancellationToken cancellationToken)
	{
		ICheatEngineClient client = _dispatch.Client;
		ImmutableArray<ModuleInfo> modules = client.Inspection.GetModules(
			new InspectionCollectionRequest(MemoryTargets.MaximumModules), null, cancellationToken);
		Dictionary<string, ImmutableArray<ModuleSectionInfo>> sections = new(StringComparer.OrdinalIgnoreCase);
		AddressInfo[] items = new AddressInfo[expressions.Length];
		List<int> resolved = [];
		Address[] addresses = new Address[expressions.Length];
		for (int index = 0; index < expressions.Length; index++)
		{
			if (!MemoryTargets.TryResolve(client, expressions[index], out addresses[index],
					out CheatEngineFailure failure, cancellationToken))
			{
				items[index] = MemoryTargets.IsItemFailure(failure)
					? new AddressInfo(expressions[index], Error: MemoryTargets.ItemError(client, failure))
					: throw CheatEngineToolException.FromFailure(failure, client.Stopping.IsCancellationRequested);
				continue;
			}

			resolved.Add(index);
			items[index] = Describe(client, addresses[index], modules, sections, includePointerValue,
				cancellationToken);
		}

		if (resolved.Count == 0)
		{
			return new AddressInfoResult(items);
		}

		ulong[] values = [.. resolved.Select(index => addresses[index].ToUInt64())];
		AddressExtras extras = _dispatch.ExecuteLua(CheatEngineToolNames.MemoryGetAddressInfo,
			MemoryScripts.AddressExtras, MemoryJsonContext.Default.AddressExtras, cancellationToken, values,
			includeRtti);
		for (int position = 0; position < resolved.Count; position++)
		{
			int index = resolved[position];
			items[index] = items[index] with
			{
				IsSystemModule = position < extras.System.Length && extras.System[position],
				RttiClass = position < extras.Rtti.Length ? extras.Rtti[position] : null
			};
		}

		return new AddressInfoResult(items);
	}

	private static AddressInfo Describe(ICheatEngineClient client, Address address,
		ImmutableArray<ModuleInfo> modules, Dictionary<string, ImmutableArray<ModuleSectionInfo>> sections,
		bool includePointerValue, CancellationToken cancellationToken)
	{
		string text = HexFormat.Address(address);
		string symbol = client.Inspection.TryResolveName(address, out string? name, out _, cancellationToken) &&
						!string.IsNullOrEmpty(name)
			? name
			: text;
		ModuleInfo? module = MemoryTargets.ContainingModule(modules, address);
		string? section = null;
		if (module is { } owner)
		{
			if (!sections.TryGetValue(owner.Name, out ImmutableArray<ModuleSectionInfo> list))
			{
				list = client.Inspection.TryGetModuleSections(new ModuleName(owner.Name),
					new InspectionCollectionRequest(MaximumSections), out ImmutableArray<ModuleSectionInfo> copied,
					out _, cancellationToken)
					? copied
					: [];
				sections[owner.Name] = list;
			}

			section = Section(list, address);
		}

		MemoryRegionDetail? region =
			client.Inspection.TryGetMemoryRegion(address, out MemoryRegionInfo info, out _, cancellationToken)
				? MemoryTargets.Detail(info)
				: null;
		string? pointer = includePointerValue &&
						  client.Memory.TryReadPrimitive(address, out Address value, out _, cancellationToken)
			? HexFormat.Address(value)
			: null;
		return new AddressInfo(text, symbol, module?.Name, section, Region: region, PointerValue: pointer);
	}

	private static string? Section(ImmutableArray<ModuleSectionInfo> sections, Address address)
	{
		ulong value = address.ToUInt64();
		foreach (ModuleSectionInfo section in sections)
		{
			ulong start = section.Address.ToUInt64();
			if (value >= start && value - start < section.Size.Value)
			{
				return section.Name;
			}
		}

		return null;
	}

	private MemoryRegionEntry[] Regions(string? start, string? end, string? moduleName, RegionFilter filter,
		bool detailed, CancellationToken cancellationToken)
	{
		ICheatEngineClient client = _dispatch.Client;
		ulong low = start is null ? 0 : MemoryTargets.Resolve(client, start, "startAddress", cancellationToken)
			.ToUInt64();
		ulong high = end is null
			? ulong.MaxValue
			: MemoryTargets.Resolve(client, end, "endAddress", cancellationToken).ToUInt64();
		if (low > high)
		{
			throw CheatEngineToolException.InvalidArgument("endAddress", "must not be below startAddress.");
		}

		ImmutableArray<ModuleInfo> modules = client.Inspection.GetModules(
			new InspectionCollectionRequest(MemoryTargets.MaximumModules), null, cancellationToken);
		if (moduleName is not null)
		{
			ModuleInfo owner = MemoryTargets.FindModule(modules, moduleName) ??
							   throw CheatEngineToolException.NotFound($"No loaded module is named '{moduleName}'.",
								   "List the loaded modules with module_list.");
			ulong moduleStart = owner.BaseAddress.ToUInt64();
			ulong moduleEnd = owner.ImageSize is { } size
				? moduleStart + size.Value - 1
				: throw CheatEngineToolException.Unsupported(
					$"Cheat Engine does not report the size of '{owner.Name}', so its regions cannot be selected.");
			low = Math.Max(low, moduleStart);
			high = Math.Min(high, moduleEnd);
		}

		ImmutableArray<MemoryRegionInfo> map = client.Inspection.GetMemoryRegions(
			new InspectionCollectionRequest(MaximumRegions), cancellationToken);
		List<MemoryRegionEntry> regions = [];
		foreach (MemoryRegionInfo region in map.OrderBy(static region => region.BaseAddress.ToUInt64()))
		{
			ulong regionStart = region.BaseAddress.ToUInt64();
			ulong regionSize = region.Size.Value;
			if (regionSize == 0 || regionStart > high || regionStart + (regionSize - 1) < low)
			{
				continue;
			}

			RegionState state = MemoryTargets.State(region.State);
			RegionType backing = MemoryTargets.Type(region.Type);
			MemoryAccess access = MemoryTargets.Access(region.Protection);
			if (!filter.Keeps(state, backing, access))
			{
				continue;
			}

			string? owner = backing is RegionType.Image
				? MemoryTargets.ContainingModule(modules, region.BaseAddress)?.Name
				: null;
			regions.Add(detailed
				? new MemoryRegionEntry(HexFormat.Address(region.BaseAddress), MemoryTargets.Size(region.Size), state,
					access, backing, owner, HexFormat.Address(region.AllocationBase),
					MemoryTargets.Access(region.AllocationProtection),
					string.IsNullOrEmpty(region.Extra) ? null : region.Extra)
				: new MemoryRegionEntry(HexFormat.Address(region.BaseAddress), MemoryTargets.Size(region.Size), state,
					access, backing, owner));
		}

		return [.. regions];
	}

	private sealed record RegionFilter(
		RegionStateFilter State,
		RegionTypeFilter Type,
		ProtectionRequirement Writable,
		ProtectionRequirement Executable,
		ProtectionRequirement CopyOnWrite)
	{
		internal bool Keeps(RegionState state, RegionType type, MemoryAccess access)
		{
			bool stateMatches = State switch
			{
				RegionStateFilter.Committed => state is RegionState.Committed,
				RegionStateFilter.Reserved => state is RegionState.Reserved,
				RegionStateFilter.Free => state is RegionState.Free,
				_ => true
			};
			bool typeMatches = Type switch
			{
				RegionTypeFilter.Image => type is RegionType.Image,
				RegionTypeFilter.Mapped => type is RegionType.Mapped,
				RegionTypeFilter.Private => type is RegionType.Private,
				_ => true
			};
			return stateMatches && typeMatches && Matches(Writable, access.Write) &&
				   Matches(Executable, access.Execute) && Matches(CopyOnWrite, access.CopyOnWrite);
		}

		private static bool Matches(ProtectionRequirement requirement, bool flag)
		{
			return requirement switch
			{
				ProtectionRequirement.Required => flag,
				ProtectionRequirement.Excluded => !flag,
				_ => true
			};
		}
	}
}
