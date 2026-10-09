using System.Collections.Immutable;

using CheatEngine.Client.Processes;
using CheatEngine.Client.Results;
using CheatEngine.Mcp.Core.Contract;
using CheatEngine.Mcp.Core.Inspection;
using CheatEngine.Mcp.Tests.Support;
using CheatEngine.SDK.Engine.Inspection;
using CheatEngine.SDK.Engine.Runtime;

namespace CheatEngine.Mcp.Tests.Core;

public sealed class PreparedInspectionStoreTests
{
	[Fact]
	public void Modules_ExpireAtTheLifetimeBoundaryAndRejectWrongIdentity()
	{
		ManualTimeProvider time = new();
		PreparedInspectionStore store = new(time);
		ProcessSnapshot target = Target(42, 7);
		store.PublishModules(target, ImmutableArray<ModuleInfo>.Empty);

		Assert.True(store.TryGetModules(target, out _));
		Assert.False(store.TryGetModules(Target(43, 7), out _));
		time.Advance(PreparedInspectionStore.Lifetime - TimeSpan.FromTicks(1));
		Assert.True(store.TryGetModules(target, out _));
		time.Advance(TimeSpan.FromTicks(1));
		Assert.False(store.TryGetModules(target, out _));
	}

	[Fact]
	public void Modules_OlderEpochCannotReplaceNewerPublication()
	{
		PreparedInspectionStore store = new(new ManualTimeProvider());
		PreparedModuleSnapshot newer = store.PublishModules(Target(42, 8), ImmutableArray<ModuleInfo>.Empty);
		CheatEngineToolException rejected = Assert.Throws<CheatEngineToolException>(() =>
			store.PublishModules(Target(42, 7), ImmutableArray<ModuleInfo>.Empty));

		Assert.Equal(ToolErrorKind.TargetChanged, rejected.Error.Kind);
		Assert.True(store.TryGetModules(Target(42, 8), out PreparedModuleSnapshot current));
		Assert.Equal(newer.Version, current.Version);
	}

	[Fact]
	public void MemoryMap_StalePublicationAfterNewerModulePublicationIsRejected()
	{
		PreparedInspectionStore store = new(new ManualTimeProvider());
		store.PublishModules(Target(42, 8), ImmutableArray<ModuleInfo>.Empty);

		Assert.Throws<CheatEngineToolException>(() => store.PublishMemoryMap(Target(42, 7),
			ImmutableArray<ModuleInfo>.Empty, ImmutableArray<MemoryRegionInfo>.Empty));
	}

	[Fact]
	public void ReadOfNewerTargetRejectsDelayedOlderPublication()
	{
		PreparedInspectionStore store = new(new ManualTimeProvider());
		store.PublishModules(Target(42, 7), ImmutableArray<ModuleInfo>.Empty);
		Assert.False(store.TryGetModules(Target(42, 8), out _));

		Assert.Throws<CheatEngineToolException>(() =>
			store.PublishModules(Target(42, 7), ImmutableArray<ModuleInfo>.Empty));
	}

	[Fact]
	public void IsCurrent_RejectsExpiredVersion()
	{
		ManualTimeProvider time = new();
		PreparedInspectionStore store = new(time);
		PreparedModuleSnapshot snapshot = store.PublishModules(Target(42, 7), ImmutableArray<ModuleInfo>.Empty);
		Assert.True(store.IsCurrent(Target(42, 7), snapshot.Version));
		time.Advance(PreparedInspectionStore.Lifetime);

		Assert.False(store.IsCurrent(Target(42, 7), snapshot.Version));
	}

	[Fact]
	public void MemoryMap_PublicationSharesModuleVersionAndModuleRefreshInvalidatesMap()
	{
		ManualTimeProvider time = new();
		PreparedInspectionStore store = new(time);
		ProcessSnapshot target = Target(42, 7);
		PreparedMemoryMapSnapshot map = store.PublishMemoryMap(target, ImmutableArray<ModuleInfo>.Empty,
			ImmutableArray<MemoryRegionInfo>.Empty);
		Assert.True(store.TryGetModules(target, out PreparedModuleSnapshot modules));
		Assert.Equal(map.Version, modules.Version);
		Assert.True(store.TryGetMemoryMap(target, out PreparedMemoryMapSnapshot read));
		Assert.Same(map, read);

		PreparedModuleSnapshot replacement = store.PublishModules(target, ImmutableArray<ModuleInfo>.Empty);

		Assert.True(replacement.Version.Revision > map.Version.Revision);
		Assert.False(store.IsCurrent(target, map.Version));
		Assert.False(store.TryGetMemoryMap(target, out _));
		Assert.True(store.IsCurrent(target, replacement.Version));
	}

	[Fact]
	public void MemoryMap_ExpiresAndWrongTargetReadInvalidatesPreviousIdentity()
	{
		ManualTimeProvider time = new();
		PreparedInspectionStore store = new(time);
		ProcessSnapshot target = Target(42, 7);
		store.PublishMemoryMap(target, ImmutableArray<ModuleInfo>.Empty, ImmutableArray<MemoryRegionInfo>.Empty);
		Assert.True(store.TryGetMemoryMap(target, out _));
		time.Advance(PreparedInspectionStore.Lifetime);
		Assert.False(store.TryGetMemoryMap(target, out _));
		store.PublishMemoryMap(target, ImmutableArray<ModuleInfo>.Empty, ImmutableArray<MemoryRegionInfo>.Empty);
		Assert.False(store.TryGetMemoryMap(Target(43, 8), out _));
		Assert.False(store.TryGetModules(target, out _));
		Assert.False(store.TryGetMemoryMap(target, out _));
	}

	[Fact]
	public void Publish_RejectsDefaultImmutableArrays()
	{
		PreparedInspectionStore store = new(new ManualTimeProvider());
		ProcessSnapshot target = Target(42, 7);

		Assert.Throws<ArgumentException>(() => store.PublishModules(target, default));
		Assert.Throws<ArgumentException>(() => store.PublishMemoryMap(target, ImmutableArray<ModuleInfo>.Empty, default));
	}

	private static ProcessSnapshot Target(int processId, long epoch)
	{
		return new ProcessSnapshot(new TargetProcessId(processId), "game.exe", null, TargetBackend.LocalProcess,
			CheatEngineArchitecture.X64, PointerSize.Bit64, 8, DateTimeOffset.UnixEpoch, epoch);
	}
}
