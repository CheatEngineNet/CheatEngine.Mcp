using System.ComponentModel;

using CheatEngine.Client;

using ModelContextProtocol.Server;

namespace CheatEngine.Mcp.Tools;

/// <summary>Exposes bounded memory operations that CheatEngine.Client does not yet model.</summary>
[McpServerToolType]
public sealed class LuaMemoryTool
{
	private const long MaximumRangeBytes = 1024 * 1024;
	private readonly ICheatEngineClient _client;

	public LuaMemoryTool(ICheatEngineClient client)
	{
		ArgumentNullException.ThrowIfNull(client);
		_client = client;
	}

	[McpServerTool(Name = "set_memory_protection")]
	[Description("Set target range read, write, and execute protection through Cheat Engine Lua.")]
	public object SetMemoryProtection([Description("Target address expression or number.")] string address,
		[Description("Positive range size in bytes, up to 1048576.")]
		long size,
		[Description("Allow reads.")] bool read = true,
		[Description("Allow writes.")] bool write = false,
		[Description("Allow execution.")] bool execute = false)
	{
		if (!IsBoundedRange(size))
		{
			return ToolExecution.Error("size must be between 1 and 1048576 bytes.");
		}

		return LuaToolRuntime.Invoke(_client, "setMemoryProtection",
			"local resolved = getAddressSafe(a[1]); assert(resolved ~= nil, 'Address could not be resolved.'); setMemoryProtection(resolved, a[2], { r = a[3], w = a[4], x = a[5] }); local actual = getMemoryProtection(resolved); assert(actual ~= nil, 'getMemoryProtection returned no result.'); local applied = actual.r == a[3] and actual.w == a[4] and actual.x == a[5]; return { address = string.format('0x%X', resolved), size = a[2], read = actual.r, write = actual.w, execute = actual.x, applied = applied }",
			address, size, read, write, execute);
	}

	[McpServerTool(Name = "get_memory_protection")]
	[Description("Read the target memory protection flags reported by Cheat Engine Lua.")]
	public object GetMemoryProtection([Description("Target address expression or number.")] string address)
	{
		return LuaToolRuntime.Invoke(_client, "getMemoryProtection",
			"local resolved = getAddressSafe(a[1]); assert(resolved ~= nil, 'Address could not be resolved.'); local actual = getMemoryProtection(resolved); if actual == nil then return { address = string.format('0x%X', resolved), found = false } end; return { address = string.format('0x%X', resolved), found = true, read = actual.r, write = actual.w, execute = actual.x }",
			address);
	}

	[McpServerTool(Name = "full_access_memory")]
	[Description("Make a bounded target range writable and executable through Cheat Engine Lua.")]
	public object FullAccessMemory([Description("Target address expression or number.")] string address,
		[Description("Positive range size in bytes, up to 1048576.")]
		long size)
	{
		if (!IsBoundedRange(size))
		{
			return ToolExecution.Error("size must be between 1 and 1048576 bytes.");
		}

		return LuaToolRuntime.Invoke(_client, "fullAccess",
			"local resolved = getAddressSafe(a[1]); assert(resolved ~= nil, 'Address could not be resolved.'); fullAccess(resolved, a[2]); return { address = string.format('0x%X', resolved), size = a[2] }",
			address, size);
	}

	[McpServerTool(Name = "copy_memory")]
	[Description("Copy a bounded memory range using Cheat Engine's target or local copy route.")]
	public object CopyMemory([Description("Source address expression or number.")] string sourceAddress,
		[Description("Positive byte count, up to 1048576.")]
		long size,
		[Description(
			"Destination address expression or number. Allocate a Client-owned destination first when needed.")]
		string destinationAddress,
		[Description("0 target-to-target, 1 target-to-CE, 2 CE-to-target, or 3 CE-to-CE.")]
		int method = 0)
	{
		if (!IsBoundedRange(size))
		{
			return ToolExecution.Error("size must be between 1 and 1048576 bytes.");
		}

		if (string.IsNullOrWhiteSpace(destinationAddress))
		{
			return ToolExecution.Error("destinationAddress is required.");
		}

		if (method is < 0 or > 3)
		{
			return ToolExecution.Error("method must be between 0 and 3.");
		}

		return LuaToolRuntime.Invoke(_client, "copyMemory",
			"local result = copyMemory(a[1], a[2], a[3], a[4]); assert(result ~= nil, 'copyMemory failed'); return { destinationAddress = string.format('0x%X', result), size = a[2] }",
			sourceAddress, size, destinationAddress, method);
	}

	[McpServerTool(Name = "compare_memory")]
	[Description("Compare two bounded memory ranges through Cheat Engine Lua.")]
	public object CompareMemory([Description("First address expression or number.")] string firstAddress,
		[Description("Second address expression or number.")]
		string secondAddress,
		[Description("Positive byte count, up to 1048576.")]
		long size,
		[Description("0 target-to-target, 1 target-to-CE, or 2 CE-to-CE.")]
		int method = 0)
	{
		if (!IsBoundedRange(size))
		{
			return ToolExecution.Error("size must be between 1 and 1048576 bytes.");
		}

		if (method is < 0 or > 2)
		{
			return ToolExecution.Error("method must be between 0 and 2.");
		}

		return LuaToolRuntime.Invoke(_client, "compareMemory",
			"local equal, difference = compareMemory(a[1], a[2], a[3], a[4]); return { equal = equal, firstDifferenceOffset = difference }",
			firstAddress, secondAddress, size, method);
	}

	[McpServerTool(Name = "hash_memory")]
	[Description("Compute Cheat Engine's MD5 hash for a bounded target memory range.")]
	public object HashMemory([Description("Target address expression or number.")] string address,
		[Description("Positive byte count, up to 1048576.")]
		long size)
	{
		if (!IsBoundedRange(size))
		{
			return ToolExecution.Error("size must be between 1 and 1048576 bytes.");
		}

		return LuaToolRuntime.Invoke(_client, "md5memory",
			"local resolved = getAddressSafe(a[1]); assert(resolved ~= nil, 'Address could not be resolved.'); return { address = string.format('0x%X', resolved), md5 = md5memory(resolved, a[2]), size = a[2] }",
			address, size);
	}

	[McpServerTool(Name = "dump_memory")]
	[Description(
		"Write a bounded target-memory range to an explicit file through a temporary Cheat Engine memory stream.")]
	public object DumpMemory([Description("Destination filename, up to 4096 characters.")] string filename,
		[Description("Target address expression or number.")]
		string address,
		[Description("Positive byte count, up to 1048576.")]
		long size)
	{
		if (string.IsNullOrWhiteSpace(filename) || filename.Length > 4096)
		{
			return ToolExecution.Error("filename must contain 1 to 4096 characters.");
		}

		if (!IsBoundedRange(size))
		{
			return ToolExecution.Error("size must be between 1 and 1048576 bytes.");
		}

		return LuaToolRuntime.Invoke(_client, "readBytes",
			"local resolved = getAddressSafe(a[1]); assert(resolved ~= nil, 'Address could not be resolved.'); local stream = createMemoryStream(); local ok, err = pcall(function() stream.write(readBytes(resolved, a[2], true)); stream.saveToFile(a[3]) end); stream.destroy(); assert(ok, err); return { filename = a[3], address = string.format('0x%X', resolved), bytesWritten = a[2] }",
			address, size, filename);
	}

	private static bool IsBoundedRange(long size)
	{
		return size is > 0 and <= MaximumRangeBytes;
	}
}
