using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace CheatEngine.Mcp.Core.Lua;

/// <summary>Deserializes a bounded JSON copy of a Lua result with source-generated metadata.</summary>
internal static class LuaJsonWriter
{
	internal static T Deserialize<T>(ReadOnlySpan<byte> json, JsonTypeInfo<T> typeInfo)
	{
		ArgumentNullException.ThrowIfNull(typeInfo);
		T? value;
		try
		{
			value = JsonSerializer.Deserialize(json, typeInfo);
		}
		catch (JsonException exception)
		{
			throw new LuaJsonException(LuaJsonViolation.Contract,
				$"The Lua result does not match {typeInfo.Type.Name}: {exception.Message}", exception);
		}

		if (value is null)
		{
			throw new LuaJsonException(LuaJsonViolation.Contract,
				$"The Lua result is nil where {typeInfo.Type.Name} was expected.");
		}

		return value;
	}
}
