using System.Collections.Immutable;

using CheatEngine.Client.Processes;
using CheatEngine.Mcp.Core.Contract;
using CheatEngine.SDK.Engine.Inspection;

namespace CheatEngine.Mcp.Core.Inspection;

/// <summary>Identifies an immutable prepared inspection snapshot.</summary>
public readonly record struct PreparedInspectionVersion(int ProcessId, long SelectionEpoch, long Revision);

/// <summary>Prepared modules for one process-selection version.</summary>
public sealed record PreparedModuleSnapshot(PreparedInspectionVersion Version, ImmutableArray<ModuleInfo> Modules);

/// <summary>Prepared modules and memory regions for one process-selection version.</summary>
public sealed record PreparedMemoryMapSnapshot(PreparedInspectionVersion Version, ImmutableArray<ModuleInfo> Modules,
	ImmutableArray<MemoryRegionInfo> Regions);

/// <summary>Activation-scoped immutable inspection snapshots prepared by explicit source tools.</summary>
public sealed class PreparedInspectionStore
{
	/// <summary>The maximum age of prepared data; the exact expiry boundary is stale.</summary>
	public static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(5);

	private readonly Lock _gate = new();
	private readonly TimeProvider _time;
	private PreparedModuleSnapshot? _modules;
	private PreparedMemoryMapSnapshot? _memoryMap;
	private long _publishedAt;
	private long _revision;
	private long _observedEpoch = -1;
	private int _observedProcessId;

	/// <summary>Creates an empty activation store using a monotonic clock for freshness.</summary>
	public PreparedInspectionStore(TimeProvider time)
	{
		ArgumentNullException.ThrowIfNull(time);
		_time = time;
	}

	/// <summary>Publishes a complete module list, invalidating earlier versions and memory-map preparation.</summary>
	public PreparedModuleSnapshot PublishModules(ProcessSnapshot target, ImmutableArray<ModuleInfo> modules)
	{
		RequireModules(modules);
		lock (_gate)
		{
			if (!Observe(target))
			{
				throw TargetChanged();
			}

			PreparedInspectionVersion version = NextVersion(target);
			PreparedModuleSnapshot snapshot = new(version, modules);
			_modules = snapshot;
			_memoryMap = null;
			_publishedAt = _time.GetTimestamp();
			return snapshot;
		}
	}

	/// <summary>Atomically publishes a complete region map and its module ownership list under one new version.</summary>
	public PreparedMemoryMapSnapshot PublishMemoryMap(ProcessSnapshot target, ImmutableArray<ModuleInfo> modules,
		ImmutableArray<MemoryRegionInfo> regions)
	{
		RequireModules(modules);
		if (regions.IsDefault)
		{
			throw new ArgumentException("Memory regions must not use the default immutable array.", nameof(regions));
		}

		lock (_gate)
		{
			if (!Observe(target))
			{
				throw TargetChanged();
			}

			PreparedInspectionVersion version = NextVersion(target);
			PreparedMemoryMapSnapshot map = new(version, modules, regions);
			_modules = new PreparedModuleSnapshot(version, modules);
			_memoryMap = map;
			_publishedAt = _time.GetTimestamp();
			return map;
		}
	}

	/// <summary>Observes the current target and returns only its fresh prepared module list.</summary>
	public bool TryGetModules(ProcessSnapshot target, out PreparedModuleSnapshot snapshot)
	{
		lock (_gate)
		{
			if (Observe(target) && _modules is { } current && Matches(target, current.Version) && IsFresh())
			{
				snapshot = current;
				return true;
			}
		}

		snapshot = null!;
		return false;
	}

	/// <summary>Observes the current target and returns only its fresh prepared memory map.</summary>
	public bool TryGetMemoryMap(ProcessSnapshot target, out PreparedMemoryMapSnapshot snapshot)
	{
		lock (_gate)
		{
			if (Observe(target) && _memoryMap is { } current && Matches(target, current.Version) && IsFresh())
			{
				snapshot = current;
				return true;
			}
		}

		snapshot = null!;
		return false;
	}

	/// <summary>Checks target identity, latest publication and expiry for a prepared version.</summary>
	public bool IsCurrent(ProcessSnapshot target, PreparedInspectionVersion version)
	{
		lock (_gate)
		{
			return Observe(target) && _modules is { } current && current.Version == version && Matches(target, version)
				&& IsFresh();
		}
	}

	private PreparedInspectionVersion NextVersion(ProcessSnapshot target)
	{
		return new PreparedInspectionVersion(target.Id.Value, target.SelectionEpoch, checked(++_revision));
	}

	private bool IsFresh()
	{
		return _time.GetElapsedTime(_publishedAt, _time.GetTimestamp()) < Lifetime;
	}

	private bool Observe(ProcessSnapshot target)
	{
		if (target.SelectionEpoch < _observedEpoch
			|| (target.SelectionEpoch == _observedEpoch && target.Id.Value != _observedProcessId))
		{
			return false;
		}

		if (target.SelectionEpoch > _observedEpoch)
		{
			_observedEpoch = target.SelectionEpoch;
			_observedProcessId = target.Id.Value;
			_modules = null;
			_memoryMap = null;
		}

		return true;
	}

	private static bool Matches(ProcessSnapshot target, PreparedInspectionVersion version)
	{
		return target.Id.Value == version.ProcessId && target.SelectionEpoch == version.SelectionEpoch;
	}

	private static void RequireModules(ImmutableArray<ModuleInfo> modules)
	{
		if (modules.IsDefault)
		{
			throw new ArgumentException("Modules must not use the default immutable array.", nameof(modules));
		}
	}

	private static CheatEngineToolException TargetChanged()
	{
		return new CheatEngineToolException(new ToolError(ToolErrorKind.TargetChanged,
			"The prepared inspection publication belongs to an older or different selected target.", null,
			ToolHostEffect.NotStarted, false, "Read the current target again before publishing inspection data."));
	}
}
