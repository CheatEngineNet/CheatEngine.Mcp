using System.ComponentModel;

using CheatEngine.Client;
using CheatEngine.Client.Allocations;
using CheatEngine.Mcp.Core.Contract;
using CheatEngine.Mcp.Core.Values;
using CheatEngine.SDK.Engine.Values;

using ModelContextProtocol.Server;

namespace CheatEngine.Mcp.Tools.Memory;

/// <summary>
///     Named target allocations owned by the activation: each is a Client lease tracked by <see cref="TargetResources" />,
///     so <c>runtime_release_resources</c> and the plugin's disable free it too.
/// </summary>
[McpServerToolType]
public sealed class MemoryAllocationTools
{
	private const string ResourceKind = "allocation";

	private readonly ToolDispatch _dispatch;
	private readonly AllocationRegistry _registry;
	private readonly TargetResources _resources;

	/// <summary>Creates the tools; the constructor does no Cheat Engine work.</summary>
	/// <param name="dispatch">The activation's dispatch facade.</param>
	/// <param name="resources">The activation's target resources.</param>
	/// <param name="registry">The activation's named allocations.</param>
	public MemoryAllocationTools(ToolDispatch dispatch, TargetResources resources, AllocationRegistry registry)
	{
		ArgumentNullException.ThrowIfNull(dispatch);
		ArgumentNullException.ThrowIfNull(resources);
		ArgumentNullException.ThrowIfNull(registry);
		_dispatch = dispatch;
		_resources = resources;
		_registry = registry;
	}

	/// <summary>Allocates named target memory owned by this activation.</summary>
	[McpServerTool(Name = CheatEngineToolNames.MemoryAllocate, Title = "Allocate memory", ReadOnly = false,
		Destructive = false, Idempotent = false, OpenWorld = false, UseStructuredContent = true)]
	[McpMeta(McpDispatchClass.MetaKey, McpDispatchClass.Short)]
	[Description(
		"Allocate target memory under a caller-chosen name, read and write by default or executable for code (up to 16 MiB each, 128 allocations and 64 MiB per activation). The allocation is an activation resource: free it with memory_free or runtime_release_resources before switching processes; disabling the plugin frees it too.")]
	public AllocationInfo Allocate(
		[Description("A unique name for the allocation, 1 to 256 characters; memory_free takes it back.")]
		string name,
		[Description("The number of bytes to allocate, 1 to 16777216; Cheat Engine may round it up to a page.")]
		long size,
		[Description("Whether the memory is executable (read, write and execute), for injected code.")]
		bool executable = false,
		[Description(
			"An address or expression near which Cheat Engine should allocate, for code that jumps with a 32-bit displacement; Cheat Engine may allocate elsewhere.")]
		string? near = null,
		CancellationToken cancellationToken = default)
	{
		string allocationName = RequireName(name);
		MemoryTargets.RequireRange(size, "size", 1, AllocationRegistry.MaximumAllocationBytes);
		string? nearExpression = near is null ? null : MemoryTargets.RequireExpression(near, "near");
		_registry.Reserve(allocationName, size);
		try
		{
			AllocationInfo info = _dispatch.Run(CheatEngineToolNames.MemoryAllocate, token =>
			{
				ICheatEngineClient client = _dispatch.Client;
				Address? preferred = nearExpression is null
					? null
					: MemoryTargets.Resolve(client, nearExpression, "near", token);
				ITargetMemoryLease lease = client.Allocations.Allocate(new AllocationRequest(size,
					executable ? AllocationProtection.ExecuteReadWrite : AllocationProtection.ReadWrite,
					preferred is { IsZero: false } ? preferred : null), token);
				string address = HexFormat.Address(lease.Address);
				ITargetResource resource = _resources.Track(lease, ResourceKind,
					() => _registry.Remove(allocationName, lease), allocationName, address, lease.Size);
				_registry.Commit(allocationName, lease, resource);
				return new AllocationInfo(allocationName, address, lease.Size, executable, resource.Descriptor.Id);
			}, cancellationToken);
			return info;
		}
		catch
		{
			_registry.Cancel(allocationName);
			throw;
		}
	}

	/// <summary>Frees a named allocation of this activation.</summary>
	[McpServerTool(Name = CheatEngineToolNames.MemoryFree, Title = "Free memory", ReadOnly = false,
		Destructive = true, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
	[McpMeta(McpDispatchClass.MetaKey, McpDispatchClass.Short)]
	[Description(
		"Free a named allocation made by memory_allocate. An unknown name is not_found. A release that did not complete fails with partial_effect: retryable when nothing was done yet (the name stays and memory_free can be repeated), otherwise the memory may remain in the target and needs manual recovery.")]
	public ReleaseResult Free(
		[Description("The allocation name given to memory_allocate.")]
		string name,
		CancellationToken cancellationToken = default)
	{
		string allocationName = RequireName(name);
		if (!_registry.TryGet(allocationName, out ITargetMemoryLease? lease, out ITargetResource? resource))
		{
			throw CheatEngineToolException.NotFound($"No allocation is named '{allocationName}'.",
				"List the activation's allocations with runtime_list_resources.");
		}

		ReleaseResult result = _dispatch.Run(CheatEngineToolNames.MemoryFree, token =>
		{
			ResourceReleaseOutcome outcome = resource.Release(token);
			if (!outcome.IsRetryable)
			{
				_resources.Forget(resource);
				_registry.Remove(allocationName, lease);
			}

			return new ReleaseResult(allocationName, HexFormat.Address(lease.Address), lease.Size, outcome);
		}, cancellationToken);
		if (result.Release.IsComplete)
		{
			return result;
		}

		throw CheatEngineToolException.PartialEffect(
			$"The allocation '{allocationName}' was not freed ({result.Release.Kind}).", result.Release.HostEffect,
			result, MemoryJsonContext.Default.ReleaseResult, result.Release.IsRetryable,
			result.Release.IsRetryable
				? "Repeat memory_free later; the allocation is still tracked."
				: "The memory may remain in the target; recover it manually, it is no longer tracked.");
	}

	private static string RequireName(string? name)
	{
		if (string.IsNullOrWhiteSpace(name))
		{
			throw CheatEngineToolException.InvalidArgument("name", "must be 1 to 256 characters.");
		}

		return name.Length <= AllocationRegistry.MaximumNameLength
			? name
			: throw CheatEngineToolException.LimitExceeded("name",
				$"must be at most {AllocationRegistry.MaximumNameLength} characters.");
	}
}
