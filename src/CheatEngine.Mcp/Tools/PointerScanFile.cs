using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CheatEngine.Mcp.Tools;

/// <summary>Persists bounded MCP pointer scan results in a versioned JSON format.</summary>
internal static class PointerScanFile
{
	private const int CurrentVersion = 1;
	private const int MaximumBytes = 16 * 1024 * 1024;
	private const int MaximumPaths = 10_000;
	private const int MaximumOffsets = 8;
	private const long MaximumOffset = 1_048_576;
	private static readonly UTF8Encoding StrictUtf8 = new(false, true);
	private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
	{
		MaxDepth = 16,
		UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
	};

	internal static void Save(string filePath, PointerScanSnapshot scan, bool overwrite, CancellationToken stopping)
	{
		ArgumentNullException.ThrowIfNull(scan);
		Validate(scan);
		PersistedPath[] paths = scan.Paths.Select(static path => new PersistedPath(path.BaseAddress, path.Module, path.ModuleOffset, path.Offsets)).ToArray();
		PointerDataFile.Write(filePath, ".json", overwrite, stream =>
		{
			using MaximumLengthStream bounded = new(stream, MaximumBytes);
			JsonSerializer.Serialize(bounded, new PersistedScan(CurrentVersion, scan.Width, paths, scan.Incomplete), JsonOptions);
		}, stopping);
	}

	internal static PointerScanSnapshot Load(string filePath, CancellationToken stopping)
	{
		PersistedScan persisted = PointerDataFile.Read(filePath, ".json", MaximumBytes, Deserialize, stopping);
		stopping.ThrowIfCancellationRequested();
		if (persisted.Version != CurrentVersion)
		{
			throw new InvalidDataException($"Unsupported pointer scan file version '{persisted.Version}'.");
		}
		PointerPath[] paths = (persisted.Paths ?? throw new InvalidDataException("The pointer scan paths are missing."))
			.Select(static path => new PointerPath(path.BaseAddress, path.Module, path.ModuleOffset,
				path.Offsets ?? throw new InvalidDataException("Pointer path offsets are missing.")))
			.ToArray();
		PointerScanSnapshot scan = new(persisted.Width, paths, persisted.Incomplete);
		Validate(scan);
		PointerPath[] unresolved = scan.Paths.Select(path => path with { Verification = "unresolved" }).ToArray();
		stopping.ThrowIfCancellationRequested();
		return new PointerScanSnapshot(scan.Width, unresolved, scan.Incomplete);
	}

	private static PersistedScan Deserialize(Stream stream)
	{
		using JsonDocument document = JsonDocument.Parse(stream, new JsonDocumentOptions { MaxDepth = 16 });
		RequireProperties(document.RootElement, "version", "width", "paths", "incomplete");
		JsonElement paths = document.RootElement.GetProperty("paths");
		if (paths.ValueKind != JsonValueKind.Array)
		{
			throw new InvalidDataException("Pointer scan paths must be an array.");
		}
		if (paths.GetArrayLength() > MaximumPaths)
		{
			throw new InvalidDataException($"Pointer scans may contain at most {MaximumPaths} paths.");
		}
		foreach (JsonElement path in paths.EnumerateArray())
		{
			RequireProperties(path, "baseAddress", "module", "moduleOffset", "offsets");
			JsonElement offsets = path.GetProperty("offsets");
			if (offsets.ValueKind != JsonValueKind.Array || offsets.GetArrayLength() is < 1 or > MaximumOffsets)
			{
				throw new InvalidDataException("Pointer paths require one to eight offsets.");
			}
		}
		return JsonSerializer.Deserialize<PersistedScan>(document.RootElement, JsonOptions)
			?? throw new InvalidDataException("The pointer scan file is empty.");
	}

	private static void RequireProperties(JsonElement element, params string[] names)
	{
		if (element.ValueKind != JsonValueKind.Object || names.Any(name => !element.TryGetProperty(name, out _)))
		{
			throw new InvalidDataException("The pointer scan JSON is missing required fields.");
		}
	}

	private static void Validate(PointerScanSnapshot scan)
	{
		if (scan.Width is not (4 or 8))
		{
			throw new ArgumentException("Pointer scan width must be 4 or 8 bytes.", nameof(scan));
		}
		if (scan.Paths is null || scan.Paths.Length > MaximumPaths)
		{
			throw new ArgumentException($"Pointer scans may contain at most {MaximumPaths} paths.", nameof(scan));
		}
		foreach (PointerPath? path in scan.Paths)
		{
			if (path is null)
			{
				throw new ArgumentException("Pointer scan paths cannot contain null values.", nameof(scan));
			}
			ValidatePath(path, scan.Width);
		}
	}

	private static void ValidatePath(PointerPath path, int width)
	{
		ulong maximumAddress = width == 4 ? uint.MaxValue : ulong.MaxValue;
		if (path.BaseAddress > maximumAddress || path.ModuleOffset > maximumAddress || path.ModuleOffset > long.MaxValue)
		{
			throw new ArgumentException("Pointer path addresses do not fit the pointer width.", nameof(path));
		}
		if (path.Module is null)
		{
			if (path.ModuleOffset != 0)
			{
				throw new ArgumentException("Absolute pointer paths cannot have a module offset.", nameof(path));
			}
		}
		else
		{
			try
			{
				if (string.IsNullOrWhiteSpace(path.Module) || path.Module.Any(char.IsControl) || StrictUtf8.GetByteCount(path.Module) > 4096)
				{
					throw new ArgumentException("Pointer module names must be valid UTF-8 and at most 4096 bytes.", nameof(path));
				}
			}
			catch (EncoderFallbackException exception)
			{
				throw new ArgumentException("Pointer module names must be valid UTF-8.", nameof(path), exception);
			}
		}
		if (path.Offsets is null || path.Offsets.Length is < 1 or > MaximumOffsets || path.Offsets.Any(offset => offset is < 0 or > MaximumOffset))
		{
			throw new ArgumentException("Pointer paths require one to eight offsets between 0 and 1048576.", nameof(path));
		}
	}

	private sealed record PersistedScan(int Version, int Width, PersistedPath[]? Paths, bool Incomplete);
	private sealed record PersistedPath(ulong BaseAddress, string? Module, ulong ModuleOffset, long[]? Offsets);

	private sealed class MaximumLengthStream(Stream stream, int maximumLength) : Stream
	{
		private readonly Stream _stream = stream;
		private readonly int _maximumLength = maximumLength;

		public override bool CanRead => false;
		public override bool CanSeek => false;
		public override bool CanWrite => true;
		public override long Length => throw new NotSupportedException();
		public override long Position
		{
			get => _stream.Position;
			set => throw new NotSupportedException();
		}

		public override void Flush() => _stream.Flush();
		public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
		public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
		public override void SetLength(long value) => throw new NotSupportedException();
		public override void Write(byte[] buffer, int offset, int count)
		{
			EnsureCapacity(count);
			_stream.Write(buffer, offset, count);
		}
		public override void Write(ReadOnlySpan<byte> buffer)
		{
			EnsureCapacity(buffer.Length);
			_stream.Write(buffer);
		}

		private void EnsureCapacity(int count)
		{
			if (_stream.Position > _maximumLength - count)
			{
				throw new InvalidDataException($"Pointer scan JSON exceeds the {MaximumBytes}-byte limit.");
			}
		}
	}
}

internal sealed record PointerScanSnapshot(int Width, PointerPath[] Paths, bool Incomplete);
