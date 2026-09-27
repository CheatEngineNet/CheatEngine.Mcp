using System.Collections.Immutable;

using CheatEngine.Client.Inspection;
using CheatEngine.Client.Memory;
using CheatEngine.Client.Results;
using CheatEngine.Mcp.Core.Contract;
using CheatEngine.Mcp.Core.Values;
using CheatEngine.Mcp.Tools.Memory;
using CheatEngine.SDK.Engine.Enums;
using CheatEngine.SDK.Engine.Inspection;
using CheatEngine.SDK.Engine.Values;

using Xunit.Sdk;

namespace CheatEngine.Mcp.Tests.Tools.Memory;

/// <summary>Address descriptions and the region map: one dispatch, in-band item errors, filters and paging.</summary>
public sealed class MemoryInfoToolsTests
{
	private static CancellationToken Token => TestContext.Current.CancellationToken;

	private static readonly ImmutableArray<ModuleInfo> Modules =
	[
		new("game.exe", new Address(0x400000), new MemorySize(0x10000), true, @"C:\game\game.exe"),
		new("ntdll.dll", new Address(0x7FF800000000), new MemorySize(0x200000), true, @"C:\Windows\ntdll.dll")
	];

	[Fact]
	public void GetAddressInfo_ResolvedAndUnresolved_DescribesEachInOneDispatch()
	{
		TargetDouble target = new();
		target.Symbols["player"] = 0x401010;
		target.Inspection = Inspection;
		target.Memory = (method, arguments) =>
		{
			Assert.Equal(nameof(IMemoryClient.TryReadPrimitive), method.Name);
			arguments[1] = new Address(0x12345678);
			arguments[2] = default(CheatEngineFailure);
			return true;
		};
		string? source = null;
		target.LuaResult = script =>
		{
			source = script;
			return new AddressExtras([false], ["Player"]);
		};

		AddressInfoResult result = new MemoryInfoTools(target.Dispatch).GetAddressInfo(["player", "missing"], true,
			true, cancellationToken: Token);

		AddressInfo player = result.Items[0];
		Assert.Equal(("401010", "game.exe+1010", "game.exe", ".text", false), (player.Address, player.Symbol,
			player.Module, player.Section, player.IsSystemModule));
		Assert.Equal(("12345678", "Player"), (player.PointerValue, player.RttiClass));
		Assert.Equal(new MemoryRegionDetail("401000", 0x1000, RegionState.Committed,
			new MemoryAccess(true, false, true, false), RegionType.Image, "400000"), player.Region);
		Assert.Equal(("missing", ToolErrorKind.NotFound), (result.Items[1].Address, result.Items[1].Error!.Kind));
		Assert.Contains("[1] = {0x401010,}, [2] = true", source, StringComparison.Ordinal);
		Assert.Equal(1, target.Dispatcher.Calls);
		Assert.Equal(1, target.LuaCalls);
	}

	[Fact]
	public void GetAddressInfo_NoAddressResolves_SkipsTheScript()
	{
		TargetDouble target = new()
		{
			Inspection = Inspection
		};

		AddressInfoResult result = new MemoryInfoTools(target.Dispatch).GetAddressInfo(["missing"], cancellationToken: Token);

		Assert.Equal(ToolErrorKind.NotFound, Assert.Single(result.Items).Error!.Kind);
		Assert.Equal(0, target.LuaCalls);
	}

	[Fact]
	public void GetAddressInfo_TooManyAddresses_RefusesBeforeAnyDispatch()
	{
		TargetDouble target = new();

		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() =>
			new MemoryInfoTools(target.Dispatch).GetAddressInfo([.. Enumerable.Repeat("1000", 257)], cancellationToken: Token));

		Assert.Equal(ToolErrorKind.LimitExceeded, exception.Error.Kind);
		Assert.Equal(0, target.Dispatcher.Calls);
	}

	[Fact]
	public void ListRegions_Filters_KeepMatchingRegionsInAddressOrderAndPage()
	{
		TargetDouble target = new()
		{
			Inspection = Inspection
		};

		RegionList all = new MemoryInfoTools(target.Dispatch).ListRegions(state: RegionStateFilter.Any, cancellationToken: Token);
		RegionList code = new MemoryInfoTools(target.Dispatch).ListRegions(executable: ProtectionRequirement.Required,
			format: ResultFormat.Detailed, cancellationToken: Token);
		RegionList page = new MemoryInfoTools(target.Dispatch).ListRegions(limit: 1, cancellationToken: Token);
		RegionList module = new MemoryInfoTools(target.Dispatch).ListRegions(module: "game.exe",
			writable: ProtectionRequirement.Excluded, cancellationToken: Token);

		Assert.Equal(["10000", "401000", "402000", "500000"], all.Regions.Select(static region => region.Base));
		MemoryRegionEntry text = Assert.Single(code.Regions);
		Assert.Equal(("401000", "game.exe", "400000"), (text.Base, text.Module, text.AllocationBase));
		Assert.Equal(new MemoryAccess(true, true, true, true), text.AllocationProtection);
		Assert.Equal((3, 1), (page.Total, page.NextOffset));
		Assert.Equal(["401000"], module.Regions.Select(static region => region.Base));
		Assert.Null(all.NextOffset);
	}

	[Fact]
	public void ListRegions_LimitAbove2000_RefusesBeforeAnyDispatch()
	{
		TargetDouble target = new();

		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() =>
			new MemoryInfoTools(target.Dispatch).ListRegions(limit: 2001, cancellationToken: Token));

		Assert.Equal(ToolErrorKind.LimitExceeded, exception.Error.Kind);
		Assert.Equal(0, target.Dispatcher.Calls);
	}

	private static object? Inspection(System.Reflection.MethodInfo method, object?[] arguments)
	{
		switch (method.Name)
		{
			case nameof(IInspectionClient.GetModules):
				return Modules;
			case nameof(IInspectionClient.TryResolveName):
				arguments[1] = "game.exe+1010";
				arguments[2] = default(CheatEngineFailure);
				return true;
			case nameof(IInspectionClient.TryGetModuleSections):
				Assert.Equal("game.exe", ((ModuleName) arguments[0]!).Value);
				arguments[2] = ImmutableArray.Create(
					new ModuleSectionInfo(".text", new MemorySize(0x1000), new Address(0x401000),
						new ModuleFileOffset(0x400)),
					new ModuleSectionInfo(".data", new MemorySize(0x1000), new Address(0x402000),
						new ModuleFileOffset(0x1400)));
				arguments[3] = default(CheatEngineFailure);
				return true;
			case nameof(IInspectionClient.TryGetMemoryRegion):
				arguments[1] = Region(0x401000, 0x1000, MemoryProtection.ExecuteRead);
				arguments[2] = default(CheatEngineFailure);
				return true;
			case nameof(IInspectionClient.GetMemoryRegions):
				return ImmutableArray.Create(
					Region(0x500000, 0x1000, MemoryProtection.ReadWrite, MemoryRegionType.Private),
					Region(0x401000, 0x1000, MemoryProtection.ExecuteRead),
					Region(0x10000, 0x10000, MemoryProtection.None, 0, MemoryRegionState.Free),
					Region(0x402000, 0x1000, MemoryProtection.ReadWrite));
			default:
				throw new XunitException($"Unexpected inspection call {method.Name}.");
		}
	}

	private static MemoryRegionInfo Region(ulong baseAddress, ulong size, MemoryProtection protection,
		MemoryRegionType type = MemoryRegionType.Image, MemoryRegionState state = MemoryRegionState.Committed)
	{
		return new MemoryRegionInfo(new Address(baseAddress),
			new Address(type is MemoryRegionType.Image ? 0x400000UL : baseAddress),
			MemoryProtection.ExecuteWriteCopy, new MemorySize(size), state, protection, type, null);
	}
}
