using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;

using CheatEngine.Client;

using ModelContextProtocol.Server;
namespace CheatEngine.Mcp.Tools;

/// <summary>Provides bounded debugger control without retaining callbacks or native debugger handles.</summary>
[McpServerToolType]
public sealed class LuaDebuggerTool(ICheatEngineClient client, LuaDebuggerCaptureGuard? captureGuard = null)
{
	private long? _lastProcessId;
	private long? _lastRequestedInterface;
	private long? _lastActualInterface;
	[McpServerTool(Name = "debugger_start"), Description("Attach the selected debugger interface. A repeated request is idempotent. Interface changes require explicit detach; targets previously using VEH must restart before reattachment.")]
	public object Start([Description("0 default, 1 Windows, 2 VEH, or 3 kernel.")] int debuggerInterface = 0)
	{
		if (debuggerInterface is < 0 or > 3)
		{
			return ToolExecution.Error("debuggerInterface must be between 0 and 3.");
		}


		return WithIdleDebugger(() =>
		{
			(int processId, string? identity) = GetTargetIdentity();
			if (identity is null)
			{
				return ToolExecution.Error("The selected local process is no longer live.");
			}
			Dictionary<string, object?> result = (Dictionary<string, object?>) LuaToolRuntime.Execute(client, "debugProcess",
				LuaDebuggerScripts.Attach, debuggerInterface, processId, identity, _lastProcessId, _lastRequestedInterface, _lastActualInterface)!;
			_lastProcessId = (long) result["processId"]!;
			_lastRequestedInterface = debuggerInterface;
			_lastActualInterface = (long) result["debuggerInterface"]!;
			return new
			{
				success = true,
				result
			};
		});
	}

	[McpServerTool(Name = "debugger_detach"), Description("Unpause and detach the debugger while preserving the current target selection.")]
	public object Detach() => WithIdleDebugger(() =>
	{
		(int processId, string? identity) = GetTargetIdentity();
		object? result = LuaToolRuntime.Execute(client, "detachIfPossible", LuaDebuggerScripts.Detach, processId, identity);
		_lastProcessId = null;
		_lastRequestedInterface = null;
		_lastActualInterface = null;
		return new
		{
			success = true,
			result
		};
	});

	[McpServerTool(Name = "debugger_status"), Description("Read copied debugger state and active interface.")]
	public object Status() => LuaDebuggerScripts.Invoke(client, "debugger_status", LuaDebuggerScripts.Status);

	[McpServerTool(Name = "debugger_break_thread"), Description("Request that Cheat Engine break a target thread; stopping can be asynchronous.")]
	public object BreakThread([Description("Target thread identifier.")] long threadId) => GuardedInvoke("debug_breakThread", "assert(flag('debug_isDebugging'),'Attach the debugger explicitly before breaking a thread'); debug_breakThread(a[1]); return {threadId=a[1],requested=true}", threadId);

	[McpServerTool(Name = "debugger_add_breakpoint"), Description("Create an execute, access, or write breakpoint. It persists until removed or debugger detachment.")]
	public object AddBreakpoint([Description("Target address expression or number.")] string address, [Description("Watch size for access or write: 1, 2, 4, or 8.")] int size = 1, [Description("execute, access, or write.")] string trigger = "execute")
	{
		if (string.IsNullOrWhiteSpace(address) || size is not 1 and not 2 and not 4 and not 8)
		{
			return ToolExecution.Error("address is required and size must be 1, 2, 4, or 8.");
		}

		if (trigger is not ("execute" or "access" or "write"))
		{
			return ToolExecution.Error("trigger must be execute, access, or write.");
		}

		return GuardedInvoke("debug_setBreakpoint", "local address=getAddressSafe(a[1]); assert(address~=nil,'Address could not be resolved'); assert(flag('debug_isDebugging'),'Debugger is not attached'); local triggers={execute=bptExecute,access=bptAccess,write=bptWrite}; local applied,id=debug_setBreakpoint(address,a[2],triggers[a[3]]); assert(applied==true,'Breakpoint was rejected'); return {address=string.format('0x%X',address),id=breakpointDescriptor(id),size=a[2],trigger=a[3]}", address, size, trigger);
	}

	[McpServerTool(Name = "debugger_remove_breakpoint"), Description("Remove the breakpoint containing the supplied address.")]
	public object RemoveBreakpoint([Description("Breakpoint address expression or number.")] string address) => GuardedInvoke("debug_removeBreakpoint", "local address=getAddressSafe(a[1]); assert(address~=nil,'Address could not be resolved'); assert(debug_removeBreakpoint(address)~=false,'Breakpoint removal was rejected'); return {address=string.format('0x%X',address),removed=true}", address);

	[McpServerTool(Name = "debugger_breakpoints"), Description("Return a bounded copied list of breakpoint addresses.")]
	public object Breakpoints([Description("Maximum entries from 1 through 1024.")] int maximumResults = 256)
	{
		if (maximumResults is < 1 or > 1024)
		{
			return ToolExecution.Error("maximumResults must be between 1 and 1024.");
		}

		return LuaToolRuntime.Invoke(client, "debug_getBreakpointList", "local source=debug_getBreakpointList() or {}; local items={}; for i=1,math.min(#source,a[1]) do items[i]=string.format('0x%X',source[i]) end; return {count=#source,truncated=#source>a[1],breakpoints=items}", maximumResults);
	}

	[McpServerTool(Name = "debugger_continue"), Description("Continue, step into, or step over from the current broken context.")]
	public object Continue([Description("run, stepInto, or stepOver.")] string mode = "run")
	{
		string? method = mode switch
		{
			"run" => "co_run",
			"stepInto" => "co_stepinto",
			"stepOver" => "co_stepover",
			_ => null
		};
		if (method is null)
		{
			return ToolExecution.Error("mode must be run, stepInto, or stepOver.");
		}


		return GuardedInvoke("debug_continueFromBreakpoint", "assert(stopped(),'Debugger has no stopped context'); local methods={run=co_run,stepInto=co_stepinto,stepOver=co_stepover}; debug_continueFromBreakpoint(methods[a[1]]); return {continued=true,mode=a[1]}", mode);
	}

	[McpServerTool(Name = "debugger_context"), Description("Read copied current registers; the debugger must be broken for meaningful values.")]
	public object Context([Description("Include floating-point and XMM registers.")] bool includeExtraRegisters = false) => LuaToolRuntime.Invoke(client, "debug_getCurrentContextTable", LuaDebuggerScripts.State + "\n" + "assert(stopped(a[1]),'Debugger has no stopped context'); return {registers=debug_getCurrentContextTable(a[1])}", includeExtraRegisters);

	[McpServerTool(Name = "debugger_set_register"), Description("Set one general-purpose register in the currently broken debugger context, then write the context back before continuation.")]
	public object SetRegister([Description("Register name: EAX through EIP, RAX through R15, RIP, RSP, RBP, or EFLAGS.")] string register, [Description("Integer value to write.")] long value)
	{
		string normalized = register?.ToUpperInvariant() ?? string.Empty;
		if (normalized is not ("EAX" or "EBX" or "ECX" or "EDX" or "ESI" or "EDI" or "EBP" or "ESP" or "EIP" or "RAX" or "RBX" or "RCX" or "RDX" or "RSI" or "RDI" or "RBP" or "RSP" or "RIP" or "R8" or "R9" or "R10" or "R11" or "R12" or "R13" or "R14" or "R15" or "EFLAGS"))
		{
			return ToolExecution.Error("register must name a supported general-purpose register.");
		}
		return GuardedInvoke("debug_setContext", """
			assert(stopped(),'Debugger has no stopped context')
			assert(targetIsX86(),'Register editing requires an x86 or x64 target')
			local wide=targetIs64Bit()
			local name=a[1]
			local aliases={EAX='RAX',EBX='RBX',ECX='RCX',EDX='RDX',ESI='RSI',EDI='RDI',EBP='RBP',ESP='RSP',EIP='RIP'}
			local is32=name=='EFLAGS' or aliases[name]~=nil
			assert(wide or is32,'This register is unavailable in a 32-bit target')
			if is32 then assert(a[2]>=-2147483648 and a[2]<=4294967295,'Value does not fit a 32-bit register') end
			local expected=is32 and (a[2]&0xFFFFFFFF) or a[2]
			local actualName=wide and aliases[name] or name
			assert(debug_getContext(false)==true,'Could not read the broken context')
			assert(math.type(_G[actualName])=='integer','Register is unavailable in the current context')
			_G[actualName]=expected
			assert(debug_setContext(false)==true,'Could not write the broken context')
			assert(debug_getContext(false)==true,'Could not verify the written context')
			assert(_G[actualName]==expected,'Register read-back differs from the requested value')
			debug_updateGUI()
			return {register=name,contextRegister=actualName,value=string.format('0x%X',expected),verified=true}
			""", normalized, value);
	}

	[McpServerTool(Name = "debugger_ignore_thread"), Description("Add or remove a target thread from Cheat Engine's breakpoint-ignore list.")]
	public object IgnoreThread([Description("Target thread identifier.")] long threadId, [Description("True adds; false removes.")] bool ignore = true) => GuardedInvoke("debug_addThreadToNoBreakList", "if a[2] then debug_addThreadToNoBreakList(a[1]) else debug_removeThreadFromNoBreakList(a[1]) end; return {threadId=a[1],ignored=a[2]}", threadId, ignore);
	private object WithIdleDebugger(Func<object> body) => ToolExecution.Run(client, () => captureGuard?.PrepareForTransition() ?? body());

	private object GuardedInvoke(string operation, string body, params object?[] arguments) => WithIdleDebugger(() => LuaToolRuntime.Invoke(client, operation, LuaDebuggerScripts.State + "\n" + body, arguments));
	private (int ProcessId, string? Identity) GetTargetIdentity()
	{
		long selected = (long) LuaToolRuntime.Execute(client, "debugger_target", "return getOpenedProcessID()")!;
		int processId = checked((int) selected);
		if (processId <= 0)
		{
			return (processId, null);
		}
		try
		{
			using Process process = Process.GetProcessById(processId);
			if (process.HasExited)
			{
				return (processId, null);
			}
			string identity = processId.ToString(CultureInfo.InvariantCulture) + ":" +
				process.StartTime.ToUniversalTime().Ticks.ToString(CultureInfo.InvariantCulture);
			return (processId, identity);
		}
		catch (ArgumentException)
		{
			return (processId, null);
		}
	}
}
