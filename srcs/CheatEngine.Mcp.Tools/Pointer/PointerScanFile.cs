using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.Json;

namespace CheatEngine.Mcp.Tools.Pointer;

/// <summary>Reads and writes bounded, versioned JSON pointer-scan snapshots.</summary>
/// <remarks>
/// The codec deliberately owns streams instead of paths: tool transports decide how a stream is opened and whether a
/// write is made atomic. It uses <see cref="Utf8JsonWriter" /> and <see cref="JsonDocument" /> directly so Native AOT
/// never needs reflection-based <see cref="JsonSerializer" /> metadata.
/// </remarks>
internal static class PointerScanFile
{
	internal const int MaximumBytes = 32 * 1024 * 1024;
	/// <summary>The greatest permitted encoded JSON length.</summary>
	internal const int MaximumFileBytes = MaximumBytes;
	internal const int MaximumPaths = 100_000;
	private const int CurrentVersion = 2;
	private const int LegacyVersion = 1;
	private const int MaximumOffsets = 8;
	private const long MaximumOffset = 1_048_576;
	private const int MaximumJsonDepth = 8;
	private const int MaximumModuleNameBytes = 4096;
	private const string LegacyMapName = "imported";
	private static readonly UTF8Encoding StrictUtf8 = new(false, true);
	private static readonly JsonDocumentOptions DocumentOptions = new()
	{
		AllowTrailingCommas = false,
		CommentHandling = JsonCommentHandling.Disallow,
		MaxDepth = MaximumJsonDepth
	};

	/// <summary>Writes a version-2 scan snapshot to an already-open stream.</summary>
	internal static void Save(Stream output, PointerScanSnapshot snapshot, CancellationToken stopping)
	{
		ArgumentNullException.ThrowIfNull(output);
		if (!output.CanWrite)
		{
			throw new ArgumentException("The pointer scan output stream is not writable.", nameof(output));
		}

		Validate(snapshot);
		stopping.ThrowIfCancellationRequested();
		using MaximumLengthWriteStream bounded = new(output, MaximumBytes);
		using Utf8JsonWriter writer = new(bounded, new JsonWriterOptions { Indented = false, SkipValidation = false });
		writer.WriteStartObject();
		writer.WriteNumber("version", CurrentVersion);
		writer.WriteString("mapName", snapshot.MapName);
		writer.WriteNumber("target", snapshot.Target);
		writer.WriteNumber("width", snapshot.Width);
		writer.WriteBoolean("incomplete", snapshot.Incomplete);
		writer.WriteBoolean("coverageIncomplete", snapshot.CoverageIncomplete ?? snapshot.Incomplete);
		writer.WritePropertyName("paths");
		writer.WriteStartArray();
		foreach (PointerPath path in snapshot.Paths)
		{
			stopping.ThrowIfCancellationRequested();
			writer.WriteStartObject();
			writer.WriteNumber("baseAddress", path.BaseAddress);
			if (path.Module is null)
			{
				writer.WriteNull("module");
			}
			else
			{
				writer.WriteString("module", path.Module);
			}

			writer.WriteNumber("moduleOffset", path.ModuleOffset);
			writer.WritePropertyName("offsets");
			writer.WriteStartArray();
			foreach (long offset in path.Offsets)
			{
				writer.WriteNumberValue(offset);
			}

			writer.WriteEndArray();
			writer.WriteEndObject();
		}

		writer.WriteEndArray();
		writer.WriteEndObject();
		writer.Flush();
		stopping.ThrowIfCancellationRequested();
	}

	/// <summary>
	/// Reads a version-1 or version-2 scan snapshot from an already-open stream. Imported paths are always unresolved.
	/// </summary>
	internal static PointerScanSnapshot Load(Stream input, CancellationToken stopping)
	{
		ArgumentNullException.ThrowIfNull(input);
		if (!input.CanRead)
		{
			throw new ArgumentException("The pointer scan input stream is not readable.", nameof(input));
		}

		stopping.ThrowIfCancellationRequested();
		using MemoryStream json = ReadJson(input, stopping);
		using JsonDocument document = JsonDocument.Parse(json.GetBuffer().AsMemory(0, checked((int) json.Length)),
			DocumentOptions);
		stopping.ThrowIfCancellationRequested();
		return ReadSnapshot(document.RootElement, stopping);
	}

	/// <summary>Validates a current-version snapshot before a caller publishes it into an in-memory scan.</summary>
	internal static void Validate(PointerScanSnapshot snapshot)
	{
		ArgumentNullException.ThrowIfNull(snapshot);
		ValidateSnapshot(snapshot, legacyOffsets: false, nameof(snapshot));
	}

	private static MemoryStream ReadJson(Stream input, CancellationToken stopping)
	{
		if (input.CanSeek && input.Length - input.Position > MaximumBytes)
		{
			throw new InvalidDataException($"Pointer scan JSON exceeds the {MaximumBytes}-byte limit.");
		}

		MemoryStream json = new();
		byte[] buffer = new byte[81920];
		try
		{
			while (true)
			{
				stopping.ThrowIfCancellationRequested();
				int read = input.Read(buffer, 0, buffer.Length);
				if (read == 0)
				{
					break;
				}

				if (json.Length > MaximumBytes - read)
				{
					throw new InvalidDataException($"Pointer scan JSON exceeds the {MaximumBytes}-byte limit.");
				}

				json.Write(buffer, 0, read);
			}

			if (json.Length == 0)
			{
				throw new InvalidDataException("The pointer scan file is empty.");
			}

			return json;
		}
		catch
		{
			json.Dispose();
			throw;
		}
	}

	private static PointerScanSnapshot ReadSnapshot(JsonElement root, CancellationToken stopping)
	{
		RequireObject(root, "The pointer scan JSON root must be an object.");
		int version = ReadInt32(root, "version");
		return version switch
		{
			LegacyVersion => ReadVersion1(root, stopping),
			CurrentVersion => ReadVersion2(root, stopping),
			_ => throw new InvalidDataException($"Unsupported pointer scan file version '{version}'.")
		};
	}

	private static PointerScanSnapshot ReadVersion1(JsonElement root, CancellationToken stopping)
	{
		RequireExactProperties(root, "version", "width", "paths", "incomplete");
		int width = ReadInt32(root, "width");
		bool incomplete = ReadBoolean(root, "incomplete");
		PointerPath[] paths = ReadPaths(root, width, legacyOffsets: true, stopping);
		PointerScanSnapshot snapshot = new(LegacyMapName, 0, width, paths, incomplete);
		ValidateSnapshot(snapshot, legacyOffsets: true, parameter: null);
		return snapshot;
	}

	private static PointerScanSnapshot ReadVersion2(JsonElement root, CancellationToken stopping)
	{
		bool hasCoverage = root.TryGetProperty("coverageIncomplete", out _);
		if (hasCoverage)
		{
			RequireExactProperties(root, "version", "mapName", "target", "width", "paths", "incomplete",
				"coverageIncomplete");
		}
		else
		{
			RequireExactProperties(root, "version", "mapName", "target", "width", "paths", "incomplete");
		}
		string mapName = ReadString(root, "mapName");
		ulong target = ReadUInt64(root, "target");
		int width = ReadInt32(root, "width");
		bool incomplete = ReadBoolean(root, "incomplete");
		bool coverageIncomplete = hasCoverage ? ReadBoolean(root, "coverageIncomplete") : incomplete;
		PointerPath[] paths = ReadPaths(root, width, legacyOffsets: false, stopping);
		PointerScanSnapshot snapshot = new(mapName, target, width, paths, incomplete, coverageIncomplete);
		ValidateSnapshot(snapshot, legacyOffsets: false, parameter: null);
		return snapshot;
	}

	private static PointerPath[] ReadPaths(JsonElement root, int width, bool legacyOffsets, CancellationToken stopping)
	{
		JsonElement paths = ReadProperty(root, "paths");
		if (paths.ValueKind != JsonValueKind.Array)
		{
			throw new InvalidDataException("Pointer scan paths must be an array.");
		}

		int count = paths.GetArrayLength();
		if (count > MaximumPaths)
		{
			throw new InvalidDataException($"Pointer scans may contain at most {MaximumPaths} paths.");
		}

		PointerPath[] result = new PointerPath[count];
		int index = 0;
		foreach (JsonElement path in paths.EnumerateArray())
		{
			stopping.ThrowIfCancellationRequested();
			RequireExactProperties(path, "baseAddress", "module", "moduleOffset", "offsets");
			ulong baseAddress = ReadUInt64(path, "baseAddress");
			string? module = ReadNullableString(path, "module");
			ulong moduleOffset = ReadUInt64(path, "moduleOffset");
			long[] offsets = ReadOffsets(path, legacyOffsets);
			PointerPath imported = new(baseAddress, module, moduleOffset, offsets)
			{
				Verification = PointerVerification.Unresolved
			};
			ValidatePath(imported, width, legacyOffsets, parameter: null);
			result[index++] = imported;
		}

		return result;
	}

	private static long[] ReadOffsets(JsonElement path, bool legacyOffsets)
	{
		JsonElement offsets = ReadProperty(path, "offsets");
		if (offsets.ValueKind != JsonValueKind.Array)
		{
			throw new InvalidDataException("Pointer path offsets must be an array.");
		}

		int count = offsets.GetArrayLength();
		if (count is < 1 or > MaximumOffsets)
		{
			throw new InvalidDataException($"Pointer paths require one to {MaximumOffsets} offsets.");
		}

		long[] result = new long[count];
		int index = 0;
		foreach (JsonElement offset in offsets.EnumerateArray())
		{
			if (offset.ValueKind != JsonValueKind.Number || !offset.TryGetInt64(out long value) ||
				value is < -MaximumOffset or > MaximumOffset || (legacyOffsets && value < 0))
			{
				throw new InvalidDataException(legacyOffsets
					? $"Version-1 pointer offsets must be between 0 and {MaximumOffset}."
					: $"Pointer offsets must be between {-MaximumOffset} and {MaximumOffset}.");
			}

			result[index++] = value;
		}

		return result;
	}

	private static void ValidateSnapshot(PointerScanSnapshot snapshot, bool legacyOffsets, string? parameter)
	{
		if (snapshot.Width is not (4 or 8))
		{
			ThrowInvalid("Pointer scan width must be 4 or 8 bytes.", parameter);
		}
		if (snapshot.CoverageIncomplete == true && !snapshot.Incomplete)
		{
			ThrowInvalid("An incomplete source cannot have a complete scan result.", parameter);
		}

		ValidateMapName(snapshot.MapName, parameter);
		if (snapshot.Width == 4 && snapshot.Target > uint.MaxValue)
		{
			ThrowInvalid("Pointer scan target does not fit the pointer width.", parameter);
		}

		if (snapshot.Paths is null)
		{
			ThrowInvalid("Pointer scan paths cannot be null.", parameter);
		}

		PointerPath[] paths = snapshot.Paths;
		if (paths.Length > MaximumPaths)
		{
			ThrowInvalid($"Pointer scans may contain at most {MaximumPaths} paths.", parameter);
		}

		foreach (PointerPath? path in paths)
		{
			if (path is null)
			{
				ThrowInvalid("Pointer scan paths cannot contain null values.", parameter);
			}

			ValidatePath(path!, snapshot.Width, legacyOffsets, parameter);
		}
	}

	private static void ValidatePath(PointerPath path, int width, bool legacyOffsets, string? parameter)
	{
		ulong maximumAddress = width == 4 ? uint.MaxValue : ulong.MaxValue;
		if (path.BaseAddress > maximumAddress || path.ModuleOffset > maximumAddress)
		{
			ThrowInvalid("Pointer path addresses do not fit the pointer width.", parameter);
		}

		if (path.Module is null)
		{
			if (path.ModuleOffset != 0)
			{
				ThrowInvalid("Absolute pointer paths cannot have a module offset.", parameter);
			}
		}
		else
		{
			ValidateModuleName(path.Module, parameter);
		}

		if (path.Offsets is null || path.Offsets.Length is < 1 or > MaximumOffsets ||
			path.Offsets.Any(offset => offset is < -MaximumOffset or > MaximumOffset || (legacyOffsets && offset < 0)))
		{
			ThrowInvalid(legacyOffsets
				? $"Version-1 pointer paths require one to {MaximumOffsets} offsets between 0 and {MaximumOffset}."
				: $"Pointer paths require one to {MaximumOffsets} offsets between {-MaximumOffset} and {MaximumOffset}.",
				parameter);
		}
	}

	private static void ValidateMapName(string? mapName, string? parameter)
	{
		if (string.IsNullOrWhiteSpace(mapName) || mapName.Length > PointerStore.MaximumNameLength ||
			mapName.Any(char.IsControl))
		{
			ThrowInvalid($"Pointer map names must be 1 to {PointerStore.MaximumNameLength} printable characters.", parameter);
		}

		EnsureStrictUtf8(mapName, "Pointer map names must be valid UTF-8.", parameter);
	}

	private static void ValidateModuleName(string module, string? parameter)
	{
		if (string.IsNullOrWhiteSpace(module) || module.Any(char.IsControl))
		{
			ThrowInvalid("Pointer module names must be printable.", parameter);
		}

		try
		{
			if (StrictUtf8.GetByteCount(module) > MaximumModuleNameBytes)
			{
				ThrowInvalid($"Pointer module names must be at most {MaximumModuleNameBytes} UTF-8 bytes.", parameter);
			}
		}
		catch (EncoderFallbackException)
		{
			ThrowInvalid("Pointer module names must be valid UTF-8.", parameter);
		}
	}

	private static void EnsureStrictUtf8(string value, string message, string? parameter)
	{
		try
		{
			_ = StrictUtf8.GetByteCount(value);
		}
		catch (EncoderFallbackException)
		{
			ThrowInvalid(message, parameter);
		}
	}

	[DoesNotReturn]
	private static void ThrowInvalid(string message, string? parameter)
	{
		if (parameter is not null)
		{
			throw new ArgumentException(message, parameter);
		}

		throw new InvalidDataException(message);
	}

	private static void RequireExactProperties(JsonElement element, params string[] expected)
	{
		RequireObject(element, "Pointer scan JSON values must be objects.");
		int seen = 0;
		foreach (JsonProperty property in element.EnumerateObject())
		{
			int index = Array.IndexOf(expected, property.Name);
			if (index < 0 || (seen & (1 << index)) != 0)
			{
				throw new InvalidDataException($"Unexpected or duplicate pointer scan property '{property.Name}'.");
			}

			seen |= 1 << index;
		}

		if (seen != (1 << expected.Length) - 1)
		{
			throw new InvalidDataException("The pointer scan JSON is missing required fields.");
		}
	}

	private static void RequireObject(JsonElement element, string message)
	{
		if (element.ValueKind != JsonValueKind.Object)
		{
			throw new InvalidDataException(message);
		}
	}

	private static JsonElement ReadProperty(JsonElement objectElement, string name)
	{
		return objectElement.TryGetProperty(name, out JsonElement value)
			? value
			: throw new InvalidDataException($"The pointer scan JSON is missing '{name}'.");
	}

	private static int ReadInt32(JsonElement objectElement, string name)
	{
		JsonElement value = ReadProperty(objectElement, name);
		if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out int result))
		{
			throw new InvalidDataException($"Pointer scan field '{name}' must be a 32-bit integer.");
		}

		return result;
	}

	private static ulong ReadUInt64(JsonElement objectElement, string name)
	{
		JsonElement value = ReadProperty(objectElement, name);
		if (value.ValueKind != JsonValueKind.Number || !value.TryGetUInt64(out ulong result))
		{
			throw new InvalidDataException($"Pointer scan field '{name}' must be an unsigned 64-bit integer.");
		}

		return result;
	}

	private static bool ReadBoolean(JsonElement objectElement, string name)
	{
		JsonElement value = ReadProperty(objectElement, name);
		if (value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
		{
			throw new InvalidDataException($"Pointer scan field '{name}' must be a boolean.");
		}

		return value.GetBoolean();
	}

	private static string ReadString(JsonElement objectElement, string name)
	{
		JsonElement value = ReadProperty(objectElement, name);
		return value.ValueKind == JsonValueKind.String
			? value.GetString() ?? throw new InvalidDataException($"Pointer scan field '{name}' cannot be null.")
			: throw new InvalidDataException($"Pointer scan field '{name}' must be a string.");
	}

	private static string? ReadNullableString(JsonElement objectElement, string name)
	{
		JsonElement value = ReadProperty(objectElement, name);
		return value.ValueKind switch
		{
			JsonValueKind.Null => null,
			JsonValueKind.String => value.GetString() ?? throw new InvalidDataException($"Pointer scan field '{name}' is invalid."),
			_ => throw new InvalidDataException($"Pointer scan field '{name}' must be a string or null.")
		};
	}

	private sealed class MaximumLengthWriteStream(Stream output, int maximumLength) : Stream
	{
		private readonly Stream _output = output;
		private readonly int _maximumLength = maximumLength;
		private int _written;

		public override bool CanRead => false;
		public override bool CanSeek => false;
		public override bool CanWrite => true;
		public override long Length => throw new NotSupportedException();
		public override long Position
		{
			get => _written;
			set => throw new NotSupportedException();
		}

		public override void Flush() => _output.Flush();
		public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
		public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
		public override void SetLength(long value) => throw new NotSupportedException();
		public override void Write(byte[] buffer, int offset, int count)
		{
			EnsureCapacity(count);
			_output.Write(buffer, offset, count);
		}

		public override void Write(ReadOnlySpan<byte> buffer)
		{
			EnsureCapacity(buffer.Length);
			_output.Write(buffer);
		}

		private void EnsureCapacity(int count)
		{
			if (count < 0 || _written > _maximumLength - count)
			{
				throw new InvalidDataException($"Pointer scan JSON exceeds the {MaximumBytes}-byte limit.");
			}

			_written += count;
		}
	}
}

/// <summary>A serialized pointer scan and the source-map metadata needed to restore it.</summary>
internal sealed record PointerScanSnapshot(string MapName, ulong Target, int Width, PointerPath[] Paths, bool Incomplete,
	bool? CoverageIncomplete = null);
