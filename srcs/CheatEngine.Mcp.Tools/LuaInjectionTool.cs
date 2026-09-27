using System.ComponentModel;

using CheatEngine.Client;

using ModelContextProtocol.Server;

namespace CheatEngine.Mcp.Tools;

/// <summary>Provides bounded injection and remote execution through documented Cheat Engine Lua APIs.</summary>
[McpServerToolType]
public sealed class LuaInjectionTool(ICheatEngineClient client)
{
	[McpServerTool(Name = "inject_library")]
	[Description("Inject a native DLL or library into the selected target. Cheat Engine must report true for success.")]
	public object InjectLibrary([Description("Absolute library path, up to 32767 characters.")] string fileName,
		[Description("Skip symbol reload wait after injection.")]
		bool skipSymbolReloadWait = false)
	{
		if (string.IsNullOrWhiteSpace(fileName) || fileName.Length > 32_767)
		{
			return ToolExecution.Error("fileName must contain at most 32767 characters.");
		}

		return LuaToolRuntime.Invoke(client, "injectLibrary",
			"local result=injectLibrary(a[1],a[2]); assert(result==true,'Cheat Engine reported library injection failure'); return {injected=true}",
			fileName, skipSymbolReloadWait);
	}

	[McpServerTool(Name = "inject_dotnet_library")]
	[Description("Inject a managed assembly and invoke its static entry point in the selected target.")]
	public object InjectDotNetLibrary([Description("Absolute managed assembly path.")] string assemblyPath,
		[Description("Fully qualified class name.")]
		string className,
		[Description("Static method name.")] string methodName,
		[Description("String parameter.")] string parameter = "",
		[Description("Timeout in milliseconds, 0 through 300000.")]
		int timeoutMilliseconds = 30_000)
	{
		if (string.IsNullOrWhiteSpace(assemblyPath) || string.IsNullOrWhiteSpace(className) ||
			string.IsNullOrWhiteSpace(methodName) || timeoutMilliseconds is < 0 or > 300_000)
		{
			return ToolExecution.Error(
				"assemblyPath, className, and methodName are required; timeoutMilliseconds must be between 0 and 300000.");
		}

		return LuaToolRuntime.Invoke(client, "injectDotNetDLL",
			"local result=injectDotNetDLL(a[1],a[2],a[3],a[4],a[5]); assert(result~=false and result~=nil,'Cheat Engine reported managed injection failure'); return {result=result}",
			assemblyPath, className, methodName, parameter, timeoutMilliseconds);
	}

	[McpServerTool(Name = "execute_remote_code")]
	[Description("Execute a one-parameter stdcall function in the selected target and return its copied result.")]
	public object ExecuteRemoteCode([Description("Target function address expression or number.")] string address,
		[Description(
			"Timeout in milliseconds, 1 through 300000. The call waits for completion within the supplied timeout.")]
		int timeoutMilliseconds = 30_000, [Description("Integer parameter.")] long parameter = 0)
	{
		if (string.IsNullOrWhiteSpace(address) || timeoutMilliseconds is < 1 or > 300_000)
		{
			return ToolExecution.Error("address is required and timeoutMilliseconds must be between 1 and 300000.");
		}

		return LuaToolRuntime.Invoke(client, "executeCode",
			"local address=getAddressSafe(a[1]); assert(address~=nil,'Address could not be resolved'); local result=executeCode(address,a[3],a[2]); assert(result~=nil,'Cheat Engine remote call failed'); return {address=string.format('0x%X',address),result=result}",
			address, timeoutMilliseconds, parameter);
	}

	[McpServerTool(Name = "execute_local_code")]
	[Description("Execute a one-parameter stdcall function inside Cheat Engine and return its copied result.")]
	public object ExecuteLocalCode(
		[Description("Cheat Engine local function address expression or number.")]
		string address,
		[Description("Integer parameter.")] long parameter = 0)
	{
		return LuaToolRuntime.Invoke(client, "executeCodeLocal",
			"local address=getAddressSafe(a[1],true); assert(address~=nil,'Local address could not be resolved'); local result=executeCodeLocal(address,a[2]); assert(result~=nil,'Cheat Engine local call failed'); return {address=string.format('0x%X',address),result=result}",
			address, parameter);
	}

	[McpServerTool(Name = "generate_api_hook_script")]
	[Description("Generate a Cheat Engine Auto Assembler API-hook script without applying it.")]
	public object GenerateApiHook([Description("Hook address expression.")] string address,
		[Description("Jump target expression.")]
		string jumpTarget,
		[Description("Optional symbol for the new call address.")]
		string? newCallAddress = null,
		[Description("Optional architecture extension.")]
		string? extension = null,
		[Description("Generate for Cheat Engine itself.")]
		bool targetSelf = false)
	{
		if (string.IsNullOrWhiteSpace(address) || string.IsNullOrWhiteSpace(jumpTarget))
		{
			return ToolExecution.Error("address and jumpTarget are required.");
		}

		return LuaToolRuntime.Invoke(client, "generateAPIHookScript",
			"local script=generateAPIHookScript(a[1],a[2],a[3],a[4],a[5]); assert(script~=nil,'Cheat Engine could not generate a hook script'); return {script=script}",
			address, jumpTarget, newCallAddress, extension, targetSelf);
	}
}
