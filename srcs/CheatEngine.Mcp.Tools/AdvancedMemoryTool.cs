using System.ComponentModel;

using CheatEngine.Client;
using CheatEngine.Client.Allocations;
using CheatEngine.Client.Results;
using CheatEngine.SDK.Engine.Values;

using ModelContextProtocol.Server;

namespace CheatEngine.Mcp.Tools;

[McpServerToolType]
public sealed class AdvancedMemoryTool : IDisposable
{
	private const long MaximumAllocationBytes = 16 * 1024 * 1024;
	private const long MaximumTotalAllocationBytes = 64 * 1024 * 1024;
	private const int MaximumAllocations = 128;
	private const int MaximumAllocationNameLength = 256;
	private readonly Dictionary<string, ITargetMemoryLease> _allocations = new(StringComparer.Ordinal);
	private readonly ICheatEngineClient _client;
	private readonly TargetResources? _targetResources;
	private bool _disposed;

	public AdvancedMemoryTool(ICheatEngineClient client, TargetResources? targetResources = null)
	{
		ArgumentNullException.ThrowIfNull(client);
		_client = client;
		_targetResources = targetResources;
	}

	public void Dispose()
	{
		if (_disposed)
		{
			return;
		}

		_disposed = true;
		foreach ((string name, ITargetMemoryLease lease) in _allocations.ToArray())
		{
			LeaseReleaseOutcome outcome = lease.Release();
			if (!outcome.IsRetryable)
			{
				_targetResources?.Forget(lease);
				_allocations.Remove(name);
			}
		}
	}

	[McpServerTool(Name = "allocate_memory")]
	[Description("Allocate target memory owned by a named Client lease.")]
	public object AllocateMemory([Description("Caller-owned allocation name.")] string name,
		[Description("Requested allocation size in bytes (1-16777216).")]
		long size,
		[Description("Optional preferred target address.")]
		string? preferredAddress = null)
	{
		return ToolExecution.Run(_client, () =>
		{
			if (string.IsNullOrWhiteSpace(name))
			{
				return ToolExecution.Error("name is required.");
			}

			if (name.Length > MaximumAllocationNameLength)
			{
				return ToolExecution.Error("name must not exceed 256 characters.");
			}

			if (size <= 0)
			{
				return ToolExecution.Error("size must be greater than zero.");
			}

			if (size > MaximumAllocationBytes)
			{
				return ToolExecution.Error("size must not exceed 16777216 bytes.");
			}

			if (_allocations.ContainsKey(name))
			{
				return ToolExecution.Error($"An allocation named '{name}' already exists.");
			}

			if (_allocations.Count >= MaximumAllocations)
			{
				return ToolExecution.Error("At most 128 allocations may be owned by this server.");
			}

			if (TotalAllocationBytes() > MaximumTotalAllocationBytes - size)
			{
				return ToolExecution.Error("The server allocation total must not exceed 67108864 bytes.");
			}

			Address? preferred = string.IsNullOrWhiteSpace(preferredAddress)
				? null
				: ToolExecution.Address(_client, preferredAddress);
			ITargetMemoryLease lease =
				_client.Allocations.Allocate(new AllocationRequest(size, preferredAddress: preferred));
			_allocations.Add(name, lease);
			_targetResources?.Track(lease, () => _allocations.Remove(name),
				new { kind = "allocation", name, address = $"0x{lease.Address.Value:X}", size = lease.Size });
			return new { success = true, name, address = $"0x{lease.Address.Value:X}", size = lease.Size };
		});
	}

	[McpServerTool(Name = "free_memory")]
	[Description("Release a named target-memory allocation lease.")]
	public object FreeMemory([Description("The allocation name returned by allocate_memory.")] string name)
	{
		return ToolExecution.Run(_client, () =>
		{
			if (!_allocations.TryGetValue(name, out ITargetMemoryLease? lease))
			{
				return ToolExecution.Error($"No allocation named '{name}' exists.");
			}

			LeaseReleaseOutcome release = lease.Release();
			if (!release.IsRetryable)
			{
				_targetResources?.Forget(lease);
				_allocations.Remove(name);
			}

			return new
			{
				success = release.IsComplete,
				name,
				address = $"0x{lease.Address.Value:X}",
				size = lease.Size,
				release,
				requiresManualRecovery = release.RequiresManualRecovery
			};
		});
	}

	private long TotalAllocationBytes()
	{
		return _allocations.Values.Sum(lease => lease.Size);
	}
}
