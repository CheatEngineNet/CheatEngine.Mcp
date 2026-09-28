using System.ComponentModel;
using System.Globalization;

using CheatEngine.Mcp.Core.Contract;
using CheatEngine.Mcp.Core.Features;
using CheatEngine.Mcp.Core.Jobs;

using ModelContextProtocol.Server;

namespace CheatEngine.Mcp.Tools.Mono;

/// <summary>
///     Tools over Cheat Engine's Mono data collector. Attach, invoke and JIT compilation require target-code
///     execution.
/// </summary>
[McpServerToolType]
public sealed class MonoTools
{
	private const int MaximumPage = 1_000;
	private const int MaximumFields = 2_048;
	private const int MaximumInstances = 2_048;
	private const int MaximumNameLength = 512;
	private const int MaximumFilterLength = 256;
	private const int MaximumAddressLength = 512;
	private const int MaximumBacktrack = 65_536;
	private const int MaximumArgumentValueLength = 4_096;
	private const int MaximumArguments = 64;
	private const int VtByte = 0;
	private const int VtWord = 1;
	private const int VtDword = 2;
	private const int VtQword = 3;
	private const int VtSingle = 4;
	private const int VtDouble = 5;
	private const int VtString = 6;
	private const int VtPointer = 12;
	private readonly ToolDispatch _dispatch;
	private readonly JobRegistry _jobs;
	private readonly TargetResources _resources;
	private MonoAttachment? _attachment;

	/// <summary>Creates the Mono tools for one plugin activation.</summary>
	public MonoTools(ToolDispatch dispatch, JobRegistry jobs, TargetResources resources)
	{
		ArgumentNullException.ThrowIfNull(dispatch);
		ArgumentNullException.ThrowIfNull(jobs);
		ArgumentNullException.ThrowIfNull(resources);
		_dispatch = dispatch;
		_jobs = jobs;
		_resources = resources;
	}

	/// <summary>Injects and attaches Cheat Engine's Mono collector.</summary>
	[McpServerTool(Name = CheatEngineToolNames.MonoAttach, Title = "Attach Mono collector", ReadOnly = false,
		Destructive = true, Idempotent = false, OpenWorld = false, UseStructuredContent = true)]
	[McpMeta(McpDispatchClass.MetaKey, McpDispatchClass.MayPrompt)]
	[RequiresFeature(McpFeature.TargetCodeExecution)]
	[Description(
		"Inject Cheat Engine's Mono data collector into the selected process. It can patch mono_error_ok, install Cheat Engine hooks, set the table Uses Mono option (Cheat Engine then injects the collector again on each process open) and show a native dialog; it may block for several seconds. It is refused while the target is paused or the debugger is stopped.")]
	public MonoAttachResult Attach(CancellationToken cancellationToken = default)
	{
		if (_attachment is { HoldsHostState: true })
		{
			throw CheatEngineToolException.Busy("This activation already owns a Mono collector attachment.",
				"Detach it with mono_detach or release it with runtime_release_resources before attaching again.");
		}

		// The Lua body records the attachment under this id, so a later detach can tell it from one Cheat Engine made.
		string id = _resources.NextId("mono");
		MonoAttachResult result = _dispatch.RunLua(CheatEngineToolNames.MonoAttach, MonoLuaScripts.Attach,
			MonoJsonContext.Default.MonoAttachResult, cancellationToken, id);
		MonoAttachment attachment = new(id, _dispatch);
		_resources.Track(attachment, () => _attachment = null);
		_attachment = attachment;
		return result;
	}

	/// <summary>Detaches the Mono collector connection and releases this activation's attachment resource.</summary>
	[McpServerTool(Name = CheatEngineToolNames.MonoDetach, Title = "Detach Mono collector", ReadOnly = false,
		Destructive = true, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
	[McpMeta(McpDispatchClass.MetaKey, McpDispatchClass.Short)]
	[Description(
		"Close the Mono collector attachment this plugin activation made with mono_attach the way Cheat Engine's own detach does: send the collector its terminate command, then run libmono.terminate, which removes the lookup and dissect hooks. The collector DLL, the mono_error_ok patch and the table Uses Mono option remain. An attachment Cheat Engine or the user made is never closed: when this activation's attachment already ended (a detach, re-activation or target switch in Cheat Engine), only its MCP resource is released and alreadyEnded is true.")]
	public MonoDetachResult Detach(CancellationToken cancellationToken = default)
	{
		MonoAttachment? attachment = _attachment;
		if (attachment is null || attachment.IsEnded)
		{
			throw CheatEngineToolException.NotFound("This plugin activation does not own a Mono collector attachment.",
				"Only mono_attach creates an attachment that mono_detach can release.");
		}

		ResourceReleaseOutcome outcome =
			_dispatch.Run(CheatEngineToolNames.MonoDetach, attachment.Release, cancellationToken);
		if (!outcome.IsComplete)
		{
			throw new CheatEngineToolException(new ToolError(ToolErrorKind.PartialEffect,
				"The Mono collector detach did not complete.", CheatEngineToolNames.MonoDetach, outcome.HostEffect,
				outcome.IsRetryable,
				"Use runtime_release_resources after the collector is healthy, or restart the target."));
		}

		_resources.Forget(attachment);
		_attachment = null;
		return new MonoDetachResult(true,
		[
			"collector_dll_remains_loaded", "mono_error_ok_patch_remains_until_target_restart",
			"uses_mono_table_option_remains"
		], outcome.Kind == ResourceReleaseKind.ExternallyRemoved);
	}

	/// <summary>Reports the current Mono collector state.</summary>
	[McpServerTool(Name = CheatEngineToolNames.MonoGetStatus, Title = "Get Mono collector status", ReadOnly = true,
		Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
	[McpMeta(McpDispatchClass.MetaKey, McpDispatchClass.Short)]
	[Description(
		"Report whether Cheat Engine's Mono collector is attached to the selected process (by anyone), whether it reports its limited IL2CPP mode, and the Mono domain handles.")]
	public MonoStatus GetStatus(CancellationToken cancellationToken = default)
	{
		return _dispatch.RunLua(CheatEngineToolNames.MonoGetStatus, MonoLuaScripts.Status,
			MonoJsonContext.Default.MonoStatus, cancellationToken);
	}

	/// <summary>Lists Mono assemblies and images.</summary>
	[McpServerTool(Name = CheatEngineToolNames.MonoListAssemblies, Title = "List Mono assemblies", ReadOnly = true,
		Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
	[McpMeta(McpDispatchClass.MetaKey, McpDispatchClass.Short)]
	[Description(
		"Page assemblies and their Mono images. Every handle is opaque and valid only while the collector remains attached.")]
	public MonoAssemblyPage ListAssemblies([Description("The zero-based first assembly.")] int offset = 0,
		[Description("The most assemblies returned, 1 to 1000.")]
		int limit = 100,
		CancellationToken cancellationToken = default)
	{
		Page(offset, limit);
		return _dispatch.RunLua(CheatEngineToolNames.MonoListAssemblies, MonoLuaScripts.Assemblies,
			MonoJsonContext.Default.MonoAssemblyPage, cancellationToken, offset, limit);
	}

	/// <summary>Lists classes in one Mono image.</summary>
	[McpServerTool(Name = CheatEngineToolNames.MonoListClasses, Title = "List Mono classes", ReadOnly = true,
		Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
	[McpMeta(McpDispatchClass.MetaKey, McpDispatchClass.HostScan)]
	[Description(
		"Page classes in a Mono image, optionally only those whose namespace-qualified name contains nameContains. Cheat Engine enumerates the whole image before the filter and the page apply, which can take time for large assemblies.")]
	public MonoClassPage ListClasses(
		[Description("An opaque image handle from mono_list_assemblies.")]
		string imageHandle,
		[Description("The zero-based first class.")]
		int offset = 0,
		[Description("The most classes returned, 1 to 1000.")]
		int limit = 100,
		[Description(
			"Keep only the classes whose namespace-qualified name (Namespace.Name) contains this text, ignoring the case of ASCII letters, such as Player or Game.Player; at most 256 characters. Omit it for every class.")]
		string? nameContains = null,
		CancellationToken cancellationToken = default)
	{
		Page(offset, limit);
		string image = Handle(imageHandle, "imageHandle");
		return _dispatch.RunLua(CheatEngineToolNames.MonoListClasses, MonoLuaScripts.Classes,
			MonoJsonContext.Default.MonoClassPage, cancellationToken, image, offset, limit, NameFilter(nameContains));
	}

	/// <summary>Finds one Mono class by namespace and name.</summary>
	[McpServerTool(Name = CheatEngineToolNames.MonoFindClass, Title = "Find Mono class", ReadOnly = true,
		Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
	[McpMeta(McpDispatchClass.MetaKey, McpDispatchClass.HostScan)]
	[Description("Find a Mono class across the active collector images by exact namespace and class name.")]
	public MonoClass FindClass([Description("The namespace; empty searches the global namespace.")] string? @namespace,
		[Description("The exact class name.")] string className, CancellationToken cancellationToken = default)
	{
		string name = Name(className, "className", true);
		return _dispatch.RunLua(CheatEngineToolNames.MonoFindClass, MonoLuaScripts.FindClass,
			MonoJsonContext.Default.MonoClass, cancellationToken,
			@namespace is null ? null : Name(@namespace, "namespace", false), name);
	}

	/// <summary>Lists the fields of a Mono class.</summary>
	[McpServerTool(Name = CheatEngineToolNames.MonoListFields, Title = "List Mono fields", ReadOnly = true,
		Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
	[McpMeta(McpDispatchClass.MetaKey, McpDispatchClass.Short)]
	[Description(
		"List up to 2048 fields of a Mono class, optionally including inherited fields and only those whose name contains nameContains. Instance offsets count from the object start; static offsets count from the class static data, and staticAddress gives the address when Cheat Engine reports it.")]
	public MonoFieldList ListFields([Description("An opaque Mono class handle.")] string classHandle,
		[Description(
			"Include inherited fields; Cheat Engine's per-class field cache is bypassed so the flag always applies.")]
		bool includeParents = false,
		[Description("The maximum fields copied, 1 to 2048.")]
		int maximumFields = 512,
		[Description(
			"Keep only the fields whose name contains this text, ignoring the case of ASCII letters, such as health (which also matches <Health>k__BackingField); at most 256 characters. Omit it for every field.")]
		string? nameContains = null,
		CancellationToken cancellationToken = default)
	{
		Range(maximumFields, 1, MaximumFields, "maximumFields");
		string @class = Handle(classHandle, "classHandle");
		return _dispatch.RunLua(CheatEngineToolNames.MonoListFields, MonoLuaScripts.Fields,
			MonoJsonContext.Default.MonoFieldList, cancellationToken, @class, includeParents, maximumFields,
			NameFilter(nameContains));
	}

	/// <summary>Lists methods in a Mono class.</summary>
	[McpServerTool(Name = CheatEngineToolNames.MonoListMethods, Title = "List Mono methods", ReadOnly = true,
		Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
	[McpMeta(McpDispatchClass.MetaKey, McpDispatchClass.Short)]
	[Description(
		"Page methods in a Mono class with their parameter types and names, return type and static flag, optionally only those whose name contains nameContains. Method handles are opaque and can be JIT-compiled with mono_compile_method.")]
	public MonoMethodPage ListMethods([Description("An opaque Mono class handle.")] string classHandle,
		[Description("The zero-based first method.")]
		int offset = 0,
		[Description("The most methods returned, 1 to 1000.")]
		int limit = 100,
		[Description(
			"Keep only the methods whose name contains this text, ignoring the case of ASCII letters, such as Damage; at most 256 characters. Omit it for every method.")]
		string? nameContains = null,
		CancellationToken cancellationToken = default)
	{
		Page(offset, limit);
		string @class = Handle(classHandle, "classHandle");
		return _dispatch.RunLua(CheatEngineToolNames.MonoListMethods, MonoLuaScripts.Methods,
			MonoJsonContext.Default.MonoMethodPage, cancellationToken, @class, offset, limit, NameFilter(nameContains));
	}

	/// <summary>Finds one method by exact name in a Mono class.</summary>
	[McpServerTool(Name = CheatEngineToolNames.MonoFindMethod, Title = "Find Mono method", ReadOnly = true,
		Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
	[McpMeta(McpDispatchClass.MetaKey, McpDispatchClass.Short)]
	[Description(
		"Find the first Mono method with an exact name in a class. Inspect mono_list_methods first to resolve overloads.")]
	public MonoMethod FindMethod([Description("An opaque Mono class handle.")] string classHandle,
		[Description("The exact method name.")]
		string methodName, CancellationToken cancellationToken = default)
	{
		string name = Name(methodName, "methodName", true);
		return _dispatch.RunLua(CheatEngineToolNames.MonoFindMethod, MonoLuaScripts.FindMethod,
			MonoJsonContext.Default.MonoMethod, cancellationToken, Handle(classHandle, "classHandle"), name);
	}

	/// <summary>Gets the static data address of a Mono class.</summary>
	[McpServerTool(Name = CheatEngineToolNames.MonoGetStaticFieldAddress, Title = "Get Mono static field address",
		ReadOnly = true,
		Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
	[McpMeta(McpDispatchClass.MetaKey, McpDispatchClass.Short)]
	[Description(
		"Read a Mono class's static field-data base address. Add an individual static field's offset from mono_list_fields to reach that field.")]
	public MonoStaticFieldAddress GetStaticFieldAddress(
		[Description("An opaque Mono class handle.")]
		string classHandle,
		[Description(
			"An optional opaque Mono domain handle from mono_get_status; the first domain is used when omitted.")]
		string? domainHandle = null,
		CancellationToken cancellationToken = default)
	{
		return _dispatch.RunLua(CheatEngineToolNames.MonoGetStaticFieldAddress, MonoLuaScripts.StaticFieldAddress,
			MonoJsonContext.Default.MonoStaticFieldAddress, cancellationToken, Handle(classHandle, "classHandle"),
			domainHandle is null ? null : Handle(domainHandle, "domainHandle"));
	}

	/// <summary>JIT-compiles a Mono method to obtain native code.</summary>
	[McpServerTool(Name = CheatEngineToolNames.MonoCompileMethod, Title = "Compile Mono method", ReadOnly = false,
		Destructive = true, Idempotent = false, OpenWorld = false, UseStructuredContent = true)]
	[McpMeta(McpDispatchClass.MetaKey, McpDispatchClass.BlockingNative)]
	[RequiresFeature(McpFeature.TargetCodeExecution)]
	[Description(
		"Ask the target Mono runtime to JIT-compile a method and return its native address. The address changes each run; this can execute runtime compilation work in the target.")]
	public MonoCompiledMethod CompileMethod([Description("An opaque Mono method handle.")] string methodHandle,
		CancellationToken cancellationToken = default)
	{
		return _dispatch.RunLua(CheatEngineToolNames.MonoCompileMethod, MonoLuaScripts.CompileMethod,
			MonoJsonContext.Default.MonoCompiledMethod, cancellationToken, Handle(methodHandle, "methodHandle"));
	}

	/// <summary>Invokes one Mono method once with typed Cheat Engine arguments.</summary>
	[McpServerTool(Name = CheatEngineToolNames.MonoInvokeMethod, Title = "Invoke Mono method", ReadOnly = false,
		Destructive = true, Idempotent = false, OpenWorld = false, UseStructuredContent = true)]
	[McpMeta(McpDispatchClass.MetaKey, McpDispatchClass.BlockingNative)]
	[RequiresFeature(McpFeature.TargetCodeExecution)]
	[Description(
		"Invoke a Mono method once. It executes target code and can change game state or crash the target; a managed exception is returned in the result after the call ran. Give each argument the Cheat Engine type code of its parameter: 0 byte or bool, 1 int16, 2 int32, 3 int64, 4 float, 5 double, 6 string, 12 object or pointer address. Missing trailing arguments are sent as null. Never retry an uncertain result.")]
	public MonoMethodInvocation InvokeMethod([Description("An opaque Mono method handle.")] string methodHandle,
		[Description("The instance target address for an instance method; omit for a static method.")]
		string? instanceAddress = null,
		[Description(
			"Up to 64 arguments in parameter order, each a Cheat Engine type code and a textual value; for example type 2 with value 7 for an int, or type 6 with the text for a string.")]
		MonoInvokeArgument[]? arguments = null,
		CancellationToken cancellationToken = default)
	{
		MonoInvokeArgument[] supplied = arguments ?? [];
		if (supplied.Length > MaximumArguments)
		{
			throw CheatEngineToolException.LimitExceeded("arguments", $"accepts at most {MaximumArguments} arguments.");
		}

		foreach (MonoInvokeArgument argument in supplied)
		{
			if (argument is null || argument.Value is null)
			{
				throw CheatEngineToolException.InvalidArgument("arguments", "must not contain null entries or values.");
			}

			if (argument.Value.Length > MaximumArgumentValueLength)
			{
				throw CheatEngineToolException.LimitExceeded("arguments",
					$"each argument value must contain at most {MaximumArgumentValueLength} characters.");
			}
		}

		object?[][] values = [.. supplied.Select(static (argument, index) => Argument(argument, index))];
		return _dispatch.RunLua(CheatEngineToolNames.MonoInvokeMethod, MonoLuaScripts.InvokeMethod,
			MonoJsonContext.Default.MonoMethodInvocation, cancellationToken, Handle(methodHandle, "methodHandle"),
			instanceAddress is null ? null : Handle(instanceAddress, "instanceAddress"), values);
	}

	/// <summary>Identifies the Mono or IL2CPP object that contains an address and reads its fields.</summary>
	[McpServerTool(Name = CheatEngineToolNames.MonoGetObject, Title = "Get Mono object", ReadOnly = true,
		Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
	[McpMeta(McpDispatchClass.MetaKey, McpDispatchClass.HostScan)]
	[Description(
		"Identify the Mono or IL2CPP object that contains an address, such as a value found by a scan, and read its fields with their current values. The object start is searched from the address down to maxBacktrack bytes below it by reading target memory only: the nearest header must lead to a class the collector lists in that class's own image (through the vtable on Mono, directly on IL2CPP), so the collector is never asked about an unverified address, which can crash the target. Objects of generic instance and array classes are refused as unsupported, and so are objects whose image an IL2CPP collector cannot list classes for (Cheat Engine's memory-scan guess of that list is not used). Fields include inherited and static ones, with values read like dotnet_get_object. Enumerating the classes of the object's image can block Cheat Engine for seconds on a large assembly.")]
	public MonoObject GetObject(
		[Description(
			"A target address or Cheat Engine address expression at or inside the object, such as a scan result.")]
		string address,
		[Description(
			"The most bytes searched below address for the object start, 0 to 65536; 0 accepts only a pointer-aligned address that is the object start.")]
		int maxBacktrack = 4096,
		[Description("The maximum fields copied, 1 to 2048.")]
		int maximumFields = 512,
		CancellationToken cancellationToken = default)
	{
		string expression = Address(address);
		Range(maxBacktrack, 0, MaximumBacktrack, "maxBacktrack");
		Range(maximumFields, 1, MaximumFields, "maximumFields");
		return _dispatch.RunLua(CheatEngineToolNames.MonoGetObject, MonoLuaScripts.Object,
			MonoJsonContext.Default.MonoObject, cancellationToken, expression, maxBacktrack, maximumFields);
	}

	/// <summary>Starts a bounded Mono instance lookup as an activation-owned job.</summary>
	[McpServerTool(Name = CheatEngineToolNames.MonoStartInstanceSearch, Title = "Start Mono instance search",
		ReadOnly = true,
		Destructive = false, Idempotent = false, OpenWorld = false, UseStructuredContent = true)]
	[McpMeta(McpDispatchClass.MetaKey, McpDispatchClass.HostScan)]
	[Description(
		"Start a bounded lookup of possible instances of a Mono class: one job dispatch scans the target's memory for the class vtable and can block Cheat Engine for seconds. Unlike Cheat Engine's own Unity lookup it never runs managed code; the results are candidates to verify. Poll it, and stop it with runtime_stop_job before switching targets.")]
	public MonoInstanceSearch StartInstanceSearch([Description("An opaque Mono class handle.")] string classHandle,
		[Description("The maximum retained instance addresses, 1 to 2048.")]
		int maximumResults = 100,
		[Description("How long the completed result remains pollable; the configured job maximum applies.")]
		int? lifetimeSeconds = null,
		CancellationToken cancellationToken = default)
	{
		Range(maximumResults, 1, MaximumInstances, "maximumResults");
		string @class = Handle(classHandle, "classHandle");
		TimeSpan lifetime = _jobs.ResolveTimeToLive(lifetimeSeconds);
		int buffer = _jobs.ResolveBufferLimit(maximumResults, "maximumResults");
		ManagedJob<MonoInstance> job = _jobs.StartManaged<MonoInstance>("monoinstances", lifetime, buffer,
			(writer, token) =>
			{
				MonoInstanceBatch batch = _dispatch.RunLua(CheatEngineToolNames.MonoStartInstanceSearch,
					MonoLuaScripts.Instances, MonoJsonContext.Default.MonoInstanceBatch, token, @class, buffer);
				writer.Progress(0, batch.Total);
				foreach (MonoInstance instance in batch.Instances)
				{
					token.ThrowIfCancellationRequested();
					writer.Add(instance);
				}

				writer.Progress(batch.Instances.Length, batch.Total);
				return Task.CompletedTask;
			});
		return new MonoInstanceSearch(job.Id, @class, buffer);
	}

	/// <summary>Returns a non-consuming page from a Mono instance search job.</summary>
	[McpServerTool(Name = CheatEngineToolNames.MonoPollInstanceSearch, Title = "Poll Mono instance search",
		ReadOnly = true,
		Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
	[McpMeta(McpDispatchClass.MetaKey, McpDispatchClass.Short)]
	[Description(
		"Read a non-consuming page of a Mono instance search. Start with afterSequence 0, then use nextAfterSequence; runtime_stop_job discards buffered results.")]
	public MonoInstanceSearchPage PollInstanceSearch(
		[Description("The job id returned by mono_start_instance_search.")]
		string jobId,
		[Description("The previous nextAfterSequence, or 0 for the first page.")]
		long afterSequence = 0,
		[Description("The maximum addresses returned, 1 to 1000.")]
		int limit = 100)
	{
		ManagedJob<MonoInstance> job = _jobs.Get<ManagedJob<MonoInstance>>(jobId, "monoinstances");
		JobPoll<MonoInstance> poll = job.Poll(afterSequence, limit);
		return new MonoInstanceSearchPage(poll.Job, [.. poll.Items], poll.FirstSequence, poll.NextAfterSequence,
			poll.More, poll.Dropped);
	}

	private static string Handle(string value, string parameter)
	{
		if (string.IsNullOrWhiteSpace(value) || value.Length > 64)
		{
			throw CheatEngineToolException.InvalidArgument(parameter, "must be a non-empty Mono handle or address.");
		}

		return value.Trim();
	}

	private static string Address(string value)
	{
		if (string.IsNullOrWhiteSpace(value))
		{
			throw CheatEngineToolException.InvalidArgument("address",
				"must be a non-empty Cheat Engine address expression.");
		}

		string expression = value.Trim();
		if (expression.Length > MaximumAddressLength)
		{
			throw CheatEngineToolException.LimitExceeded("address",
				$"must be at most {MaximumAddressLength} characters.");
		}

		return expression;
	}

	/// <summary>
	///     Validates an optional <c>nameContains</c> filter: <see langword="null" /> or empty applies none, and the
	///     text is passed unchanged for the fixed Lua to match literally.
	/// </summary>
	private static string? NameFilter(string? value)
	{
		if (string.IsNullOrEmpty(value))
		{
			return null;
		}

		if (value.Length > MaximumFilterLength)
		{
			throw CheatEngineToolException.InvalidArgument("nameContains",
				$"must be at most {MaximumFilterLength} characters.");
		}

		return value;
	}

	private static object?[] Argument(MonoInvokeArgument argument, int index)
	{
		string value = argument.Value;
		object converted = argument.Type switch
		{
			VtByte => Flag(value) ?? Integer(value, sbyte.MinValue, byte.MaxValue, argument.Type, index),
			VtWord => Integer(value, short.MinValue, ushort.MaxValue, argument.Type, index),
			VtDword => Integer(value, int.MinValue, uint.MaxValue, argument.Type, index),
			VtQword => Quadword(value, index),
			VtSingle => Real(value, float.MaxValue, argument.Type, index),
			VtDouble => Real(value, double.MaxValue, argument.Type, index),
			VtString => value,
			VtPointer => Expression(value, index),
			_ => throw CheatEngineToolException.InvalidArgument("arguments",
				$"entry {index} has the type code {argument.Type}; use 0 (byte or bool), 1 (int16), 2 (int32), 3 (int64), 4 (float), 5 (double), 6 (string) or 12 (object or pointer address).")
		};
		return [argument.Type, converted];
	}

	private static long? Flag(string value)
	{
		string text = value.Trim();
		if (text.Equals("true", StringComparison.OrdinalIgnoreCase) ||
			text.Equals("yes", StringComparison.OrdinalIgnoreCase))
		{
			return 1;
		}

		return text.Equals("false", StringComparison.OrdinalIgnoreCase) ||
			   text.Equals("no", StringComparison.OrdinalIgnoreCase)
			? 0
			: null;
	}

	private static long Integer(string value, long minimum, long maximum, int type, int index)
	{
		if (!TryParseInteger(value, out Int128 parsed) || parsed < minimum || parsed > maximum)
		{
			throw CheatEngineToolException.InvalidArgument("arguments",
				$"entry {index} must be a decimal or 0x integer from {minimum} to {maximum} for type {type}.");
		}

		return (long) parsed;
	}

	private static ulong Quadword(string value, int index)
	{
		if (!TryParseInteger(value, out Int128 parsed) || parsed < long.MinValue || parsed > ulong.MaxValue)
		{
			throw CheatEngineToolException.InvalidArgument("arguments",
				$"entry {index} must be a decimal or 0x integer from {long.MinValue} to {ulong.MaxValue} for type {VtQword}.");
		}

		return parsed < 0 ? unchecked((ulong) (long) parsed) : (ulong) parsed;
	}

	private static bool TryParseInteger(string value, out Int128 parsed)
	{
		ReadOnlySpan<char> text = value.AsSpan().Trim();
		bool negative = text.StartsWith('-');
		if (negative)
		{
			text = text[1..];
		}

		ulong magnitude;
		bool valid = text.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
			? ulong.TryParse(text[2..], NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out magnitude)
			: ulong.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out magnitude);
		parsed = negative ? -(Int128) magnitude : magnitude;
		return valid;
	}

	private static double Real(string value, double maximum, int type, int index)
	{
		if (!double.TryParse(value.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed) ||
			!double.IsFinite(parsed) || Math.Abs(parsed) > maximum)
		{
			throw CheatEngineToolException.InvalidArgument("arguments",
				$"entry {index} must be a finite decimal number for type {type}.");
		}

		return parsed;
	}

	private static string Expression(string value, int index)
	{
		string text = value.Trim();
		if (text.Length == 0)
		{
			throw CheatEngineToolException.InvalidArgument("arguments",
				$"entry {index} must be an object address or Cheat Engine address expression for type {VtPointer}; use 0 for null.");
		}

		return text;
	}

	private static string Name(string value, string parameter, bool required)
	{
		if (string.IsNullOrWhiteSpace(value))
		{
			if (required)
			{
				throw CheatEngineToolException.InvalidArgument(parameter, "must be non-empty.");
			}

			return string.Empty;
		}

		string name = value.Trim();
		if (name.Length > MaximumNameLength)
		{
			throw CheatEngineToolException.LimitExceeded(parameter, $"must be at most {MaximumNameLength} characters.");
		}

		return name;
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
