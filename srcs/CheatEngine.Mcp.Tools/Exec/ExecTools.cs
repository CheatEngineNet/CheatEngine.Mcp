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
	private const int MaximumCSourceLength = 131_072;
	private const int StackPointerRegister = 4;
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
		"Inject a native DLL or library into the selected target. The absolute local path is checked and held " +
		"against changes while Cheat Engine opens it; protected MCP folders and links are refused. Injection can " +
		"load target code and cannot be undone by MCP. A failure after admission has hostEffect unknown; inspect the " +
		"target before retrying.")]
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
	/// <remarks>
	///     Cheat Engine 7.7's <c>injectDotNetDLL</c> formats the four strings into an Auto Assembler script, so they
	///     are checked by <see cref="ExecSupport.ManagedInjectionText" /> before the file is opened, and the checked
	///     full path Cheat Engine receives is checked again. It waits for the method without a timeout, so the tool
	///     takes none.
	/// </remarks>
	[McpServerTool(Name = CheatEngineToolNames.ExecInjectDotNet, Title = "Inject managed assembly", ReadOnly = false,
		Destructive = true, Idempotent = false, OpenWorld = true, UseStructuredContent = true)]
	[McpMeta(McpDispatchClass.MetaKey, McpDispatchClass.BlockingNative)]
	[RequiresFeature(McpFeature.TargetCodeExecution)]
	[Description(
		"Inject a managed assembly into the selected .NET Framework or .NET Core target and call its public static " +
		"int method that takes one string. The absolute local path is checked and held while Cheat Engine opens it. " +
		"Cheat Engine copies assemblyPath, className, methodName and parameter into fixed Auto Assembler strings, so " +
		"control characters, quotes and braces are refused, as are more than 255 UTF-8 bytes for the path and 127 " +
		"for the others. Cheat Engine then waits on its main thread, with no time limit, until the method returns: " +
		"only the MCP call timeout (45 s by default in the gateway) bounds the request, and Cheat Engine stays " +
		"blocked after it. A target with an active Cheat Engine Mono data collector is refused as unsupported. A " +
		"timeout or failure after admission has hostEffect unknown; inspect the target before retrying.")]
	public ExecDotNetInjection InjectDotNet(
		[Description(
			"Absolute path of the managed assembly on the Cheat Engine host; at most 255 UTF-8 bytes without " +
			"control characters, quotes or braces.")]
		string assemblyPath,
		[Description(
			"Namespace-qualified name of the type that declares the method, such as MyMod.Loader (nested types use " +
			"+); 1 to 127 UTF-8 bytes without control characters, quotes or braces.")]
		string className,
		[Description(
			"Name of a public static method declared as int Method(string); 1 to 127 UTF-8 bytes without control " +
			"characters, quotes or braces.")]
		string methodName,
		[Description(
			"String passed to the method; 0 to 127 UTF-8 bytes without control characters, quotes or braces.")]
		string parameter = "",
		CancellationToken cancellationToken = default)
	{
		RequireTargetExecution(CheatEngineToolNames.ExecInjectDotNet);
		ExecSupport.ManagedInjectionText(assemblyPath, "assemblyPath", ExecSupport.MaximumManagedPathBytes, true);
		ExecSupport.ManagedInjectionText(className, "className", ExecSupport.MaximumManagedTextBytes, true);
		ExecSupport.ManagedInjectionText(methodName, "methodName", ExecSupport.MaximumManagedTextBytes, true);
		ExecSupport.ManagedInjectionText(parameter, "parameter", ExecSupport.MaximumManagedTextBytes, false);
		using HeldFile assembly = OpenLibrary(assemblyPath, "assemblyPath", CheatEngineToolNames.ExecInjectDotNet);
		// Cheat Engine receives the checked full path rather than the caller's spelling, so it is checked as well.
		ExecSupport.ManagedInjectionText(assembly.FullPath, "assemblyPath", ExecSupport.MaximumManagedPathBytes, true);
		return _dispatch.RunLua(CheatEngineToolNames.ExecInjectDotNet, ExecScripts.InjectDotNet,
			ExecJsonContext.Default.ExecDotNetInjection, cancellationToken, assembly.FullPath, className, methodName,
			parameter);
	}

	/// <summary>Calls a target function through Cheat Engine's typed remote-call API.</summary>
	[McpServerTool(Name = CheatEngineToolNames.ExecCallRemote, Title = "Call target function", ReadOnly = false,
		Destructive = true, Idempotent = false, OpenWorld = true, UseStructuredContent = true)]
	[McpMeta(McpDispatchClass.MetaKey, McpDispatchClass.BlockingNative)]
	[RequiresFeature(McpFeature.TargetCodeExecution)]
	[Description(
		"Call one function in the selected target with up to 16 numeric typed arguments, whose values are JSON " +
		"strings even for integers. Allocate and write text or buffers with memory_allocate and memory_write, then " +
		"pass the allocation address as an integer so it remains alive after a timeout. The function address is " +
		"resolved through Client before Cheat Engine runs the fixed call body. Calls refuse a paused or " +
		"debugger-stopped target and wait at most 10 seconds. A timeout or host refusal after the call begins has " +
		"hostEffect unknown; inspect target state before retrying.")]
	public ExecCallResult CallRemote(
		[Description("Address or Cheat Engine expression of the target function.")]
		string functionAddress,
		[Description("stdcall or cdecl.")] ExecCallingConvention callingConvention = ExecCallingConvention.Stdcall,
		[Description("Wait time in milliseconds, 1 to 10000.")]
		int timeoutMilliseconds = 10_000,
		[Description(
			"0 to 16 typed arguments as {type, value}: type is integer, float or double, and value is always a JSON " +
			"string, even for integers (\"10\", not 10). Allocate and write text or buffers with memory_allocate and " +
			"memory_write, then pass the allocation address as an integer.")]
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
	/// <remarks>
	///     Cheat Engine numbers the instance register 0 to 15 as RAX, RCX, RDX, RBX, RSP, RBP, RSI, RDI and R8 to R15,
	///     with the 32-bit names on a 32-bit target, where it raises for 8 to 15. Register 4 would move the stack
	///     pointer, so it is refused here; 8 to 15 on a 32-bit target are refused by the fixed body. On a 64-bit target
	///     Cheat Engine's <c>executeMethod</c> reserves no argument slot for the instance, so the fixed body makes an
	///     RCX call through <c>executeCodeEx</c> with the instance as the first argument and refuses a register that
	///     an argument would overwrite.
	/// </remarks>
	[McpServerTool(Name = CheatEngineToolNames.ExecCallMethod, Title = "Call target instance method", ReadOnly = false,
		Destructive = true, Idempotent = false, OpenWorld = true, UseStructuredContent = true)]
	[McpMeta(McpDispatchClass.MetaKey, McpDispatchClass.BlockingNative)]
	[RequiresFeature(McpFeature.TargetCodeExecution)]
	[Description(
		"Call an instance method in the selected target with up to 16 numeric typed arguments, whose values are JSON " +
		"strings even for integers. Allocate and write text or buffers with memory_allocate and memory_write, then " +
		"pass the allocation address as an integer so it remains alive after a timeout. functionAddress and " +
		"classInstance are resolved through Client before the call. classRegister, 1 (ECX/RCX) by default, carries " +
		"this. On a 32-bit target the arguments go on the stack. On a 64-bit target, classRegister 1 makes the " +
		"Microsoft x64 member call: this in RCX, then argument 1 in RDX or XMM1, 2 in R8 or XMM2, 3 in R9 or XMM3 " +
		"and the rest on the stack. With another register the arguments start at RCX or XMM0, as in " +
		"exec_call_remote, and a register that an argument would overwrite is refused. Calls refuse a paused or " +
		"debugger-stopped target and wait at most 10 seconds; inspect the target before retrying an unknown result.")]
	public ExecCallResult CallMethod(
		[Description("Address or Cheat Engine expression of the instance method.")]
		string functionAddress,
		[Description("Address or Cheat Engine expression of the object instance.")]
		string classInstance,
		[Description(
			"Register that carries this: 0 EAX/RAX, 1 ECX/RCX, 2 EDX/RDX, 3 EBX/RBX, 5 EBP/RBP, 6 ESI/RSI, " +
			"7 EDI/RDI, 8 to 15 R8 to R15 (64-bit targets only). 4 is the stack pointer and is refused. On a 64-bit " +
			"target 1 is the standard member call; Cheat Engine loads an integer argument 2, 3 or 4 into RDX, R8 or " +
			"R9 and a fifth or later argument through RAX, so 2, 8, 9 and 0 are refused when that would overwrite " +
			"this.")]
		int classRegister = 1,
		[Description("stdcall or cdecl.")] ExecCallingConvention callingConvention = ExecCallingConvention.Stdcall,
		[Description("Wait time in milliseconds, 1 to 10000.")]
		int timeoutMilliseconds = 10_000,
		[Description(
			"0 to 16 typed arguments after this, as {type, value}: type is integer, float or double, and value is " +
			"always a JSON string, even for integers (\"10\", not 10). Allocate and write text or buffers with " +
			"memory_allocate and memory_write, then pass the allocation address as an integer.")]
		ExecCallArgument[]? arguments = null,
		CancellationToken cancellationToken = default)
	{
		RequireTargetExecution(CheatEngineToolNames.ExecCallMethod);
		string method = ExecSupport.Expression(functionAddress, "functionAddress");
		string instance = ExecSupport.Expression(classInstance, "classInstance");
		if (classRegister is < 0 or > 15 or StackPointerRegister)
		{
			throw CheatEngineToolException.InvalidArgument("classRegister",
				"must be 0 to 3 or 5 to 15; 4 is the stack pointer, which cannot carry this.",
				"Use 1 (ECX/RCX) unless the method expects this in another register; 8 to 15 need a 64-bit target.");
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
		"Call a one-parameter stdcall function in Cheat Engine's own process. localAddress is resolved in Cheat " +
		"Engine, rather than the selected target. Cheat Engine provides no timeout for this API, so call only a " +
		"routine known to return promptly; a refusal after admission has hostEffect unknown.")]
	public ExecCallResult CallLocal(
		[Description("Cheat Engine local address or expression of the function.")]
		string localAddress,
		[Description(
			"Integer or pointer passed to the function as its exact 64-bit value (10 passes 10); a negative value " +
			"passes its two's-complement bits.")]
		long parameter = 0,
		CancellationToken cancellationToken = default)
	{
		RequireTargetExecution(CheatEngineToolNames.ExecCallLocal);
		string expression = ExecSupport.Expression(localAddress, "localAddress");
		return _dispatch.RunLua(CheatEngineToolNames.ExecCallLocal, ExecScripts.CallLocal,
			ExecJsonContext.Default.ExecCallResult, cancellationToken, expression, parameter);
	}

	/// <summary>Compiles bounded C source through Cheat Engine's compiler.</summary>
	/// <remarks>
	///     Cheat Engine's <c>compile</c> allocates only when no address is given, and only that allocation honours its
	///     kernel-allocation flag; with an address it writes the compiled bytes there. So <c>kernelMode</c> with
	///     <c>address</c> is refused rather than silently ignored, and <c>kernelMode</c> alone needs kernel access.
	/// </remarks>
	[McpServerTool(Name = CheatEngineToolNames.ExecCompileC, Title = "Compile C source", ReadOnly = false,
		Destructive = true, Idempotent = false, OpenWorld = true, UseStructuredContent = true)]
	[McpMeta(McpDispatchClass.MetaKey, McpDispatchClass.BlockingNative)]
	[RequiresFeature(McpFeature.TargetCodeExecution)]
	[Description(
		"Compile up to 131072 characters of C source through Cheat Engine. Without address, Cheat Engine allocates " +
		"memory in the selected target, or in itself with targetSelf, and writes the code there; kernelMode " +
		"allocates kernel memory instead and requires Mcp:EnableKernelAccess. With address, Cheat Engine allocates " +
		"nothing: it compiles for that address and writes the bytes there, overwriting what is there, so pass " +
		"writable memory you own with room for the code, such as a memory_allocate allocation. Cheat Engine owns any " +
		"allocation it made and exposes no general release API, so compile only code you intend to retain for this " +
		"target session.")]
	public ExecCompileResult CompileC(
		[Description("C source, 1 to 131072 characters.")]
		string source,
		[Description(
			"Optional write target in the selected process: Cheat Engine compiles for this address or expression and " +
			"writes the bytes there without allocating. Only for targetSelf=false and without kernelMode.")]
		string? address = null,
		[Description("Compile for Cheat Engine instead of the selected target.")]
		bool targetSelf = false,
		[Description(
			"Allocate the code in kernel memory through Cheat Engine's kernel driver; requires " +
			"Mcp:EnableKernelAccess and applies only without address.")]
		bool kernelMode = false,
		[Description("Skip the source-line information Cheat Engine's debugger uses for the compiled code.")]
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

		if (kernelMode && address is not null)
		{
			throw CheatEngineToolException.InvalidArgument("kernelMode",
				"applies only when address is omitted; with address, Cheat Engine writes to that address and " +
				"allocates nothing.",
				"Omit address to allocate kernel memory, or omit kernelMode to write at address.");
		}

		if (kernelMode)
		{
			// The same refusal as the RequiresFeature call filter, for a requirement that depends on an argument.
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

	private static void RequireConvention(ExecCallingConvention convention)
	{
		if (!Enum.IsDefined(convention))
		{
			throw CheatEngineToolException.InvalidArgument("callingConvention", "must be stdcall or cdecl.");
		}
	}
}
