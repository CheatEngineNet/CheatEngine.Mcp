using System.ComponentModel;

using CheatEngine.Mcp.Core.Contract;
using CheatEngine.Mcp.Core.Features;
using CheatEngine.Mcp.Core.Files;

using ModelContextProtocol.Server;

namespace CheatEngine.Mcp.Tools.Exec;

/// <summary>Bounded, explicitly gated injection, function-call and C-compilation tools.</summary>
[McpServerToolType]
public sealed class ExecTools
{
	private const int MaximumPathLength = 32_767;
	private const int MaximumManagedTimeoutMilliseconds = 30_000;
	private const int MaximumCSourceLength = 131_072;
	private readonly ToolDispatch _dispatch;
	private readonly McpFilePaths _files;

	/// <summary>Creates the tool container without accessing Cheat Engine or the host filesystem.</summary>
	public ExecTools(ToolDispatch dispatch, McpFilePaths files)
	{
		ArgumentNullException.ThrowIfNull(dispatch);
		ArgumentNullException.ThrowIfNull(files);
		_dispatch = dispatch;
		_files = files;
	}

	/// <summary>Injects a native library into the selected target.</summary>
	[McpServerTool(Name = CheatEngineToolNames.ExecInjectLibrary, Title = "Inject native library", ReadOnly = false,
		Destructive = true, Idempotent = false, OpenWorld = true, UseStructuredContent = true)]
	[McpMeta(McpDispatchClass.MetaKey, McpDispatchClass.BlockingNative)]
	[RequiresFeature(McpFeature.TargetCodeExecution)]
	[Description(
		"Inject a native DLL or library into the selected target. The absolute local path is checked and held against changes while Cheat Engine opens it; protected MCP folders and links are refused. Injection can load target code and cannot be undone by MCP. A failure after admission has hostEffect unknown; inspect the target before retrying.")]
	public ExecLibraryInjection InjectLibrary(
		[Description("Absolute path of the native library on the Cheat Engine host.")]
		string libraryPath,
		[Description("Do not wait for Cheat Engine to reload symbols after injection.")]
		bool skipSymbolReloadWait = false,
		CancellationToken cancellationToken = default)
	{
		RequireTargetExecution(CheatEngineToolNames.ExecInjectLibrary);
		using HeldFile library = OpenLibrary(libraryPath, "libraryPath", CheatEngineToolNames.ExecInjectLibrary);
		return _dispatch.RunLua(CheatEngineToolNames.ExecInjectLibrary, ExecScripts.InjectLibrary,
			ExecJsonContext.Default.ExecLibraryInjection, cancellationToken, library.FullPath, skipSymbolReloadWait);
	}

	/// <summary>Injects a managed assembly and calls a static entry point in the selected target.</summary>
	[McpServerTool(Name = CheatEngineToolNames.ExecInjectDotNet, Title = "Inject managed assembly", ReadOnly = false,
		Destructive = true, Idempotent = false, OpenWorld = true, UseStructuredContent = true)]
	[McpMeta(McpDispatchClass.MetaKey, McpDispatchClass.BlockingNative)]
	[RequiresFeature(McpFeature.TargetCodeExecution)]
	[Description(
		"Inject a managed assembly and invoke its static method in the selected target. The assembly path is absolute, local, checked and held while Cheat Engine opens it. timeoutMilliseconds is 0 to 30000; a timeout or failed admission leaves the target effect unknown, so do not retry blindly.")]
	public ExecDotNetInjection InjectDotNet(
		[Description("Absolute path of the managed assembly on the Cheat Engine host.")]
		string assemblyPath,
		[Description("Fully qualified type name that contains the static entry point.")]
		string className,
		[Description("Static method name to invoke.")]
		string methodName,
		[Description("String passed to the static method.")]
		string parameter = "",
		[Description("Cheat Engine wait time in milliseconds, 0 to 30000.")]
		int timeoutMilliseconds = 30_000,
		CancellationToken cancellationToken = default)
	{
		RequireTargetExecution(CheatEngineToolNames.ExecInjectDotNet);
		using HeldFile assembly = OpenLibrary(assemblyPath, "assemblyPath", CheatEngineToolNames.ExecInjectDotNet);
		RequireText(className, "className", 1024);
		RequireText(methodName, "methodName", 256);
		if (parameter is null || parameter.Length > ExecSupport.MaximumArgumentText)
		{
			throw CheatEngineToolException.InvalidArgument("parameter",
				$"must contain at most {ExecSupport.MaximumArgumentText} characters.");
		}

		if (timeoutMilliseconds is < 0 or > MaximumManagedTimeoutMilliseconds)
		{
			throw CheatEngineToolException.InvalidArgument("timeoutMilliseconds", "must be between 0 and 30000.");
		}

		return _dispatch.RunLua(CheatEngineToolNames.ExecInjectDotNet, ExecScripts.InjectDotNet,
			ExecJsonContext.Default.ExecDotNetInjection, cancellationToken, assembly.FullPath, className, methodName,
			parameter, timeoutMilliseconds);
	}

	/// <summary>Calls a target function through Cheat Engine's typed remote-call API.</summary>
	[McpServerTool(Name = CheatEngineToolNames.ExecCallRemote, Title = "Call target function", ReadOnly = false,
		Destructive = true, Idempotent = false, OpenWorld = true, UseStructuredContent = true)]
	[McpMeta(McpDispatchClass.MetaKey, McpDispatchClass.BlockingNative)]
	[RequiresFeature(McpFeature.TargetCodeExecution)]
	[Description(
		"Call one function in the selected target with up to 16 numeric typed arguments. Allocate and write text or buffers with memory_allocate and memory_write, then pass the allocation address as an integer so it remains alive after a timeout. The function address is resolved through Client before Cheat Engine runs the fixed call body. Calls refuse a paused or debugger-stopped target and wait at most 10 seconds. A timeout or host refusal after the call begins has hostEffect unknown; inspect target state before retrying.")]
	public ExecCallResult CallRemote(
		[Description("Address or Cheat Engine expression of the target function.")]
		string functionAddress,
		[Description("stdcall or cdecl.")] ExecCallingConvention callingConvention = ExecCallingConvention.Stdcall,
		[Description("Wait time in milliseconds, 1 to 10000.")]
		int timeoutMilliseconds = 10_000,
		[Description(
			"0 to 16 numeric typed arguments. Allocate and write text or buffers with memory_allocate and memory_write, then pass the allocation address as an integer.")]
		ExecCallArgument[]? arguments = null,
		CancellationToken cancellationToken = default)
	{
		RequireTargetExecution(CheatEngineToolNames.ExecCallRemote);
		string expression = ExecSupport.Expression(functionAddress, "functionAddress");
		RequireConvention(callingConvention);
		ExecSupport.Timeout(timeoutMilliseconds);
		(int[] types, object?[] values) = ExecSupport.Arguments(arguments ?? []);
		return _dispatch.Run(CheatEngineToolNames.ExecCallRemote, token =>
		{
			ulong address = ExecSupport.Resolve(_dispatch.Client, expression, "functionAddress", token).ToUInt64();
			return _dispatch.ExecuteLua(CheatEngineToolNames.ExecCallRemote, ExecScripts.CallRemote,
				ExecJsonContext.Default.ExecCallResult, token, address, (int) callingConvention, timeoutMilliseconds,
				types,
				values);
		}, cancellationToken);
	}

	/// <summary>Calls an instance method in the selected target through Cheat Engine's method-call API.</summary>
	[McpServerTool(Name = CheatEngineToolNames.ExecCallMethod, Title = "Call target instance method", ReadOnly = false,
		Destructive = true, Idempotent = false, OpenWorld = true, UseStructuredContent = true)]
	[McpMeta(McpDispatchClass.MetaKey, McpDispatchClass.BlockingNative)]
	[RequiresFeature(McpFeature.TargetCodeExecution)]
	[Description(
		"Call an instance method in the selected target with up to 16 numeric typed arguments. Allocate and write text or buffers with memory_allocate and memory_write, then pass the allocation address as an integer so it remains alive after a timeout. functionAddress and classInstance are resolved through Client before the call. classRegister is Cheat Engine's register number for this (ECX/RCX is 1). Calls refuse a paused or debugger-stopped target and wait at most 10 seconds; inspect the target before retrying an unknown result.")]
	public ExecCallResult CallMethod(
		[Description("Address or Cheat Engine expression of the instance method.")]
		string functionAddress,
		[Description("Address or Cheat Engine expression of the object instance.")]
		string classInstance,
		[Description("Register number for this, 0 through 15; 1 is ECX/RCX.")]
		int classRegister = 1,
		[Description("stdcall or cdecl.")] ExecCallingConvention callingConvention = ExecCallingConvention.Stdcall,
		[Description("Wait time in milliseconds, 1 to 10000.")]
		int timeoutMilliseconds = 10_000,
		[Description(
			"0 to 16 numeric typed arguments after this. Allocate and write text or buffers with memory_allocate and memory_write, then pass the allocation address as an integer.")]
		ExecCallArgument[]? arguments = null,
		CancellationToken cancellationToken = default)
	{
		RequireTargetExecution(CheatEngineToolNames.ExecCallMethod);
		string method = ExecSupport.Expression(functionAddress, "functionAddress");
		string instance = ExecSupport.Expression(classInstance, "classInstance");
		if (classRegister is < 0 or > 15)
		{
			throw CheatEngineToolException.InvalidArgument("classRegister", "must be between 0 and 15.");
		}

		RequireConvention(callingConvention);
		ExecSupport.Timeout(timeoutMilliseconds);
		(int[] types, object?[] values) = ExecSupport.Arguments(arguments ?? []);
		return _dispatch.Run(CheatEngineToolNames.ExecCallMethod, token =>
		{
			ulong address = ExecSupport.Resolve(_dispatch.Client, method, "functionAddress", token).ToUInt64();
			ulong target = ExecSupport.Resolve(_dispatch.Client, instance, "classInstance", token).ToUInt64();
			return _dispatch.ExecuteLua(CheatEngineToolNames.ExecCallMethod, ExecScripts.CallMethod,
				ExecJsonContext.Default.ExecCallResult, token, address, (int) callingConvention, timeoutMilliseconds,
				target, classRegister, types, values);
		}, cancellationToken);
	}

	/// <summary>Calls a one-parameter function in Cheat Engine's own process.</summary>
	[McpServerTool(Name = CheatEngineToolNames.ExecCallLocal, Title = "Call Cheat Engine function", ReadOnly = false,
		Destructive = true, Idempotent = false, OpenWorld = true, UseStructuredContent = true)]
	[McpMeta(McpDispatchClass.MetaKey, McpDispatchClass.BlockingNative)]
	[RequiresFeature(McpFeature.TargetCodeExecution)]
	[Description(
		"Call a one-parameter stdcall function in Cheat Engine's own process. localAddress is resolved in Cheat Engine, rather than the selected target. Cheat Engine provides no timeout for this API, so call only a routine known to return promptly; a refusal after admission has hostEffect unknown.")]
	public ExecCallResult CallLocal(
		[Description("Cheat Engine local address or expression of the function.")]
		string localAddress,
		[Description("Integer or pointer parameter passed to the function.")]
		long parameter = 0,
		CancellationToken cancellationToken = default)
	{
		RequireTargetExecution(CheatEngineToolNames.ExecCallLocal);
		string expression = ExecSupport.Expression(localAddress, "localAddress");
		return _dispatch.RunLua(CheatEngineToolNames.ExecCallLocal, ExecScripts.CallLocal,
			ExecJsonContext.Default.ExecCallResult, cancellationToken, expression, parameter);
	}

	/// <summary>Compiles bounded C source through Cheat Engine's compiler.</summary>
	[McpServerTool(Name = CheatEngineToolNames.ExecCompileC, Title = "Compile C source", ReadOnly = false,
		Destructive = true, Idempotent = false, OpenWorld = true, UseStructuredContent = true)]
	[McpMeta(McpDispatchClass.MetaKey, McpDispatchClass.BlockingNative)]
	[RequiresFeature(McpFeature.TargetCodeExecution)]
	[Description(
		"Compile up to 131072 characters of C source through Cheat Engine. By default compilation targets the selected process; address optionally chooses the target allocation location. targetSelf compiles for Cheat Engine itself. kernelMode additionally requires Mcp:EnableKernelAccess. Cheat Engine owns any compiled allocation and exposes no general release API, so compile only code you intend to retain for this target session.")]
	public ExecCompileResult CompileC(
		[Description("C source, 1 to 131072 characters.")]
		string source,
		[Description(
			"Optional target address or Cheat Engine expression near which to compile; only for targetSelf=false.")]
		string? address = null,
		[Description("Compile for Cheat Engine instead of the selected target.")]
		bool targetSelf = false,
		[Description("Compile in kernel mode; requires Mcp:EnableKernelAccess.")]
		bool kernelMode = false,
		[Description("Do not attach a debugger to the generated code.")]
		bool noDebug = true,
		CancellationToken cancellationToken = default)
	{
		RequireTargetExecution(CheatEngineToolNames.ExecCompileC);
		if (string.IsNullOrWhiteSpace(source) || source.Length > MaximumCSourceLength)
		{
			throw CheatEngineToolException.InvalidArgument("source",
				$"must contain 1 to {MaximumCSourceLength} characters.");
		}

		if (targetSelf && address is not null)
		{
			throw CheatEngineToolException.InvalidArgument("address", "applies only when targetSelf is false.");
		}

		if (kernelMode)
		{
			_dispatch.Features.Require(McpFeature.KernelAccess, CheatEngineToolNames.ExecCompileC);
		}

		string? expression = address is null ? null : ExecSupport.Expression(address, "address");
		return _dispatch.Run(CheatEngineToolNames.ExecCompileC, token =>
		{
			ulong? targetAddress = null;
			if (!targetSelf)
			{
				// This fails with not_attached before the compiler can allocate into a target.
				_ = _dispatch.Client.Processes.GetCurrentProcess(token);
				if (expression is not null)
				{
					targetAddress = ExecSupport.Resolve(_dispatch.Client, expression, "address", token).ToUInt64();
				}
			}

			return _dispatch.ExecuteLua(CheatEngineToolNames.ExecCompileC, ExecScripts.CompileC,
				ExecJsonContext.Default.ExecCompileResult, token, source, targetAddress, targetSelf, kernelMode,
				noDebug);
		}, cancellationToken);
	}

	private void RequireTargetExecution(string toolName)
	{
		_dispatch.Features.Require(McpFeature.TargetCodeExecution, toolName);
	}

	private HeldFile OpenLibrary(string path, string parameter, string operation)
	{
		if (path is null || path.Length > MaximumPathLength)
		{
			throw CheatEngineToolException.InvalidArgument(parameter,
				$"must contain an absolute path of at most {MaximumPathLength} characters.");
		}

		return _files.OpenRead(path, operation, 0, parameter);
	}

	private static void RequireText(string? value, string parameter, int maximum)
	{
		if (string.IsNullOrWhiteSpace(value) || value.Length > maximum || value.Any(char.IsControl))
		{
			throw CheatEngineToolException.InvalidArgument(parameter,
				$"must contain 1 to {maximum} characters without control characters.");
		}
	}

	private static void RequireConvention(ExecCallingConvention convention)
	{
		if (!Enum.IsDefined(convention))
		{
			throw CheatEngineToolException.InvalidArgument("callingConvention", "must be stdcall or cdecl.");
		}
	}
}
