using System.Collections.Immutable;
using System.Reflection;
using System.Text.Json;

using CheatEngine.Client.Inspection;
using CheatEngine.Client.Memory;
using CheatEngine.Client.Results;
using CheatEngine.Mcp.Core.Contract;
using CheatEngine.Mcp.Core.Inspection;
using CheatEngine.Mcp.Core.Values;
using CheatEngine.Mcp.Tests.Support;
using CheatEngine.Mcp.Tools.Memory;
using CheatEngine.SDK.Engine.Enums;
using CheatEngine.SDK.Engine.Inspection;
using CheatEngine.SDK.Engine.Values;

using Xunit.Sdk;

namespace CheatEngine.Mcp.Tests.Tools.Memory;

/// <summary>Address descriptions and the region map: one dispatch, in-band item errors, filters and paging.</summary>
public sealed class MemoryInfoToolsTests
{
	private static readonly ImmutableArray<ModuleInfo> Modules =
	[
		new("game.exe", new Address(0x400000), new MemorySize(0x10000), true, @"C:\game\game.exe"),
		new("ntdll.dll", new Address(0x7FF800000000), new MemorySize(0x200000), true, @"C:\Windows\ntdll.dll")
	];

	private static CancellationToken Token => TestContext.Current.CancellationToken;

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

		AddressInfoResult result = Tools(target).GetAddressInfo(["player", "missing"], true,
			true, Token);

		AddressInfo player = result.Items[0];
		Assert.Equal(("401010", "game.exe+1010", "game.exe", ".text", (bool?) false), (player.Address, player.Symbol,
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
	public void AddressInfo_SystemModuleAvailability_OmitsUnavailableAndRetainsBooleanAnswers()
	{
		AddressInfoResult value = new(
		[
			new AddressInfo("1000", IsSystemModule: null),
			new AddressInfo("2000", IsSystemModule: false),
			new AddressInfo("3000", IsSystemModule: true)
		]);

		using JsonDocument json = JsonDocument.Parse(
			JsonSerializer.Serialize(value, MemoryJsonContext.Default.AddressInfoResult));
		JsonElement[] items = json.RootElement.GetProperty("items").EnumerateArray().ToArray();

		Assert.False(items[0].TryGetProperty("isSystemModule", out _));
		Assert.False(items[1].GetProperty("isSystemModule").GetBoolean());
		Assert.True(items[2].GetProperty("isSystemModule").GetBoolean());
	}

	[Fact]
	public void GetAddressInfo_UnavailableSystemModuleStatus_RetainsNull()
	{
		TargetDouble target = new();
		target.Symbols["player"] = 0x401010;
		target.Inspection = Inspection;
		target.LuaResult = _ => new AddressExtras([null], [null]);

		AddressInfo result = Assert.Single(
			Tools(target).GetAddressInfo(["player"], cancellationToken: Token).Items);

		Assert.Null(result.IsSystemModule);
	}

	[Fact]
	public void GetAddressInfo_NoAddressResolves_SkipsTheScript()
	{
		TargetDouble target = new()
		{
			Inspection = Inspection
		};

		AddressInfoResult result =
			Tools(target).GetAddressInfo(["missing"], cancellationToken: Token);

		Assert.Equal(ToolErrorKind.NotFound, Assert.Single(result.Items).Error!.Kind);
		Assert.Equal(0, target.LuaCalls);
	}

	[Fact]
	public void GetAddressInfo_TooManyAddresses_RefusesBeforeAnyDispatch()
	{
		TargetDouble target = new();

		CheatEngineToolException exception = Assert.Throws<CheatEngineToolException>(() =>
			Tools(target).GetAddressInfo([.. Enumerable.Repeat("1000", 257)],
				cancellationToken: Token));

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

		RegionList all =
			Tools(target).ListRegions(state: RegionStateFilter.Any, cancellationToken: Token);
		RegionList code = Tools(target).ListRegions(executable: ProtectionRequirement.Required,
			format: ResultFormat.Detailed, cancellationToken: Token);
		RegionList page = Tools(target).ListRegions(limit: 1, cancellationToken: Token);
		RegionList module = Tools(target).ListRegions(module: "game.exe",
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
			Tools(target).ListRegions(limit: 2001, cancellationToken: Token));

		Assert.Equal(ToolErrorKind.LimitExceeded, exception.Error.Kind);
		Assert.Equal(0, target.Dispatcher.Calls);
	}

	[Fact]
	public void ListPreparedRegions_ProjectsTheExactDefaultExplicitResultWithoutInspectionCalls()
	{
		TargetDouble target = new()
		{
			Inspection = Inspection
		};
		PreparedInspectionStore prepared = new(TimeProvider.System);
		MemoryInfoTools tools = Tools(target, prepared);

		RegionList explicitResult = tools.ListRegions(cancellationToken: Token);
		string[] before = target.CallsTo("Inspection");
		RegionList projected = tools.ListPreparedRegions(cancellationToken: Token);

		Assert.Equal(JsonSerializer.Serialize(explicitResult, MemoryJsonContext.Default.RegionList),
			JsonSerializer.Serialize(projected, MemoryJsonContext.Default.RegionList));
		Assert.Equal(before, target.CallsTo("Inspection"));
	}

	[Fact]
	public void ListPreparedRegions_UsesTheCompleteMapAfterAnExplicitFilteredRead()
	{
		TargetDouble target = new()
		{
			Inspection = Inspection
		};
		MemoryInfoTools tools = Tools(target);

		RegionList filtered = tools.ListRegions(state: RegionStateFilter.Any,
			executable: ProtectionRequirement.Required, format: ResultFormat.Detailed, cancellationToken: Token);
		RegionList projected = tools.ListPreparedRegions(cancellationToken: Token);

		Assert.Equal(["401000"], filtered.Regions.Select(static region => region.Base));
		Assert.Equal(["401000", "402000", "500000"], projected.Regions.Select(static region => region.Base));
		Assert.Equal(3, projected.Total);
	}

	[Fact]
	public void ListPreparedRegions_ColdStaleAndChangedTarget_RefuseWithoutEnumeration()
	{
		TargetDouble target = new()
		{
			Inspection = Inspection
		};
		ManualTimeProvider time = new();
		PreparedInspectionStore prepared = new(time);
		MemoryInfoTools tools = Tools(target, prepared);

		CheatEngineToolException cold = Assert.Throws<CheatEngineToolException>(() =>
			tools.ListPreparedRegions(cancellationToken: Token));
		Assert.Equal(ToolErrorKind.InvalidState, cold.Error.Kind);
		Assert.Empty(target.CallsTo("Inspection"));

		tools.ListRegions(cancellationToken: Token);
		time.Advance(PreparedInspectionStore.Lifetime);
		string[] beforeStaleRead = target.CallsTo("Inspection");
		CheatEngineToolException stale = Assert.Throws<CheatEngineToolException>(() =>
			tools.ListPreparedRegions(cancellationToken: Token));
		Assert.Equal(ToolErrorKind.InvalidState, stale.Error.Kind);
		Assert.Equal(beforeStaleRead, target.CallsTo("Inspection"));

		target.Epochs.Enqueue(1);
		target.Epochs.Enqueue(2);
		CheatEngineToolException changed = Assert.Throws<CheatEngineToolException>(() =>
			tools.ListRegions(cancellationToken: Token));
		Assert.Equal((ToolErrorKind.TargetChanged, ToolHostEffect.Completed),
			(changed.Error.Kind, changed.Error.HostEffect));
	}

	private static MemoryInfoTools Tools(TargetDouble target, PreparedInspectionStore? prepared = null)
	{
		return new MemoryInfoTools(target.Dispatch, prepared ?? new PreparedInspectionStore(TimeProvider.System));
	}

	private static object? Inspection(MethodInfo method, object?[] arguments)
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
