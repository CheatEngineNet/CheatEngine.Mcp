using System.Text.Json;

using CheatEngine.Mcp.Core.Contract;
using CheatEngine.Mcp.Core.Values;

namespace CheatEngine.Mcp.Tools.Structures;

/// <summary>Strict bounded version-one Structure Dissect interchange JSON codec.</summary>
internal static class StructureInterchangeCodec
{
	internal const int Version = 1;
	internal const int MaximumBytes = 16 * 1024 * 1024;
	internal const int MaximumLayouts = 128;
	internal const int MaximumFields = 4096;

	internal static void Save(Stream output, StructureInterchangeDocument document, CancellationToken token)
	{
		Validate(document, token);
		using MaximumLengthWriteStream bounded = new(output, MaximumBytes);
		using Utf8JsonWriter writer = new(bounded);
		writer.WriteStartObject();
		writer.WriteNumber("version", Version);
		writer.WriteNumber("pointerWidth", document.PointerWidth);
		WriteStrings(writer, "roots", document.Roots);
		writer.WritePropertyName("layouts");
		writer.WriteStartArray();
		foreach (StructureInterchangeLayout layout in document.Layouts)
		{
			token.ThrowIfCancellationRequested();
			if (layout is null)
			{
				throw new InvalidDataException("a layout is null.");
			}

			writer.WriteStartObject();
			writer.WriteString("name", layout.Name);
			writer.WriteBoolean("internal", layout.Internal);
			writer.WritePropertyName("fields");
			writer.WriteStartArray();
			foreach (StructureInterchangeField field in layout.Fields)
			{
				WriteField(writer, field);
			}
			writer.WriteEndArray();
			writer.WriteEndObject();
		}
		writer.WriteEndArray();
		writer.WriteEndObject();
		writer.Flush();
		token.ThrowIfCancellationRequested();
	}

	internal static StructureInterchangeDocument Load(Stream input, CancellationToken token)
	{
		using JsonDocument json = JsonDocument.Parse(input, new JsonDocumentOptions { AllowTrailingCommas = false, CommentHandling = JsonCommentHandling.Disallow, MaxDepth = 8 });
		token.ThrowIfCancellationRequested();
		StructureInterchangeDocument document = ReadDocument(json.RootElement, token);
		Validate(document, token);
		return document;
	}

	internal static void Validate(StructureInterchangeDocument document, CancellationToken token)
	{
		if (document.Version != Version)
		{
			throw new InvalidDataException("version must be 1.");
		}

		if (document.PointerWidth is not (4 or 8))
		{
			throw new InvalidDataException("pointerWidth must be 4 or 8.");
		}

		if (document.Roots is null || document.Roots.Length is < 1 or > MaximumLayouts)
		{
			throw new InvalidDataException("roots must contain 1 to 128 names.");
		}

		if (document.Layouts is null || document.Layouts.Length is < 1 or > MaximumLayouts)
		{
			throw new InvalidDataException("layouts must contain 1 to 128 definitions.");
		}

		HashSet<string> names = new(StringComparer.Ordinal);
		int total = 0;
		foreach (StructureInterchangeLayout layout in document.Layouts)
		{
			token.ThrowIfCancellationRequested();
			RequireName(layout.Name, "layout name");
			if (!names.Add(layout.Name))
			{
				throw new InvalidDataException($"duplicate layout name '{layout.Name}'.");
			}

			if (layout.Fields is null || layout.Fields.Length > 1024)
			{
				throw new InvalidDataException("a layout has more than 1024 fields.");
			}

			total += layout.Fields.Length;
			if (total > MaximumFields)
			{
				throw new InvalidDataException("layouts contain more than 4096 fields.");
			}
		}
		HashSet<string> roots = new(StringComparer.Ordinal);
		foreach (string root in document.Roots)
		{
			RequireName(root, "root");
			if (!roots.Add(root))
			{
				throw new InvalidDataException($"duplicate root '{root}'.");
			}

			if (!names.Contains(root))
			{
				throw new InvalidDataException($"root '{root}' has no layout.");
			}
		}
		foreach (StructureInterchangeLayout layout in document.Layouts)
		{
			foreach (StructureInterchangeField field in layout.Fields)
			{
				ValidateField(field, names);
			}
		}
	}

	private static void ValidateField(StructureInterchangeField field, HashSet<string> names)
	{
		if (field is null)
		{
			throw new InvalidDataException("a field is null.");
		}

		try
		{
			_ = StructureArguments.Offset(field.Offset, "offset");
			_ = StructureArguments.ElementName(field.Name, "name");
		}
		catch (CheatEngineToolException e) { throw new InvalidDataException(e.Error.Message); }
		if (!Enum.TryParse(field.ValueType, true, out McpValueType type) || !Enum.IsDefined(type))
		{
			throw new InvalidDataException($"unsupported valueType '{field.ValueType}'.");
		}

		if (field.CustomType is not null || field.BitStart is not null || field.BitSize is not null)
		{
			throw new InvalidDataException("custom and binary fields are not representable by version 1 import.");
		}

		StructureDisplay? display = null;
		if (field.Display is not null)
		{
			if (!Enum.TryParse(field.Display, true, out StructureDisplay parsed) || !Enum.IsDefined(parsed))
			{
				throw new InvalidDataException("display is unsupported.");
			}

			display = parsed;
		}
		try
		{
			_ = StructureArguments.Specs([new StructureElementSpec(field.Offset, field.Name, type, display, field.ByteSize, field.ChildStructure, field.ChildStructureStart)], "fields");
		}
		catch (CheatEngineToolException e) { throw new InvalidDataException(e.Error.Message); }
		if (field.ChildStructure is not null && !names.Contains(field.ChildStructure))
		{
			throw new InvalidDataException($"childStructure '{field.ChildStructure}' has no layout.");
		}
	}

	private static StructureInterchangeDocument ReadDocument(JsonElement root, CancellationToken token)
	{
		RequireObject(root, ["version", "pointerWidth", "roots", "layouts"]);
		int version = Number(root, "version");
		int width = Number(root, "pointerWidth");
		string[] roots = Strings(root.GetProperty("roots"), "roots", MaximumLayouts);
		JsonElement layoutsElement = root.GetProperty("layouts");
		if (layoutsElement.ValueKind != JsonValueKind.Array)
		{
			throw new InvalidDataException("layouts must be an array.");
		}

		List<StructureInterchangeLayout> layouts = [];
		int fieldsRemaining = MaximumFields;
		foreach (JsonElement item in layoutsElement.EnumerateArray())
		{
			token.ThrowIfCancellationRequested();
			if (layouts.Count >= MaximumLayouts)
			{
				throw new InvalidDataException("layouts must contain at most 128 definitions.");
			}

			StructureInterchangeLayout layout = ReadLayout(item, fieldsRemaining, token);
			fieldsRemaining -= layout.Fields.Length;
			layouts.Add(layout);
		}
		return new StructureInterchangeDocument(version, width, roots, [.. layouts]);
	}
	private static StructureInterchangeLayout ReadLayout(JsonElement value, int fieldsRemaining, CancellationToken token)
	{
		RequireObject(value, ["name", "internal", "fields"]);
		if (value.GetProperty("internal").ValueKind is not JsonValueKind.True and not JsonValueKind.False)
		{
			throw new InvalidDataException("internal must be boolean.");
		}

		JsonElement fieldsElement = value.GetProperty("fields");
		if (fieldsElement.ValueKind != JsonValueKind.Array)
		{
			throw new InvalidDataException("fields must be an array.");
		}

		List<StructureInterchangeField> fields = [];
		int maximum = Math.Min(1024, fieldsRemaining);
		foreach (JsonElement item in fieldsElement.EnumerateArray())
		{
			token.ThrowIfCancellationRequested();
			if (fields.Count >= maximum)
			{
				throw new InvalidDataException(fieldsRemaining <= 0
					? "layouts contain more than 4096 fields."
					: "a layout has more than 1024 fields.");
			}

			fields.Add(ReadField(item));
		}

		return new(Text(value, "name"), value.GetProperty("internal").GetBoolean(), [.. fields]);
	}
	private static StructureInterchangeField ReadField(JsonElement value)
	{
		RequireObject(value, ["offset", "name", "valueType", "display", "byteSize", "childStructure", "childStructureStart", "customType", "bitStart", "bitSize"]);
		return new(Text(value, "offset"), Text(value, "name"), Text(value, "valueType"), NullableText(value, "display"), NullableInt(value, "byteSize"), NullableText(value, "childStructure"), NullableText(value, "childStructureStart"), NullableText(value, "customType"), NullableInt(value, "bitStart"), NullableInt(value, "bitSize"));
	}
	private static void RequireObject(JsonElement value, string[] fields)
	{
		if (value.ValueKind != JsonValueKind.Object)
		{
			throw new InvalidDataException("value must be an object.");
		}

		HashSet<string> seen = [];
		foreach (JsonProperty property in value.EnumerateObject())
		{
			if (!seen.Add(property.Name) || !fields.Contains(property.Name, StringComparer.Ordinal))
			{
				throw new InvalidDataException($"unknown or duplicate property '{property.Name}'.");
			}
		}

		foreach (string field in fields)
		{
			if (!seen.Contains(field))
			{
				throw new InvalidDataException($"missing property '{field}'.");
			}
		}
	}
	private static int Number(JsonElement value, string name)
	{
		JsonElement item = value.GetProperty(name);
		return item.ValueKind is JsonValueKind.Number && item.TryGetInt32(out int result)
			? result
			: throw new InvalidDataException($"{name} must be an integer.");
	}
	private static string Text(JsonElement value, string name) => value.GetProperty(name).ValueKind == JsonValueKind.String ? value.GetProperty(name).GetString()! : throw new InvalidDataException($"{name} must be a string.");
	private static string? NullableText(JsonElement value, string name) => value.GetProperty(name).ValueKind switch { JsonValueKind.Null => null, JsonValueKind.String => value.GetProperty(name).GetString(), _ => throw new InvalidDataException($"{name} must be a string or null.") };
	private static int? NullableInt(JsonElement value, string name) => value.GetProperty(name).ValueKind switch { JsonValueKind.Null => null, JsonValueKind.Number when value.GetProperty(name).TryGetInt32(out int n) => n, _ => throw new InvalidDataException($"{name} must be an integer or null.") };
	private static string[] Strings(JsonElement value, string name, int maximum)
	{
		if (value.ValueKind != JsonValueKind.Array)
		{
			throw new InvalidDataException($"{name} must be an array.");
		}

		List<string> values = [];
		foreach (JsonElement item in value.EnumerateArray())
		{
			if (values.Count >= maximum)
			{
				throw new InvalidDataException($"{name} must contain at most {maximum} names.");
			}

			if (item.ValueKind != JsonValueKind.String)
			{
				throw new InvalidDataException($"{name} must contain strings.");
			}

			values.Add(item.GetString()!);
		}

		return [.. values];
	}
	private static void RequireName(string? value, string kind)
	{
		if (string.IsNullOrWhiteSpace(value) || value.Length > StructureArguments.MaxNameLength)
		{
			throw new InvalidDataException($"{kind} must contain 1 to 256 characters.");
		}
	}
	private static void WriteStrings(Utf8JsonWriter writer, string name, string[] values)
	{
		writer.WritePropertyName(name);
		writer.WriteStartArray();
		foreach (string value in values)
		{
			writer.WriteStringValue(value);
		}

		writer.WriteEndArray();
	}
	private static void WriteField(Utf8JsonWriter w, StructureInterchangeField f)
	{
		w.WriteStartObject();
		w.WriteString("offset", f.Offset);
		w.WriteString("name", f.Name);
		w.WriteString("valueType", f.ValueType);
		WriteNullable(w, "display", f.Display);
		WriteNullable(w, "byteSize", f.ByteSize);
		WriteNullable(w, "childStructure", f.ChildStructure);
		WriteNullable(w, "childStructureStart", f.ChildStructureStart);
		WriteNullable(w, "customType", f.CustomType);
		WriteNullable(w, "bitStart", f.BitStart);
		WriteNullable(w, "bitSize", f.BitSize);
		w.WriteEndObject();
	}
	private static void WriteNullable(Utf8JsonWriter w, string n, string? v)
	{
		if (v is null)
		{
			w.WriteNull(n);
		}
		else
		{
			w.WriteString(n, v);
		}
	}
	private static void WriteNullable(Utf8JsonWriter w, string n, int? v)
	{
		if (v is null)
		{
			w.WriteNull(n);
		}
		else
		{
			w.WriteNumber(n, v.Value);
		}
	}

	private sealed class MaximumLengthWriteStream(Stream output, int maximum) : Stream
	{
		private int _written;
		public override bool CanRead => false;
		public override bool CanSeek => false;
		public override bool CanWrite => true;
		public override long Length => throw new NotSupportedException();
		public override long Position
		{
			get => _written; set => throw new NotSupportedException();
		}
		public override void Flush() => output.Flush();
		public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
		public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
		public override void SetLength(long value) => throw new NotSupportedException();
		public override void Write(byte[] buffer, int offset, int count)
		{
			Ensure(count);
			output.Write(buffer, offset, count);
		}
		public override void Write(ReadOnlySpan<byte> buffer)
		{
			Ensure(buffer.Length);
			output.Write(buffer);
		}
		private void Ensure(int count)
		{
			if (count < 0 || _written > maximum - count)
			{
				throw new InvalidDataException($"Structure interchange JSON exceeds the {MaximumBytes}-byte limit.");
			}

			_written += count;
		}
	}
}
