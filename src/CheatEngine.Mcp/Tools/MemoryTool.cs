using System.ComponentModel;
using System.Globalization;

using CheatEngine.Client;
using CheatEngine.Client.Memory;
using CheatEngine.SDK.Engine.Values;

using ModelContextProtocol.Server;

namespace CheatEngine.Mcp.Tools;

[McpServerToolType]
public sealed class MemoryTool
{
	private const int MaximumBytes = 1_048_576;
	private readonly ICheatEngineClient _client;

	public MemoryTool(ICheatEngineClient client)
	{
		ArgumentNullException.ThrowIfNull(client);
		_client = client;
	}

	[McpServerTool(Name = "read_memory"), Description("Read typed target memory through CheatEngine.Client.")]
	public object ReadMemory([Description("A hexadecimal address or Cheat Engine address expression.")] string address,
		[Description("bytes, byte, int16, int32, int64, float, double, or string.")] string dataType,
		[Description("Required byte count for bytes reads.")] int? byteCount = null,
		[Description("Required maximum length for string reads.")] int? maximumLength = null,
		[Description("Read or write UTF-16 strings instead of UTF-8.")] bool wideChar = false)
	{
		return ToolExecution.Run(_client, () =>
		{
			Address target = ToolExecution.Address(_client, address);
			return NormalizeType(dataType) switch
			{
				"bytes" => ReadBytes(target, byteCount),
				"byte" => Success(_client.Memory.ReadPrimitive<byte>(target)),
				"int16" => Success(_client.Memory.ReadPrimitive<short>(target)),
				"int32" => Success(_client.Memory.ReadPrimitive<int>(target)),
				"int64" => Success(_client.Memory.ReadPrimitive<long>(target)),
				"float" => Success(_client.Memory.ReadPrimitive<float>(target)),
				"double" => Success(_client.Memory.ReadPrimitive<double>(target)),
				"string" => ReadString(target, maximumLength, wideChar),
				_ => ToolExecution.Error("dataType must be bytes, byte, int16, int32, int64, float, double, or string.")
			};
		});
	}

	[McpServerTool(Name = "write_memory"), Description("Write typed target memory through CheatEngine.Client.")]
	public object WriteMemory([Description("A hexadecimal address or Cheat Engine address expression.")] string address,
		[Description("bytes, byte, int16, int32, int64, float, double, or string.")] string dataType,
		[Description("The value formatted for the selected data type.")] string value,
		[Description("Explicit maximum encoded length for string writes.")] int? maximumLength = null,
		[Description("Read or write UTF-16 strings instead of UTF-8.")] bool wideChar = false)
	{
		return ToolExecution.Run(_client, () =>
		{
			ArgumentNullException.ThrowIfNull(value);
			Address target = ToolExecution.Address(_client, address);
			switch (NormalizeType(dataType))
			{
				case "bytes":
					byte[] bytes = ParseBytes(value);
					_client.Memory.WriteBytes(new MemoryBytesWriteRequest(target, bytes));
					return Success(bytes);
				case "byte":
					return Write(target, byte.Parse(value, CultureInfo.InvariantCulture));
				case "int16":
					return Write(target, short.Parse(value, CultureInfo.InvariantCulture));
				case "int32":
					return Write(target, int.Parse(value, CultureInfo.InvariantCulture));
				case "int64":
					return Write(target, long.Parse(value, CultureInfo.InvariantCulture));
				case "float":
					return Write(target, float.Parse(value, CultureInfo.InvariantCulture));
				case "double":
					return Write(target, double.Parse(value, CultureInfo.InvariantCulture));
				case "string":
					int limit = maximumLength ?? (wideChar ? value.Length : System.Text.Encoding.UTF8.GetByteCount(value));
					if (limit is < 1 or > MaximumBytes)
					{
						return ToolExecution.Error("maximumLength must be between 1 and 1048576.");
					}

					_client.Memory.WriteString(new MemoryStringWriteRequest(target, value, limit,
						wideChar ? MemoryStringEncoding.Utf16 : MemoryStringEncoding.Utf8));
					return Success(value);
				default:
					return ToolExecution.Error("dataType must be bytes, byte, int16, int32, int64, float, double, or string.");
			}
		});
	}

	private object ReadBytes(Address address, int? count)
	{
		if (count is not { } length || length is < 1 or > MaximumBytes)
		{
			return ToolExecution.Error("byteCount must be between 1 and 1048576.");
		}

		return Success(_client.Memory.ReadBytes(new MemoryBytesReadRequest(address, length)).ToArray());
	}

	private object ReadString(Address address, int? maximumLength, bool wideChar)
	{
		if (maximumLength is not { } length || length is < 1 or > MaximumBytes)
		{
			return ToolExecution.Error("maximumLength must be between 1 and 1048576.");
		}

		string value = _client.Memory.ReadString(new MemoryStringReadRequest(address, length,
			wideChar ? MemoryStringEncoding.Utf16 : MemoryStringEncoding.Utf8));
		return Success(value);
	}

	private object Write<T>(Address address, T value) where T : unmanaged
	{
		_client.Memory.WritePrimitive(address, value);
		return Success(value);
	}

	private static object Success<T>(T value) => new { success = true, value };

	private static string NormalizeType(string? dataType) => dataType?.ToLowerInvariant() switch
	{
		"int" or "integer" => "int32",
		"long" or "qword" => "int64",
		"short" => "int16",
		_ => dataType?.ToLowerInvariant() ?? string.Empty
	};

	private static byte[] ParseBytes(string value)
	{
		string[] tokens = value.Split([' ', ','], StringSplitOptions.RemoveEmptyEntries);
		if (tokens.Length is < 1 or > MaximumBytes)
		{
			throw new ArgumentException("bytes must contain 1 to 1048576 hexadecimal bytes.");
		}

		return tokens.Select(token => byte.Parse(token, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture)).ToArray();
	}
}
