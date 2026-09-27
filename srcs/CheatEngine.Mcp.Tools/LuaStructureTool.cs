using System.ComponentModel;

using CheatEngine.Client;

using ModelContextProtocol.Server;

namespace CheatEngine.Mcp.Tools;

/// <summary>Provides bounded Structure Dissect operations not yet represented by CheatEngine.Client.</summary>
[McpServerToolType]
public sealed class LuaStructureTool
{
	private const int MaximumElements = 1024;
	private const int MaximumNameLength = 256;
	private readonly ICheatEngineClient _client;

	public LuaStructureTool(ICheatEngineClient client)
	{
		_client = client;
	}

	[McpServerTool(Name = "list_structures")]
	[Description("List up to 1024 global Cheat Engine Structure Dissect definitions.")]
	public object ListStructures([Description("Maximum structures to return (1-1024). ")] int maximumResults = 256)
	{
		if (maximumResults is < 1 or > MaximumElements)
		{
			return ToolExecution.Error("maximumResults must be between 1 and 1024.");
		}

		return Invoke("list_structures", """
		                                 local count=getStructureCount()
		                                 local limit=math.min(count,a[1])
		                                 local items={}
		                                 for i=0,limit-1 do local s=getStructure(i); items[#items+1]={index=i,name=s.Name,size=s.Size,count=s.Count,internal=s.Internal} end
		                                 return {count=count,truncated=count>limit,structures=items}
		                                 """, maximumResults);
	}

	[McpServerTool(Name = "get_structure")]
	[Description("Get one global or internal Structure Dissect definition by its case-sensitive name.")]
	public object GetStructure([Description("Case-sensitive structure name (1-256 characters). ")] string name)
	{
		if (!ValidName(name))
		{
			return ToolExecution.Error("name must contain 1 to 256 characters.");
		}

		return Invoke("get_structure", """
		                               local s=getStructure(a[1]); if s==nil then error('Structure not found') end
		                               local elements={}; local limit=math.min(s.Count,1024)
		                               for i=0,limit-1 do local e=s.getElement(i); elements[#elements+1]={index=i,offset=e.Offset,name=e.Name,vartype=e.Vartype,bytesize=e.Bytesize,childClassName=e.ChildClassName,childStructStart=e.ChildStructStart} end
		                               return {name=s.Name,size=s.Size,count=s.Count,internal=s.Internal,truncated=s.Count>limit,elements=elements}
		                               """, name);
	}

	[McpServerTool(Name = "create_structure")]
	[Description(
		"Create a persistent global Structure Dissect definition. Delete it explicitly with delete_structure.")]
	public object CreateStructure([Description("Unique structure name (1-256 characters). ")] string name,
		[Description("Hide the definition from normal Structure Dissect selection. ")]
		bool isInternal = false)
	{
		if (!ValidName(name))
		{
			return ToolExecution.Error("name must contain 1 to 256 characters.");
		}

		return Invoke("create_structure", """
		                                  if getStructure(a[1])~=nil then error('Structure already exists') end
		                                  local s=createStructure(a[1]); local ok,err=pcall(function() s.Internal=a[2]; s.addToGlobalStructureList() end); if not ok then s.destroy(); error(err) end
		                                  return {name=s.Name,size=s.Size,count=s.Count,internal=s.Internal}
		                                  """, name, isInternal);
	}

	[McpServerTool(Name = "delete_structure")]
	[Description("Remove a persistent global Structure Dissect definition by name.")]
	public object DeleteStructure([Description("Case-sensitive structure name (1-256 characters). ")] string name)
	{
		if (!ValidName(name))
		{
			return ToolExecution.Error("name must contain 1 to 256 characters.");
		}

		return Invoke("delete_structure", """
		                                  local s=getStructure(a[1]); if s==nil then error('Structure not found') end
		                                  s.destroy(); return {name=a[1],deleted=true}
		                                  """, name);
	}

	[McpServerTool(Name = "add_structure_element")]
	[Description("Append one Structure Dissect element with validated primitive fields.")]
	public object AddStructureElement([Description("Case-sensitive structure name. ")] string structureName,
		[Description("Byte offset from the structure base. ")]
		long offset,
		[Description("Element display name (up to 256 characters). ")]
		string name,
		[Description("Cheat Engine variable-type integer. ")]
		int variableType,
		[Description("Optional byte size for strings or byte arrays (0-1048576). ")]
		int byteSize = 0,
		[Description("Optional child class-name hint (up to 256 characters). ")]
		string? childClassName = null)
	{
		if (!ValidName(structureName) || !ValidName(name) ||
		    (childClassName is not null && childClassName.Length > MaximumNameLength))
		{
			return ToolExecution.Error("Structure and element names must contain at most 256 characters.");
		}

		if (variableType is (< 0 or > 9) and not 12)
		{
			return ToolExecution.Error("variableType must be a primitive CE type (0-9) or pointer (12).");
		}

		if (offset is < 0 or > int.MaxValue || byteSize is < 0 or > 1_048_576)
		{
			return ToolExecution.Error("offset must be non-negative and byteSize must be between 0 and 1048576.");
		}

		return Invoke("add_structure_element", """
		                                       local s=getStructure(a[1]); if s==nil then error('Structure not found') end
		                                       if s.Count>=1024 then error('A structure cannot contain more than 1024 MCP-managed elements') end
		                                       local e=s.addElement(); local ok,err=pcall(function() e.Offset=a[2]; e.Name=a[3]; e.Vartype=a[4]; if a[5]>0 then e.Bytesize=a[5] end; if a[6]~=nil then e.ChildClassName=a[6] end end); if not ok then e.destroy(); error(err) end
		                                       return {offset=e.Offset,name=e.Name,vartype=e.Vartype,bytesize=e.Bytesize}
		                                       """, structureName, offset, name, variableType, byteSize,
			childClassName);
	}

	[McpServerTool(Name = "update_structure_element")]
	[Description("Change selected fields of one Structure Dissect element by zero-based index.")]
	public object UpdateStructureElement([Description("Case-sensitive structure name. ")] string structureName,
		[Description("Zero-based element index. ")]
		int index,
		[Description("Replacement offset, or null to retain it. ")]
		long? offset = null,
		[Description("Replacement element name, or null to retain it. ")]
		string? name = null,
		[Description("Replacement variable-type integer, or null to retain it. ")]
		int? variableType = null,
		[Description("Replacement byte size (0-1048576), or null to retain it. ")]
		int? byteSize = null)
	{
		if (!ValidName(structureName) || index < 0 || offset is < 0 or > int.MaxValue ||
		    (name is not null && name.Length > MaximumNameLength) || byteSize is < 0 or > 1_048_576)
		{
			return ToolExecution.Error("Invalid structure element update.");
		}

		if (variableType is { } type && type is not (>= 0 and <= 9) and not 12)
		{
			return ToolExecution.Error("variableType must be a primitive CE type (0-9) or pointer (12).");
		}

		if (offset is null && name is null && variableType is null && byteSize is null)
		{
			return ToolExecution.Error("At least one replacement field is required.");
		}

		return Invoke("update_structure_element", """
		                                          local s=getStructure(a[1]); if s==nil then error('Structure not found') end; if a[2]>=s.Count then error('Element index is out of range') end; local e=s.getElement(a[2]); if e==nil then error('Element not found') end
		                                          if a.n>=3 and a[3]~=nil then e.Offset=a[3] end; if a.n>=4 and a[4]~=nil then e.Name=a[4] end; if a.n>=5 and a[5]~=nil then e.Vartype=a[5] end; if a.n>=6 and a[6]~=nil then e.Bytesize=a[6] end
		                                          return {index=a[2],offset=e.Offset,name=e.Name,vartype=e.Vartype,bytesize=e.Bytesize}
		                                          """, structureName, index, offset, name, variableType, byteSize);
	}

	[McpServerTool(Name = "remove_structure_element")]
	[Description("Remove one Structure Dissect element by zero-based index.")]
	public object RemoveStructureElement([Description("Case-sensitive structure name. ")] string structureName,
		[Description("Zero-based element index. ")]
		int index)
	{
		if (!ValidName(structureName) || index < 0)
		{
			return ToolExecution.Error("structureName and a non-negative index are required.");
		}

		return Invoke("remove_structure_element", """
		                                          local s=getStructure(a[1]); if s==nil then error('Structure not found') end; if a[2]>=s.Count then error('Element index is out of range') end; local e=s.getElement(a[2]); if e==nil then error('Element not found') end
		                                          e.destroy(); return {index=a[2],deleted=true,count=s.Count}
		                                          """, structureName, index);
	}

	[McpServerTool(Name = "autoguess_structure")]
	[Description("Ask Cheat Engine to infer Structure Dissect fields from a bounded target range.")]
	public object AutoGuessStructure([Description("Case-sensitive structure name. ")] string structureName,
		[Description("Target base address or symbol expression. ")]
		string baseAddress,
		[Description("Starting byte offset. ")]
		int offset = 0,
		[Description("Maximum bytes to infer (1-1048576). ")]
		int size = 4096)
	{
		if (!ValidName(structureName) || string.IsNullOrWhiteSpace(baseAddress) || offset is < 0 or > int.MaxValue ||
		    size is < 1 or > 1_048_576)
		{
			return ToolExecution.Error("Invalid structure auto-guess input.");
		}

		return Invoke("autoguess_structure", """
		                                     local s=getStructure(a[1]); if s==nil then error('Structure not found') end; local address=getAddressSafe(a[2]); if address==nil then error('Address could not be resolved') end
		                                     s.autoGuess(address,a[3],a[4]); return {name=s.Name,size=s.Size,count=s.Count}
		                                     """, structureName, baseAddress, offset, size);
	}

	[McpServerTool(Name = "get_structure_element_value")]
	[Description("Read one Structure Dissect element value from a target base address.")]
	public object GetStructureElementValue([Description("Case-sensitive structure name. ")] string structureName,
		[Description("Zero-based element index. ")]
		int index,
		[Description("Target base address or symbol expression. ")]
		string baseAddress)
	{
		if (!ValidName(structureName) || index < 0 || string.IsNullOrWhiteSpace(baseAddress))
		{
			return ToolExecution.Error("Invalid structure value input.");
		}

		return Invoke("get_structure_element_value", """
		                                             local s=getStructure(a[1]); if s==nil then error('Structure not found') end; if a[2]>=s.Count then error('Element index is out of range') end; local e=s.getElement(a[2]); if e==nil then error('Element not found') end; local address=getAddressSafe(a[3]); if address==nil then error('Address could not be resolved') end
		                                             return {index=a[2],offset=e.Offset,name=e.Name,value=e.getValueFromBase(address)}
		                                             """, structureName, index, baseAddress);
	}

	[McpServerTool(Name = "set_structure_element_value")]
	[Description("Write a value interpreted by a Structure Dissect element into the selected target.")]
	public object SetStructureElementValue([Description("Case-sensitive structure name.")] string structureName,
		[Description("Zero-based element index.")]
		int index,
		[Description("Target structure base address or symbol.")]
		string baseAddress,
		[Description("Value in Cheat Engine's format for the element type.")]
		string value)
	{
		if (!ValidName(structureName) || index < 0 || string.IsNullOrWhiteSpace(value) || value.Length > 65536)
		{
			return ToolExecution.Error("Invalid structure value input.");
		}

		return Invoke("set_structure_element_value",
			"local s=getStructure(a[1]); assert(s~=nil,'Structure not found'); assert(a[2]<s.Count,'Element index is out of range'); local e=s.getElement(a[2]); local address=getAddress(a[3]); e.setValueFromBase(address,a[4]); return {index=a[2],value=e.getValueFromBase(address)}",
			structureName, index, baseAddress, value);
	}

	[McpServerTool(Name = "fill_structure_from_dotnet")]
	[Description("Fill an existing Structure Dissect definition using the layout of a target .NET object.")]
	public object FillStructureFromDotNet([Description("Case-sensitive structure name.")] string structureName,
		[Description("Target .NET object address or symbol.")]
		string address,
		[Description("Rename the structure to the .NET class name.")]
		bool changeName = false)
	{
		if (!ValidName(structureName))
		{
			return ToolExecution.Error("Invalid structure name.");
		}

		return Invoke("fill_structure_from_dotnet",
			"local s=getStructure(a[1]); assert(s~=nil,'Structure not found'); s.fillFromDotNetAddress(getAddress(a[2]),a[3]); return {name=s.Name,size=s.Size,count=s.Count}",
			structureName, address, changeName);
	}

	private object Invoke(string operation, string body, params object?[] arguments)
	{
		return LuaToolRuntime.Invoke(_client, operation, body, arguments);
	}

	private static bool ValidName(string? value)
	{
		return !string.IsNullOrWhiteSpace(value) && value.Length <= MaximumNameLength;
	}
}
