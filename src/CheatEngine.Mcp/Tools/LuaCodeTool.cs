using System.ComponentModel;

using CheatEngine.Client;

using ModelContextProtocol.Server;

namespace CheatEngine.Mcp.Tools;

/// <summary>Supplies bounded Cheat Engine code-view and code-analysis operations absent from CheatEngine.Client.</summary>
[McpServerToolType]
public sealed class LuaCodeTool
{
	private const int MaximumInstructions = 1024;
	private const int MaximumTextLength = 1_048_576;
	private readonly ICheatEngineClient _client;

	public LuaCodeTool(ICheatEngineClient client)
	{
		_client = client;
	}

	[McpServerTool(Name = "disassemble_bytes"), Description("Disassemble a bounded hexadecimal byte sequence without reading target memory.")]
	public object DisassembleBytes([Description("Hexadecimal bytes, up to 1 MiB of text. ")] string hexadecimalBytes,
		[Description("Optional target origin expression for relative operands. ")] string? address = null)
	{
		if (string.IsNullOrWhiteSpace(hexadecimalBytes) || hexadecimalBytes.Length > MaximumTextLength)
		{
			return ToolExecution.Error("hexadecimalBytes must contain at most 1048576 characters.");
		}

		return Invoke("disassemble_bytes", """
			local origin=0; if a.n>=2 and a[2]~=nil then origin=getAddressSafe(a[2]); if origin==nil then error('Address could not be resolved') end end
			return {origin=origin,text=disassembleBytes(a[1],origin)}
			""", hexadecimalBytes, address);
	}

	[McpServerTool(Name = "get_previous_opcodes"), Description("Return a bounded predecessor-instruction walk before one target address.")]
	public object GetPreviousOpcodes([Description("Target address or symbol expression. ")] string address,
		[Description("Predecessor count (1-1024). ")] int count = 5)
	{
		if (string.IsNullOrWhiteSpace(address) || count is < 1 or > MaximumInstructions)
		{
			return ToolExecution.Error("address is required and count must be between 1 and 1024.");
		}

		return Invoke("get_previous_opcodes", """
			local current=getAddressSafe(a[1]); if current==nil then error('Address could not be resolved') end; local reverse={}
			for i=1,a[2] do local previous=getPreviousOpcode(current); if previous==nil or previous>=current then break end; current=previous; table.insert(reverse,1,current) end
			local items={}; for i=1,#reverse do local text=disassemble(reverse[i]); local extra,opcode,bytes,addressText=splitDisassembledString(text); items[#items+1]={address=reverse[i],addressText=addressText,bytes=bytes,opcode=opcode,extra=extra} end; return {instructions=items}
			""", address, count);
	}

	[McpServerTool(Name = "get_function_range"), Description("Return Cheat Engine's estimated function boundaries for a target address.")]
	public object GetFunctionRange([Description("Address inside the target function. ")] string address)
	{
		if (string.IsNullOrWhiteSpace(address))
		{
			return ToolExecution.Error("address is required.");
		}

		return Invoke("get_function_range", """
			local address=getAddressSafe(a[1]); if address==nil then error('Address could not be resolved') end; local start,stop=getFunctionRange(address); return {startAddress=start,endAddress=stop,size=stop-start}
			""", address);
	}

	[McpServerTool(Name = "is_jump_destination"), Description("Check whether an address is a jump destination within a bounded code range.")]
	public object IsJumpDestination([Description("Address to test. ")] string address,
		[Description("Containing function or code-range size in bytes (1-1048576). ")] int range = 4096)
	{
		if (string.IsNullOrWhiteSpace(address) || range is < 1 or > 1_048_576)
		{
			return ToolExecution.Error("address is required and range must be between 1 and 1048576.");
		}

		return Invoke("is_jump_destination", """
			local address=getAddressSafe(a[1]); if address==nil then error('Address could not be resolved') end; return {address=address,isJumpDestination=isJumpDestination(address,a[2])}
			""", address, range);
	}

	[McpServerTool(Name = "set_comment"), Description("Set a persistent user-defined Memory View comment at a target address.")]
	public object SetComment([Description("Target address or symbol expression. ")] string address,
		[Description("Comment text, up to 1 MiB. ")] string comment)
	{
		if (string.IsNullOrWhiteSpace(address) || comment is null || comment.Length > MaximumTextLength)
		{
			return ToolExecution.Error("address is required and comment must contain at most 1048576 characters.");
		}

		return Invoke("set_comment", """
			local address=getAddressSafe(a[1]); if address==nil then error('Address could not be resolved') end; setComment(address,a[2]); return {address=address}
			""", address, comment);
	}

	[McpServerTool(Name = "get_comment"), Description("Read the persistent user-defined Memory View comment at a target address.")]
	public object GetComment([Description("Target address or symbol expression. ")] string address)
	{
		if (string.IsNullOrWhiteSpace(address))
		{
			return ToolExecution.Error("address is required.");
		}

		return Invoke("get_comment", """
			local address=getAddressSafe(a[1]); if address==nil then error('Address could not be resolved') end; return {address=address,comment=getComment(address)}
			""", address);
	}

	[McpServerTool(Name = "analyze_code_range"), Description("Run Cheat Engine's code dissector on one bounded target-memory range.")]
	public object AnalyzeCodeRange([Description("Range base address or symbol expression. ")] string address,
		[Description("Range size in bytes (1-1048576). ")] int size = 4096)
	{
		if (string.IsNullOrWhiteSpace(address) || size is < 1 or > 1_048_576)
		{
			return ToolExecution.Error("address is required and size must be between 1 and 1048576.");
		}

		return Invoke("analyze_code_range", """
			local address=getAddressSafe(a[1]); if address==nil then error('Address could not be resolved') end; local d=getDissectCode(); d.dissect(address,a[2]); return {baseAddress=address,size=a[2]}
			""", address, size);
	}

	[McpServerTool(Name = "get_code_references"), Description("Read copied references to an address from Cheat Engine's current code dissector data.")]
	public object GetCodeReferences([Description("Target address or symbol expression. ")] string address,
		[Description("Maximum references to return (1-1024). ")] int maximumResults = 256)
	{
		if (string.IsNullOrWhiteSpace(address) || maximumResults is < 1 or > MaximumInstructions)
		{
			return ToolExecution.Error("address is required and maximumResults must be between 1 and 1024.");
		}

		return Invoke("get_code_references", """
			local address=getAddressSafe(a[1]); if address==nil then error('Address could not be resolved') end; local refs=getDissectCode().getReferences(address) or {}; local items={}; local count=0; for source,kind in pairs(refs) do count=count+1; if #items<a[2] then items[#items+1]={fromAddress=string.format('0x%X',source),toAddress=string.format('0x%X',address),type=kind} end end; return {address=string.format('0x%X',address),count=count,truncated=count>a[2],references=items}
			""", address, maximumResults);
	}

	[McpServerTool(Name = "get_referenced_strings"), Description("Read a bounded copied list of strings referenced by current code dissector data.")]
	public object GetReferencedStrings([Description("Maximum entries to return (1-1024). ")] int maximumResults = 256)
	{
		if (maximumResults is < 1 or > MaximumInstructions)
		{
			return ToolExecution.Error("maximumResults must be between 1 and 1024.");
		}

		return Invoke("get_referenced_strings", """
			local values=getDissectCode().getReferencedStrings() or {}; local items={}; local count=0; for address,text in pairs(values) do count=count+1; if #items<a[1] then items[#items+1]={address=string.format('0x%X',address),text=text} end end; return {count=count,truncated=count>a[1],strings=items}
			""", maximumResults);
	}

	[McpServerTool(Name = "get_referenced_functions"), Description("List bounded function addresses referenced by the current code dissector data.")]
	public object GetReferencedFunctions([Description("Maximum functions to return (1-1024).")] int maximumResults = 256)
	{
		if (maximumResults is < 1 or > MaximumInstructions)
		{
			return ToolExecution.Error("maximumResults must be between 1 and 1024.");
		}

		return Invoke("get_referenced_functions", "local source=getDissectCode().getReferencedFunctions() or {}; local items={}; for i=1,math.min(#source,a[1]) do items[i]=string.format('0x%X',source[i]) end; return {count=#source,truncated=#source>a[1],functions=items}", maximumResults);
	}

	[McpServerTool(Name = "clear_code_analysis"), Description("Clear Cheat Engine's current code-dissection data and references.")]
	public object ClearCodeAnalysis() => Invoke("clear_code_analysis", "getDissectCode().clear(); return {cleared=true}");

	[McpServerTool(Name = "add_code_reference"), Description("Add a persistent reference to Cheat Engine's code-dissection data.")]
	public object AddCodeReference([Description("Source instruction address or symbol.")] string source,
		[Description("Referenced address or symbol.")] string destination,
		[Description("call, jump, conditional, or memory.")] string type,
		[Description("For memory references, record the target as a string.")] bool isString = false)
	{
		if (type is not ("call" or "jump" or "conditional" or "memory"))
		{
			return ToolExecution.Error("Unknown reference type.");
		}

		return Invoke("add_code_reference", "local kinds={call=jtCall,jump=jtUnconditional,conditional=jtConditional,memory=jtMemory}; local source=getAddress(a[1]); local destination=getAddress(a[2]); getDissectCode().addReference(source,destination,kinds[a[3]],a[4]); return {source=string.format('0x%X',source),destination=string.format('0x%X',destination)}", source, destination, type, isString);
	}

	[McpServerTool(Name = "delete_code_reference"), Description("Remove one source-to-destination code reference.")]
	public object DeleteCodeReference([Description("Source instruction address or symbol.")] string source,
		[Description("Referenced address or symbol.")] string destination) =>
		Invoke("delete_code_reference", "getDissectCode().deleteReference(getAddress(a[1]),getAddress(a[2])); return {removed=true}", source, destination);

	private object Invoke(string operation, string body, params object?[] arguments) => LuaToolRuntime.Invoke(_client, operation, body, arguments);
}
