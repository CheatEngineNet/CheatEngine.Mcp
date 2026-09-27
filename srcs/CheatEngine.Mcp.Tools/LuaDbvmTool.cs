using System.ComponentModel;

using CheatEngine.Client;

using ModelContextProtocol.Server;

namespace CheatEngine.Mcp.Tools;

/// <summary>Provides bounded DBK and DBVM physical-memory operations without automatic initialization.</summary>
[McpServerToolType]
public sealed class LuaDbvmTool(ICheatEngineClient client)
{
	private const int MaximumBytes = 4096;

	[McpServerTool(Name = "dbk_status")]
	[Description("Report whether DBK is already initialized; this query does not initialize it.")]
	public object DbkStatus()
	{
		return LuaToolRuntime.Invoke(client, "dbk_initialized", "return {initialized=dbk_initialized()}");
	}

	[McpServerTool(Name = "dbvm_status")]
	[Description("Report whether DBVM is already initialized; this query does not initialize it.")]
	public object DbvmStatus()
	{
		return LuaToolRuntime.Invoke(client, "dbvm_initialized", "return {initialized=dbvm_initialized()}");
	}

	[McpServerTool(Name = "dbvm_initialize")]
	[Description("Initialize DBVM. Offloading the operating system is advanced and can destabilize the host.")]
	public object Initialize(
		[Description("Offload the operating system when possible.")]
		bool offloadOperatingSystem = false,
		[Description("Short diagnostic reason, up to 256 characters.")]
		string? reason = null)
	{
		if (reason?.Length > 256)
		{
			return ToolExecution.Error("reason must not exceed 256 characters.");
		}

		return LuaToolRuntime.Invoke(client, "dbvm_initialize",
			"dbvm_initialize(a[1],a[2]); assert(dbvm_initialized(),'DBVM initialization failed'); return {initialized=true}",
			offloadOperatingSystem, reason);
	}

	[McpServerTool(Name = "dbk_control_registers")]
	[Description("Read copied CR0, CR3, and CR4 values through DBK.")]
	public object ControlRegisters()
	{
		return LuaToolRuntime.Invoke(client, "dbk_getCR0",
			"return {cr0=string.format('0x%X',dbk_getCR0()),cr3=string.format('0x%X',dbk_getCR3()),cr4=string.format('0x%X',dbk_getCR4())}");
	}

	[McpServerTool(Name = "dbvm_cr4")]
	[Description("Read DBVM's real CR4 value.")]
	public object DbvmCr4()
	{
		return LuaToolRuntime.Invoke(client, "dbvm_getCR4", "return {cr4=string.format('0x%X',dbvm_getCR4())}");
	}

	[McpServerTool(Name = "dbk_physical_address")]
	[Description("Resolve a target virtual address to its physical address through DBK.")]
	public object PhysicalAddress([Description("Target virtual address expression or number.")] string address)
	{
		return string.IsNullOrWhiteSpace(address)
			? ToolExecution.Error("address is required.")
			: LuaToolRuntime.Invoke(client, "dbk_getPhysicalAddress",
				"local address=getAddressSafe(a[1]); assert(address~=nil,'Address could not be resolved'); local physical=dbk_getPhysicalAddress(address); assert(physical~=nil,'DBK could not resolve a physical address'); return {address=string.format('0x%X',address),physicalAddress=string.format('0x%X',physical)}",
				address);
	}

	[McpServerTool(Name = "dbvm_read_physical")]
	[Description("Read a bounded physical-memory range through DBVM.")]
	public object ReadPhysical([Description("Physical address expression or number.")] string address,
		[Description("Byte count from 1 through 4096.")]
		int size)
	{
		if (string.IsNullOrWhiteSpace(address) || size is < 1 or > MaximumBytes)
		{
			return ToolExecution.Error("address is required and size must be between 1 and 4096.");
		}

		return LuaToolRuntime.Invoke(client, "dbvm_readPhysicalMemory",
			"local address=getAddressSafe(a[1]); assert(address~=nil,'Address could not be resolved'); local bytes=dbvm_readPhysicalMemory(address,a[2]); assert(bytes~=nil,'DBVM physical read failed'); local text={}; for i=1,#bytes do text[i]=string.format('%02X',bytes[i]) end; return {address=string.format('0x%X',address),hexBytes=table.concat(text)}",
			address, size);
	}

	[McpServerTool(Name = "dbvm_write_physical")]
	[Description("Write a bounded hexadecimal byte sequence to physical memory through DBVM.")]
	public object WritePhysical([Description("Physical address expression or number.")] string address,
		[Description("Whitespace- or comma-separated hexadecimal bytes.")]
		string hexadecimalBytes)
	{
		if (string.IsNullOrWhiteSpace(address) || string.IsNullOrWhiteSpace(hexadecimalBytes) ||
			hexadecimalBytes.Length > MaximumBytes * 3)
		{
			return ToolExecution.Error("address and hexadecimalBytes are required.");
		}

		return LuaToolRuntime.Invoke(client, "dbvm_writePhysicalMemory",
			"local address=getAddressSafe(a[1]); assert(address~=nil,'Address could not be resolved'); local bytes={}; for token in string.gmatch(a[2],'[^,%s]+') do local value=tonumber(token,16); assert(value and value>=0 and value<=255,'hexadecimalBytes contains an invalid byte'); bytes[#bytes+1]=value; assert(#bytes<=4096,'hexadecimalBytes exceeds 4096 bytes') end; assert(#bytes>0,'hexadecimalBytes is required'); dbvm_writePhysicalMemory(address,bytes); return {address=string.format('0x%X',address),bytesWritten=#bytes}",
			address, hexadecimalBytes);
	}

	[McpServerTool(Name = "dbvm_watch")]
	[Description(
		"Capture a DBVM watch for up to five seconds and disable it before returning up to 1024 copied events.")]
	public object Watch([Description("read, write, or execute.")] string access,
		[Description("Physical address expression or number.")]
		string address,
		[Description("Watched byte size (1-4096).")]
		int byteSize = 1,
		[Description("DBVM options bit field, bits 0-3 only.")]
		int options = 0,
		[Description("Internal log entries from 1 through 4096.")]
		int internalEntryCount = 1024,
		[Description("Capture duration (1-5000 milliseconds). Blocks Cheat Engine during capture.")]
		int durationMilliseconds = 100)
	{
		if (string.IsNullOrWhiteSpace(address) || byteSize is < 1 or > 4096 || internalEntryCount is < 1 or > 4096 ||
			(options & ~0x0F) != 0 || durationMilliseconds is < 1 or > 5000)
		{
			return ToolExecution.Error(
				"address is required, byteSize must be between 1 and 4096, internalEntryCount must be between 1 and 4096, and options may use only bits 0 through 3.");
		}

		string? function = access?.ToLowerInvariant() switch
		{
			"read" => "dbvm_watch_reads",
			"write" => "dbvm_watch_writes",
			"execute" => "dbvm_watch_executes",
			_ => null
		};
		return function is null
			? ToolExecution.Error("access must be read, write, or execute.")
			: LuaToolRuntime.Invoke(client, function,
				"local address=getAddress(a[1]); local id=" + function +
				"(address,a[2],a[3],a[4]); assert(type(id)=='number' and id>=0,'DBVM watch creation failed'); local ok,events=pcall(function() sleep(a[5]); return dbvm_watch_retrievelog(id) or {} end); local stopped,err=pcall(dbvm_watch_disable,id); assert(stopped and err~=false,'Could not disable DBVM watch '..tostring(id)..'; manual recovery required'); assert(ok,events); local result={}; for i=1,math.min(#events,1024) do result[i]=events[i] end; return {id=id,address=string.format('0x%X',address),stopped=true,events=result,truncated=#events>1024}",
				address, byteSize, options, internalEntryCount, durationMilliseconds);
	}

	[McpServerTool(Name = "dbvm_watch_log")]
	[Description("Retrieve a bounded copied DBVM watch log.")]
	public object WatchLog([Description("DBVM watch identifier.")] long id,
		[Description("Maximum events from 1 through 1024.")]
		int maximumResults = 256)
	{
		if (id < 0 || maximumResults is < 1 or > 1024)
		{
			return ToolExecution.Error("id must be non-negative and maximumResults must be between 1 and 1024.");
		}

		return LuaToolRuntime.Invoke(client, "dbvm_watch_retrievelog",
			"local events=dbvm_watch_retrievelog(a[1]) or {}; local result={}; for i=1,math.min(#events,a[2]) do result[i]=events[i] end; return {count=#events,truncated=#events>a[2],events=result}",
			id, maximumResults);
	}

	[McpServerTool(Name = "dbvm_watch_stop")]
	[Description("Disable a DBVM watch by identifier.")]
	public object StopWatch([Description("Non-negative DBVM watch identifier.")] long id)
	{
		return id < 0
			? ToolExecution.Error("id must be non-negative.")
			: LuaToolRuntime.Invoke(client, "dbvm_watch_disable",
				"dbvm_watch_disable(a[1]); return {id=a[1],stopped=true}", id);
	}
}
