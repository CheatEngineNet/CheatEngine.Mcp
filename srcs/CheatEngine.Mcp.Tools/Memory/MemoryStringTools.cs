using System.Buffers;
using System.ComponentModel;
using System.Globalization;
using System.Text;

using CheatEngine.Client;
using CheatEngine.Client.Memory;
using CheatEngine.Mcp.Core.Contract;
using CheatEngine.Mcp.Core.Values;
using CheatEngine.SDK.Engine.Values;

using ModelContextProtocol.Server;

namespace CheatEngine.Mcp.Tools.Memory;

/// <summary>Read-only bounded printable-string discovery for one selected target range.</summary>
[McpServerToolType]
public sealed class MemoryStringTools
{
	/// <summary>The largest contiguous range one string search reads.</summary>
	internal const int MaximumBytes = 1 << 20;

	/// <summary>The largest number of Unicode scalar values a run must contain.</summary>
	internal const int MaximumMinimumLength = 4096;

	/// <summary>The longest literal filter accepted before dispatch.</summary>
	internal const int MaximumContainsLength = 4096;

	/// <summary>The largest matching-run page length.</summary>
	internal const int MaximumPageLength = 256;

	/// <summary>The most Unicode scalar values retained in a result preview.</summary>
	internal const int MaximumPreviewScalars = 256;

	private readonly ToolDispatch _dispatch;

	/// <summary>Creates the tools; the constructor does no Cheat Engine work.</summary>
	public MemoryStringTools(ToolDispatch dispatch)
	{
		ArgumentNullException.ThrowIfNull(dispatch);
		_dispatch = dispatch;
	}

	/// <summary>Finds printable UTF-8 or UTF-16LE runs in one explicitly bounded target-memory range.</summary>
	[McpServerTool(Name = CheatEngineToolNames.MemoryFindStrings, Title = "Find memory strings", ReadOnly = true,
		Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
	[McpMeta(McpDispatchClass.MetaKey, McpDispatchClass.Short)]
	[Description(
		"Read one selected contiguous range (at most 1 MiB) once and list printable UTF-8 or UTF-16LE runs. NUL, controls, Unicode format values, line or paragraph separators and invalid encoding delimit runs. This is not a process-wide scan or an atomic snapshot; a run at either requested-range edge can continue outside it, as marked in the result.")]
	public MemoryStringFindResult FindStrings(
		[Description("An address or Cheat Engine address expression for the first byte of the contiguous range.")]
		string address,
		[Description("The exact byte count to read once, from 1 to 1048576.")]
		int size,
		[Description("The byte encoding to decode: utf8 (default) or utf16_le.")]
		MemoryStringSearchEncoding encoding = MemoryStringSearchEncoding.Utf8,
		[Description("The minimum printable Unicode scalar values in a run, from 1 to 4096 (default 4).")]
		int minLength = 4,
		[Description("An optional literal that the full decoded run must contain, compared without case. Preview truncation does not affect this filter.")]
		string? contains = null,
		[Description("The zero-based matching-run offset.")]
		int offset = 0,
		[Description("The most matching runs to return, from 1 to 256 (default 64).")]
		int limit = 64,
		CancellationToken cancellationToken = default)
	{
		string expression = MemoryTargets.RequireExpression(address, "address");
		MemoryTargets.RequireRange(size, "size", 1, MaximumBytes);
		if (!Enum.IsDefined(encoding))
		{
			throw CheatEngineToolException.InvalidArgument("encoding", "must be utf8 or utf16_le.");
		}

		MemoryTargets.RequireRange(minLength, "minLength", 1, MaximumMinimumLength);
		if (contains is { Length: > MaximumContainsLength })
		{
			throw CheatEngineToolException.LimitExceeded("contains",
				$"must be at most {MaximumContainsLength} characters.");
		}

		MemoryTargets.RequireRange(offset, "offset", 0, int.MaxValue);
		MemoryTargets.RequireRange(limit, "limit", 1, MaximumPageLength);

		return _dispatch.Run(CheatEngineToolNames.MemoryFindStrings, token =>
		{
			ICheatEngineClient client = _dispatch.Client;
			long epoch = MemoryTargets.SelectionEpoch(client, token);
			Address target = MemoryTargets.Resolve(client, expression, "address", token);
			MemoryTargets.RequireSameTarget(client, epoch, CheatEngineToolNames.MemoryFindStrings, token);
			RequireRangeFitsTargetAddressSpace(target, size, MemoryTargets.PointerBytes(client, token));
			MemoryTargets.RequireSameTarget(client, epoch, CheatEngineToolNames.MemoryFindStrings, token);
			byte[] bytes = [.. client.Memory.ReadBytes(new MemoryBytesReadRequest(target, size), token)];
			MemoryTargets.RequireSameTarget(client, epoch, CheatEngineToolNames.MemoryFindStrings, token);
			return bytes.Length != size
				? throw CheatEngineToolException.InvalidState(
					"Cheat Engine reported a successful range read with a different byte count; no string result was returned.")
				: Page(target, bytes, encoding, minLength, contains, offset, limit, token);
		}, cancellationToken);
	}

	private static MemoryStringFindResult Page(Address target, ReadOnlySpan<byte> bytes,
		MemoryStringSearchEncoding encoding, int minLength, string? contains, int offset, int limit,
		CancellationToken cancellationToken)
	{
		(int total, List<DecodedRun> matches) = Extract(bytes, encoding, minLength, contains, offset, limit,
			cancellationToken);
		MemoryStringMatch[] page = new MemoryStringMatch[matches.Count];
		for (int index = 0; index < page.Length; index++)
		{
			DecodedRun run = matches[index];
			(string preview, bool previewTruncated) = Preview(run.Text);
			page[index] = new MemoryStringMatch(HexFormat.Address(new Address(target.ToUInt64() + (ulong) run.Offset)),
				run.ByteLength, preview, previewTruncated, run.Offset == 0, run.Offset + run.ByteLength == bytes.Length);
		}

		long next = (long) offset + page.Length;
		int? nextOffset = next < total ? (int) next : null;
		return new MemoryStringFindResult(HexFormat.Address(target), bytes.Length, encoding, total, nextOffset,
			nextOffset is not null, page);
	}

	private static (int Total, List<DecodedRun> Page) Extract(ReadOnlySpan<byte> bytes,
		MemoryStringSearchEncoding encoding, int minimumLength, string? contains, int offset, int limit,
		CancellationToken cancellationToken)
	{
		List<DecodedRun> page = [];
		StringBuilder? text = null;
		int start = 0;
		int scalarCount = 0;
		int cursor = 0;
		int lastCancellationCheck = -4096;
		int total = 0;
		while (cursor < bytes.Length)
		{
			if (cursor - lastCancellationCheck >= 4096)
			{
				cancellationToken.ThrowIfCancellationRequested();
				lastCancellationCheck = cursor;
			}

			bool decoded = encoding is MemoryStringSearchEncoding.Utf8
				? TryDecodeUtf8(bytes[cursor..], out Rune rune, out int consumed)
				: TryDecodeUtf16Le(bytes[cursor..], out rune, out consumed);
			if (!decoded || !IsPrintable(rune))
			{
				AddIfMatch(page, ref total, text, start, cursor - start, scalarCount, minimumLength, contains, offset,
					limit);
				text = null;
				scalarCount = 0;
				cursor += consumed;
				continue;
			}

			text ??= new StringBuilder();
			if (scalarCount == 0)
			{
				start = cursor;
			}

			text.Append(rune.ToString());
			scalarCount++;
			cursor += consumed;
		}

		AddIfMatch(page, ref total, text, start, cursor - start, scalarCount, minimumLength, contains, offset, limit);
		return (total, page);
	}

	private static bool TryDecodeUtf8(ReadOnlySpan<byte> bytes, out Rune rune, out int consumed)
	{
		OperationStatus status = Rune.DecodeFromUtf8(bytes, out rune, out consumed);
		if (status is OperationStatus.Done)
		{
			return true;
		}

		consumed = 1;
		rune = default;
		return false;
	}

	private static bool TryDecodeUtf16Le(ReadOnlySpan<byte> bytes, out Rune rune, out int consumed)
	{
		if (bytes.Length < 2)
		{
			rune = default;
			consumed = 1;
			return false;
		}

		char first = (char) (bytes[0] | (bytes[1] << 8));
		if (char.IsHighSurrogate(first))
		{
			if (bytes.Length >= 4)
			{
				char second = (char) (bytes[2] | (bytes[3] << 8));
				if (char.IsLowSurrogate(second))
				{
					rune = new Rune(first, second);
					consumed = 4;
					return true;
				}
			}

			rune = default;
			consumed = 2;
			return false;
		}

		if (char.IsLowSurrogate(first))
		{
			rune = default;
			consumed = 2;
			return false;
		}

		rune = new Rune(first);
		consumed = 2;
		return true;
	}

	private static void AddIfMatch(List<DecodedRun> page, ref int total, StringBuilder? text, int offset,
		int byteLength, int scalarCount, int minimumLength, string? contains, int requestedOffset, int limit)
	{
		if (text is null || scalarCount < minimumLength)
		{
			return;
		}

		string value = text.ToString();
		if (contains is null || value.Contains(contains, StringComparison.OrdinalIgnoreCase))
		{
			if (total >= requestedOffset && page.Count < limit)
			{
				page.Add(new DecodedRun(offset, byteLength, value));
			}

			total++;
		}
	}

	private static bool IsPrintable(Rune rune)
	{
		return Rune.IsControl(rune) is false && Rune.GetUnicodeCategory(rune) is not (UnicodeCategory.Format or
			UnicodeCategory.OtherNotAssigned or UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator);
	}

	private static (string Text, bool Truncated) Preview(string text)
	{
		int characters = 0;
		int scalars = 0;
		while (characters < text.Length && scalars < MaximumPreviewScalars)
		{
			OperationStatus status = Rune.DecodeFromUtf16(text.AsSpan(characters), out _, out int consumed);
			if (status is not OperationStatus.Done)
			{
				throw new InvalidOperationException("A decoded memory string contained an invalid UTF-16 sequence.");
			}

			characters += consumed;
			scalars++;
		}

		return characters == text.Length ? (text, false) : (text[..characters], true);
	}

	private static void RequireRangeFitsTargetAddressSpace(Address target, int size, int pointerBytes)
	{
		ulong maximum = pointerBytes == 4 ? uint.MaxValue : ulong.MaxValue;
		ulong start = target.ToUInt64();
		if (start > maximum || (ulong) size - 1 > maximum - start)
		{
			throw CheatEngineToolException.InvalidArgument("size",
				"extends past the selected target's address space.");
		}
	}

	private sealed record DecodedRun(int Offset, int ByteLength, string Text);
}
