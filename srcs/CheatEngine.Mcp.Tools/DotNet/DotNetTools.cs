using System.ComponentModel;

using CheatEngine.Mcp.Core.Contract;
using CheatEngine.Mcp.Core.Jobs;

using ModelContextProtocol.Server;

namespace CheatEngine.Mcp.Tools.DotNet;

/// <summary>Read-only tools over Cheat Engine's external .NET data collector.</summary>
[McpServerToolType]
public sealed class DotNetTools
{
	private const int MaximumPage = 1_000;
	private const int MaximumFields = 2_048;
	private const int MaximumInstances = 2_048;
	private const int MaximumAddressLength = 512;
	private readonly ToolDispatch _dispatch;
	private readonly JobRegistry _jobs;

	/// <summary>Creates the .NET tool container for one activation.</summary>
	public DotNetTools(ToolDispatch dispatch, JobRegistry jobs)
	{
		ArgumentNullException.ThrowIfNull(dispatch);
		ArgumentNullException.ThrowIfNull(jobs);
		_dispatch = dispatch;
		_jobs = jobs;
	}

	/// <summary>Reports whether Cheat Engine's external .NET collector can inspect the selected target.</summary>
	[McpServerTool(Name = CheatEngineToolNames.DotNetGetStatus, Title = "Get .NET collector status", ReadOnly = true,
		Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
	[McpMeta(McpDispatchClass.MetaKey, McpDispatchClass.Short)]
	[Description(
		"Report whether Cheat Engine exposes and can query its out-of-process .NET data collector. This tool never injects into the target.")]
	public DotNetStatus GetStatus(CancellationToken cancellationToken = default)
	{
		return _dispatch.RunLua(CheatEngineToolNames.DotNetGetStatus, DotNetLuaScripts.Status,
			DotNetJsonContext.Default.DotNetStatus, cancellationToken);
	}

	/// <summary>Lists application domains from the active collector.</summary>
	[McpServerTool(Name = CheatEngineToolNames.DotNetListDomains, Title = "List .NET application domains",
		ReadOnly = true,
		Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
	[McpMeta(McpDispatchClass.MetaKey, McpDispatchClass.Short)]
	[Description(
		"Page .NET application domains. Handles are opaque decimal strings scoped to the current collector session.")]
	public DotNetDomainPage ListDomains([Description("The zero-based first domain.")] int offset = 0,
		[Description("The most domains returned, 1 to 1000.")]
		int limit = 100,
		CancellationToken cancellationToken = default)
	{
		Page(offset, limit);
		return _dispatch.RunLua(CheatEngineToolNames.DotNetListDomains, DotNetLuaScripts.Domains,
			DotNetJsonContext.Default.DotNetDomainPage, cancellationToken, offset, limit);
	}

	/// <summary>Lists modules in one managed application domain.</summary>
	[McpServerTool(Name = CheatEngineToolNames.DotNetListModules, Title = "List .NET modules", ReadOnly = true,
		Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
	[McpMeta(McpDispatchClass.MetaKey, McpDispatchClass.Short)]
	[Description(
		"Page managed modules in a .NET application domain. Pass domainHandle back verbatim from dotnet_list_domains.")]
	public DotNetModulePage ListModules(
		[Description("An opaque domain handle from dotnet_list_domains.")] string domainHandle,
		[Description("The zero-based first module.")]
		int offset = 0,
		[Description("The most modules returned, 1 to 1000.")]
		int limit = 100,
		CancellationToken cancellationToken = default)
	{
		Page(offset, limit);
		return _dispatch.RunLua(CheatEngineToolNames.DotNetListModules, DotNetLuaScripts.Modules,
			DotNetJsonContext.Default.DotNetModulePage, cancellationToken, Handle(domainHandle, "domainHandle"), offset,
			limit);
	}

	/// <summary>Lists type definitions in one managed module.</summary>
	[McpServerTool(Name = CheatEngineToolNames.DotNetListTypes, Title = "List .NET types", ReadOnly = true,
		Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
	[McpMeta(McpDispatchClass.MetaKey, McpDispatchClass.HostScan)]
	[Description(
		"Page type definitions in one managed module. The collector enumerates the module before applying the page.")]
	public DotNetTypePage ListTypes(
		[Description("An opaque module handle from dotnet_list_modules.")] string moduleHandle,
		[Description("The zero-based first type.")]
		int offset = 0,
		[Description("The most types returned, 1 to 1000.")]
		int limit = 100,
		CancellationToken cancellationToken = default)
	{
		Page(offset, limit);
		return _dispatch.RunLua(CheatEngineToolNames.DotNetListTypes, DotNetLuaScripts.Types,
			DotNetJsonContext.Default.DotNetTypePage, cancellationToken, Handle(moduleHandle, "moduleHandle"), offset,
			limit);
	}

	/// <summary>Reads a type layout and its bounded field list.</summary>
	[McpServerTool(Name = CheatEngineToolNames.DotNetGetType, Title = "Get .NET type", ReadOnly = true,
		Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
	[McpMeta(McpDispatchClass.MetaKey, McpDispatchClass.Short)]
	[Description(
		"Read a managed type's layout and up to 2048 fields. Static addresses, when present, are target addresses rather than collector handles.")]
	public DotNetTypeDetails GetType(
		[Description("An opaque module handle from dotnet_list_modules.")] string moduleHandle,
		[Description("An opaque type token from dotnet_list_types.")]
		string typeToken,
		[Description("The maximum fields copied, 1 to 2048.")]
		int maximumFields = 512,
		CancellationToken cancellationToken = default)
	{
		Range(maximumFields, 1, MaximumFields, "maximumFields");
		return _dispatch.RunLua(CheatEngineToolNames.DotNetGetType, DotNetLuaScripts.Type,
			DotNetJsonContext.Default.DotNetTypeDetails, cancellationToken, Handle(moduleHandle, "moduleHandle"),
			Handle(typeToken, "typeToken"), maximumFields);
	}

	/// <summary>Lists methods in one managed type.</summary>
	[McpServerTool(Name = CheatEngineToolNames.DotNetListMethods, Title = "List .NET methods", ReadOnly = true,
		Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
	[McpMeta(McpDispatchClass.MetaKey, McpDispatchClass.Short)]
	[Description(
		"Page a managed type's methods. NativeCode is zero or absent until the runtime has JIT-compiled the method.")]
	public DotNetMethodPage ListMethods(
		[Description("An opaque module handle from dotnet_list_modules.")] string moduleHandle,
		[Description("An opaque type token from dotnet_list_types.")]
		string typeToken,
		[Description("The zero-based first method.")]
		int offset = 0,
		[Description("The most methods returned, 1 to 1000.")]
		int limit = 100,
		CancellationToken cancellationToken = default)
	{
		Page(offset, limit);
		return _dispatch.RunLua(CheatEngineToolNames.DotNetListMethods, DotNetLuaScripts.Methods,
			DotNetJsonContext.Default.DotNetMethodPage, cancellationToken, Handle(moduleHandle, "moduleHandle"),
			Handle(typeToken, "typeToken"), offset, limit);
	}

	/// <summary>Inspects a .NET object at an address.</summary>
	[McpServerTool(Name = CheatEngineToolNames.DotNetGetObject, Title = "Get .NET object", ReadOnly = true,
		Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
	[McpMeta(McpDispatchClass.MetaKey, McpDispatchClass.Short)]
	[Description(
		"Inspect a recognized .NET object at an address or Cheat Engine address expression, copying at most 2048 fields.")]
	public DotNetObject GetObject([Description("A target address or Cheat Engine address expression.")] string address,
		[Description("The maximum fields copied, 1 to 2048.")]
		int maximumFields = 512,
		CancellationToken cancellationToken = default)
	{
		string expression = Address(address, "address");
		Range(maximumFields, 1, MaximumFields, "maximumFields");
		return _dispatch.RunLua(CheatEngineToolNames.DotNetGetObject, DotNetLuaScripts.Object,
			DotNetJsonContext.Default.DotNetObject, cancellationToken, expression, maximumFields);
	}

	/// <summary>Starts one bounded .NET instance search as an activation-owned job.</summary>
	[McpServerTool(Name = CheatEngineToolNames.DotNetStartInstanceSearch, Title = "Start .NET instance search",
		ReadOnly = true,
		Destructive = false, Idempotent = false, OpenWorld = false, UseStructuredContent = true)]
	[McpMeta(McpDispatchClass.MetaKey, McpDispatchClass.HostScan)]
	[Description(
		"Start a bounded, type-filtered .NET instance search. The collector scan runs in one job dispatch and may take time; poll it and stop it with runtime_stop_job before switching targets.")]
	public DotNetInstanceSearch StartInstanceSearch(
		[Description("An opaque module handle from dotnet_list_modules.")]
		string moduleHandle,
		[Description("An opaque type token from dotnet_list_types.")]
		string typeToken,
		[Description("The maximum retained instance addresses, 1 to 2048.")]
		int maximumResults = 100,
		[Description("How long the completed result remains pollable; the configured job maximum applies.")]
		int? lifetimeSeconds = null,
		CancellationToken cancellationToken = default)
	{
		Range(maximumResults, 1, MaximumInstances, "maximumResults");
		string module = Handle(moduleHandle, "moduleHandle");
		string type = Handle(typeToken, "typeToken");
		TimeSpan lifetime = _jobs.ResolveTimeToLive(lifetimeSeconds);
		int buffer = _jobs.ResolveBufferLimit(maximumResults, "maximumResults");
		ManagedJob<DotNetInstance> job = _jobs.StartManaged<DotNetInstance>("dotnetinstances", lifetime, buffer,
			(writer, token) =>
			{
				DotNetInstanceBatch batch = _dispatch.RunLua(CheatEngineToolNames.DotNetStartInstanceSearch,
					DotNetLuaScripts.Instances, DotNetJsonContext.Default.DotNetInstanceBatch, token,
					module, type, buffer);
				writer.Progress(0, batch.Total);
				foreach (DotNetInstance instance in batch.Instances)
				{
					token.ThrowIfCancellationRequested();
					writer.Add(instance);
				}

				writer.Progress(batch.Instances.Length, batch.Total);
				return Task.CompletedTask;
			});
		return new DotNetInstanceSearch(job.Id, module, type, buffer);
	}

	/// <summary>Returns a non-consuming page from a .NET instance search job.</summary>
	[McpServerTool(Name = CheatEngineToolNames.DotNetPollInstanceSearch, Title = "Poll .NET instance search",
		ReadOnly = true,
		Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
	[McpMeta(McpDispatchClass.MetaKey, McpDispatchClass.Short)]
	[Description(
		"Read a non-consuming page of a .NET instance search. Start with afterSequence 0, then pass nextAfterSequence; runtime_stop_job discards buffered results.")]
	public DotNetInstanceSearchPage PollInstanceSearch(
		[Description("The job id returned by dotnet_start_instance_search.")] string jobId,
		[Description("The previous nextAfterSequence, or 0 for the first page.")]
		long afterSequence = 0,
		[Description("The maximum addresses returned, 1 to the configured job limit.")]
		int limit = 100)
	{
		ManagedJob<DotNetInstance> job = _jobs.Get<ManagedJob<DotNetInstance>>(jobId, "dotnetinstances");
		JobPoll<DotNetInstance> poll = job.Poll(afterSequence, limit);
		return new DotNetInstanceSearchPage(poll.Job, [.. poll.Items], poll.FirstSequence, poll.NextAfterSequence,
			poll.More, poll.Dropped);
	}

	private static string Handle(string value, string parameter)
	{
		if (string.IsNullOrWhiteSpace(value) || value.Length > 64)
		{
			throw CheatEngineToolException.InvalidArgument(parameter, "must be a non-empty collector handle.");
		}

		return value.Trim();
	}

	private static string Address(string value, string parameter)
	{
		if (string.IsNullOrWhiteSpace(value))
		{
			throw CheatEngineToolException.InvalidArgument(parameter,
				"must be a non-empty Cheat Engine address expression.");
		}

		string expression = value.Trim();
		if (expression.Length > MaximumAddressLength)
		{
			throw CheatEngineToolException.LimitExceeded(parameter,
				$"must be at most {MaximumAddressLength} characters.");
		}

		return expression;
	}

	private static void Page(int offset, int limit)
	{
		if (offset < 0)
		{
			throw CheatEngineToolException.InvalidArgument("offset", "must be zero or greater.");
		}

		Range(limit, 1, MaximumPage, "limit");
	}

	private static void Range(int value, int minimum, int maximum, string parameter)
	{
		if (value < minimum || value > maximum)
		{
			throw CheatEngineToolException.LimitExceeded(parameter, $"must be between {minimum} and {maximum}.");
		}
	}
}
