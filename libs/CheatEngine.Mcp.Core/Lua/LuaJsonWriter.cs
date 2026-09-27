using System.Buffers;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using System.Text.Unicode;

using CheatEngine.SDK.Lua.Calls;
using CheatEngine.SDK.Lua.State;

namespace CheatEngine.Mcp.Core.Lua;

/// <summary>Copies one Lua value from the stack into bounded JSON, then into a source-generated result type.</summary>
/// <remarks>
///     <para>
///         Numbers: an integer becomes a JSON integer (all 64 bits) and a finite float a JSON number; a NaN or infinity is
///         refused, so scripts stringify them. Strings are copied as UTF-8, invalid bytes becoming U+FFFD. Tables: keys
///         exactly <c>1..n</c> make an array, string keys an object, and an empty table <c>[]</c>. A table whose only
///         string
///         key is an integer <c>n</c>, and whose other keys are integers in <c>1..n</c>, is a <c>table.pack</c> result: an
///         array of length <c>n</c> whose holes become <c>null</c>. Mixed or other keys, object keys that are not valid
///         UTF-8,
///         and cycles are refused; so are functions, userdata and threads, unless the caller drops them.
///     </para>
///     <para>
///         A top-level table with an <c>mcp_error</c> field is a declared failure and is returned instead of being copied.
///         The walk uses raw access only (<c>lua_next</c>, <c>lua_rawget</c>, <c>lua_rawgeti</c>), so no metamethod runs,
///         and
///         it restores the stack. Bounds: 4 MiB of strings, 65,536 values, 16 nested tables and 8 MiB of JSON.
///     </para>
/// </remarks>
internal sealed class LuaJsonWriter
{
	internal const int MaximumStringBytes = LuaToolRuntime.MaximumStringBytes;
	internal const int MaximumItems = LuaToolRuntime.MaximumItems;
	internal const int MaximumDepth = 16;
	internal const int MaximumJsonBytes = 8 * 1024 * 1024;
	internal const int MaximumErrorFieldBytes = 4096;
	private const int MaximumErrorTokenBytes = 64;

	private static readonly JsonWriterOptions WriterOptions = new()
	{
		// The buffer is an intermediate copy that is deserialized at once, never embedded in HTML.
		Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
		MaxDepth = MaximumDepth + 1
	};

	private readonly LuaOpaqueValueHandling _opaque;
	private readonly nint[] _path = new nint[MaximumDepth];
	private readonly LuaState _state;
	private readonly Utf8JsonWriter _writer;
	private int _dropped;
	private int _items;
	private int _stringBytes;

	private LuaJsonWriter(LuaState state, Utf8JsonWriter writer, LuaOpaqueValueHandling opaque)
	{
		_state = state;
		_writer = writer;
		_opaque = opaque;
	}

	/// <summary>Copies the value at <paramref name="index" />, or reads the failure its script declared.</summary>
	/// <param name="state">The Lua state of the current protected operation.</param>
	/// <param name="index">The stack index of the value.</param>
	/// <param name="output">Receives the JSON; nothing is written when the script declared an error.</param>
	/// <param name="opaque">How functions, userdata and threads are treated.</param>
	/// <param name="droppedOpaqueCount">The opaque values dropped under <see cref="LuaOpaqueValueHandling.Drop" />.</param>
	/// <returns>The declared <c>mcp_error</c>, or <see langword="null" /> when the JSON was written.</returns>
	/// <exception cref="LuaJsonException">The value breaks the copy contract or exceeds a bound.</exception>
	internal static LuaScriptError? Write(LuaState state, int index, IBufferWriter<byte> output,
		LuaOpaqueValueHandling opaque, out int droppedOpaqueCount)
	{
		ArgumentNullException.ThrowIfNull(output);
		index = state.AbsoluteIndex(index);
		using LuaFrame frame = new(state);
		EnsureStack(state, 8);
		droppedOpaqueCount = 0;
		LuaScriptError? declared = ReadDeclaredError(state, index);
		if (declared is not null)
		{
			return declared;
		}

		using Utf8JsonWriter writer = new(output, WriterOptions);
		LuaJsonWriter copier = new(state, writer, opaque);
		copier.WriteValue(index, 0);
		writer.Flush();
		droppedOpaqueCount = copier._dropped;
		return null;
	}

	/// <summary>Copies the value at <paramref name="index" /> through a pooled buffer and deserializes it.</summary>
	/// <typeparam name="T">The result type.</typeparam>
	/// <param name="state">The Lua state of the current protected operation.</param>
	/// <param name="index">The stack index of the value.</param>
	/// <param name="typeInfo">The source-generated metadata of <typeparamref name="T" />.</param>
	/// <param name="buffers">The owner's buffer pool.</param>
	/// <param name="opaque">How functions, userdata and threads are treated.</param>
	/// <returns>The result, or the failure the script declared.</returns>
	/// <exception cref="LuaJsonException">The value breaks the copy contract, exceeds a bound or does not match.</exception>
	internal static LuaJsonResult<T> Read<T>(LuaState state, int index, JsonTypeInfo<T> typeInfo,
		LuaJsonBufferPool buffers, LuaOpaqueValueHandling opaque = LuaOpaqueValueHandling.Reject)
	{
		ArgumentNullException.ThrowIfNull(typeInfo);
		ArgumentNullException.ThrowIfNull(buffers);
		ArrayBufferWriter<byte> buffer = buffers.Rent();
		try
		{
			LuaScriptError? declared = Write(state, index, buffer, opaque, out int dropped);
			return declared is null
				? new LuaJsonResult<T>(Deserialize(buffer.WrittenSpan, typeInfo), null, dropped)
				: new LuaJsonResult<T>(default, declared, 0);
		}
		finally
		{
			buffers.Return(buffer);
		}
	}

	/// <summary>Deserializes a copied Lua result with source-generated metadata only.</summary>
	/// <typeparam name="T">The result type.</typeparam>
	/// <param name="json">The JSON written by <see cref="Write" />.</param>
	/// <param name="typeInfo">The source-generated metadata of <typeparamref name="T" />.</param>
	/// <returns>The non-null result.</returns>
	/// <exception cref="LuaJsonException">The JSON is <c>null</c> or does not match <typeparamref name="T" />.</exception>
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

	private static LuaScriptError? ReadDeclaredError(LuaState state, int index)
	{
		if (state.TypeOf(index) != LuaType.Table)
		{
			return null;
		}

		state.PushString("mcp_error"u8);
		LuaType type = state.RawGet(index);
		if (type == LuaType.Nil)
		{
			state.Pop(1);
			return null;
		}

		if (type != LuaType.Table)
		{
			throw Contract("mcp_error must be a table with string kind and message fields.");
		}

		int error = state.Top;
		string kind = ReadErrorToken(state, error, "kind"u8, true)!;
		string message = ReadErrorText(state, error, "message"u8, true)!;
		string? hostEffect = ReadErrorToken(state, error, "hostEffect"u8, false);
		string? hint = ReadErrorText(state, error, "hint"u8, false);
		state.Pop(1);
		return new LuaScriptError(kind, message, hostEffect, hint);
	}

	private static string? ReadErrorToken(LuaState state, int error, ReadOnlySpan<byte> key, bool required)
	{
		string? token = ReadErrorText(state, error, key, required);
		if (token is not null && (token.Length > MaximumErrorTokenBytes || !IsSnakeCase(token)))
		{
			throw Contract($"mcp_error.{Encoding.UTF8.GetString(key)} must be a snake_case identifier.");
		}

		return token;
	}

	private static string? ReadErrorText(LuaState state, int error, ReadOnlySpan<byte> key, bool required)
	{
		state.PushString(key);
		LuaType type = state.RawGet(error);
		try
		{
			if (type == LuaType.Nil && !required)
			{
				return null;
			}

			if (type != LuaType.String || !state.TryReadUtf8(-1, out ReadOnlySpan<byte> bytes) || bytes.IsEmpty)
			{
				throw Contract($"mcp_error.{Encoding.UTF8.GetString(key)} must be a non-empty string.");
			}

			return DecodeBounded(bytes, MaximumErrorFieldBytes);
		}
		finally
		{
			state.Pop(1);
		}
	}

	private static string DecodeBounded(ReadOnlySpan<byte> bytes, int maximum)
	{
		if (bytes.Length > maximum)
		{
			// Cut before a continuation byte so that only the dropped tail is lost, not the last character.
			int end = maximum;
			while (end > 0 && (bytes[end] & 0xC0) == 0x80)
			{
				end--;
			}

			bytes = bytes[..end];
		}

		return Encoding.UTF8.GetString(bytes);
	}

	private static bool IsSnakeCase(string token)
	{
		if (!char.IsAsciiLetterLower(token[0]))
		{
			return false;
		}

		foreach (char character in token)
		{
			if (!char.IsAsciiLetterLower(character) && !char.IsAsciiDigit(character) && character != '_')
			{
				return false;
			}
		}

		return true;
	}

	private static void EnsureStack(LuaState state, int slots)
	{
		if (!state.TryEnsureStack(slots))
		{
			throw new InvalidOperationException("Lua could not reserve stack space for bounded result copying.");
		}
	}

	private static bool IsOpaque(LuaType type)
	{
		return type is LuaType.Function or LuaType.Userdata or LuaType.LightUserdata or LuaType.Thread;
	}

	private static LuaJsonException Contract(string message)
	{
		return new LuaJsonException(LuaJsonViolation.Contract, message);
	}

	private static LuaJsonException Limit(string message)
	{
		return new LuaJsonException(LuaJsonViolation.Limit, message + " The script has already run.");
	}

	private void WriteValue(int index, int depth)
	{
		CountItem();
		LuaType type = _state.TypeOf(index);
		switch (type)
		{
			case LuaType.Nil:
				_writer.WriteNullValue();
				break;
			case LuaType.Boolean:
				_writer.WriteBooleanValue(_state.ToBoolean(index));
				break;
			case LuaType.Number:
				WriteNumber(index);
				break;
			case LuaType.String:
				WriteString(index);
				break;
			case LuaType.Table:
				WriteTable(index, depth + 1);
				break;
			case LuaType.None:
				throw Contract("No Lua value exists at the requested stack index.");
			default:
				if (_opaque == LuaOpaqueValueHandling.Reject)
				{
					string typeName = Encoding.ASCII.GetString(_state.TypeName(type));
					throw Contract($"Lua returned a {typeName}; the script must copy its documented fields.");
				}

				_dropped++;
				_writer.WriteNullValue();
				break;
		}

		if (_writer.BytesCommitted + _writer.BytesPending > MaximumJsonBytes)
		{
			throw Limit($"The Lua result exceeds {MaximumJsonBytes} bytes of JSON.");
		}
	}

	private void WriteNumber(int index)
	{
		if (_state.IsInteger(index) && _state.TryReadInteger(index, out long integer))
		{
			_writer.WriteNumberValue(integer);
			return;
		}

		if (_state.TryReadNumber(index, out double number) && double.IsFinite(number))
		{
			_writer.WriteNumberValue(number);
			return;
		}

		throw Contract("Lua returned NaN or an infinity; the script must convert non-finite numbers to strings.");
	}

	private void WriteString(int index)
	{
		ReadOnlySpan<byte> bytes = ReadString(index);
		if (Utf8.IsValid(bytes))
		{
			_writer.WriteStringValue(bytes);
		}
		else
		{
			_writer.WriteStringValue(Encoding.UTF8.GetString(bytes));
		}
	}

	private ReadOnlySpan<byte> ReadString(int index)
	{
		if (!_state.TryReadUtf8(index, out ReadOnlySpan<byte> bytes))
		{
			throw Contract("A Lua string could not be read.");
		}

		_stringBytes = checked(_stringBytes + bytes.Length);
		if (_stringBytes > MaximumStringBytes)
		{
			throw Limit($"The Lua result exceeds {MaximumStringBytes} bytes of strings.");
		}

		return bytes;
	}

	private void WriteTable(int index, int depth)
	{
		if (depth > MaximumDepth)
		{
			throw Limit($"The Lua result nests more than {MaximumDepth} tables.");
		}

		index = _state.AbsoluteIndex(index);
		nint identity = _state.ToPointer(index);
		if (_path.AsSpan(0, depth - 1).Contains(identity))
		{
			throw Contract("Lua returned a cyclic table.");
		}

		_path[depth - 1] = identity;
		EnsureStack(_state, 4);
		long arrayLength = InspectTable(index);
		if (arrayLength >= 0)
		{
			WriteArray(index, arrayLength, depth);
		}
		else
		{
			WriteObject(index, depth);
		}
	}

	/// <summary>Classifies a table from its keys.</summary>
	/// <returns>The array length, or -1 for an object.</returns>
	private long InspectTable(int table)
	{
		int entries = 0;
		int stringKeys = 0;
		int integerKeys = 0;
		long lowest = long.MaxValue;
		long highest = long.MinValue;
		long packedLength = -1;
		_state.PushNil();
		while (Next(table))
		{
			if (++entries > MaximumItems - _items)
			{
				throw Limit($"The Lua result exceeds {MaximumItems} values.");
			}

			switch (_state.TypeOf(-2))
			{
				case LuaType.String:
					stringKeys++;
					bool isLength = _state.TryReadUtf8(-2, out ReadOnlySpan<byte> name) && name.SequenceEqual("n"u8);
					if (isLength && _state.IsInteger(-1) && _state.TryReadInteger(-1, out long length))
					{
						packedLength = length;
					}

					break;
				case LuaType.Number when _state.IsInteger(-2) && _state.TryReadInteger(-2, out long position):
					integerKeys++;
					lowest = Math.Min(lowest, position);
					highest = Math.Max(highest, position);
					break;
				default:
					throw Contract("Lua table keys must be strings or integers.");
			}

			_state.Pop(1);
		}

		if (entries == 0)
		{
			return 0;
		}

		if (integerKeys == entries && lowest == 1 && highest == entries)
		{
			return entries;
		}

		bool packedKeys = integerKeys == 0 || (lowest >= 1 && highest <= packedLength);
		if (stringKeys == 1 && packedLength is >= 0 and <= MaximumItems && packedKeys)
		{
			return packedLength;
		}

		if (integerKeys == 0)
		{
			return -1;
		}

		throw Contract(
			"A Lua table mixes string and integer keys, or its integer keys are not 1..n; return a sequence, " +
			"a table.pack result or named fields.");
	}

	private void WriteArray(int table, long length, int depth)
	{
		_writer.WriteStartArray();
		for (long key = 1; key <= length; key++)
		{
			_state.RawGetIndex(table, key);
			WriteValue(-1, depth);
			_state.Pop(1);
		}

		_writer.WriteEndArray();
	}

	private void WriteObject(int table, int depth)
	{
		_writer.WriteStartObject();
		_state.PushNil();
		while (Next(table))
		{
			if (_opaque == LuaOpaqueValueHandling.Drop && IsOpaque(_state.TypeOf(-1)))
			{
				CountItem();
				_dropped++;
				_state.Pop(1);
				continue;
			}

			ReadOnlySpan<byte> name = ReadString(-2);
			if (!Utf8.IsValid(name))
			{
				throw Contract("A Lua table key is not valid UTF-8.");
			}

			_writer.WritePropertyName(name);
			WriteValue(-1, depth);
			_state.Pop(1);
		}

		_writer.WriteEndObject();
	}

	private bool Next(int table)
	{
		LuaStatus status = _state.TryNext(table, out bool hasNext);
		if (!status.IsOk)
		{
			throw Contract(LuaError.FromStack(_state, status).Message);
		}

		return hasNext;
	}

	private void CountItem()
	{
		if (++_items > MaximumItems)
		{
			throw Limit($"The Lua result exceeds {MaximumItems} values.");
		}
	}
}
