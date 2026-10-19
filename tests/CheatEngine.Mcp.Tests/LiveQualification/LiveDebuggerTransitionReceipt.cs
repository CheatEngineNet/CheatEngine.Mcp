using System.Globalization;

namespace CheatEngine.Mcp.Tests.LiveQualification;

/// <summary>Parses the bounded private diagnostic receipt without treating advisory state as a stop oracle.</summary>
internal sealed record LiveDebuggerTransitionReceipt(string[] Lines, int EventCount, int? ThreadId, ulong? Rip,
	int CallbackCount, LiveDebuggerTransitionRawCallback? FirstCallback)
{
	private const int SampleFieldCount = 28;

	internal static LiveDebuggerTransitionReceipt Parse(string[] lines, string nonce, string command)
	{
		if (lines.Length is < 3 or > SampleFieldCount || lines.Any(line => line.Length > 256) ||
			lines[0] != "ok" || lines[1] != nonce || lines[2] != command)
		{
			throw new InvalidDataException("The private debugger diagnostic returned a mismatched or failed receipt.");
		}

		if (command is "install" or "restore")
		{
			return lines.Length == 3 ? new(lines, 0, null, null, 0, null)
				: throw new InvalidDataException("The diagnostic acknowledgement contains unexpected fields.");
		}

		if (command != "sample" || lines.Length != SampleFieldCount ||
			!int.TryParse(lines[15], NumberStyles.None, CultureInfo.InvariantCulture, out int eventCount) || eventCount is < 0 or > 1 ||
			!int.TryParse(lines[18], NumberStyles.None, CultureInfo.InvariantCulture, out int callbackCount) || callbackCount is < 0 or > 65535)
		{
			throw new InvalidDataException("The debugger diagnostic event or callback count is invalid.");
		}

		(int? threadId, ulong? rip) = ParseEvent(lines, eventCount);
		LiveDebuggerTransitionRawCallback? first = ParseFirstCallback(lines, callbackCount);
		if (eventCount == 1 && callbackCount == 0)
		{
			throw new InvalidDataException("The diagnostic reported an exact event without a callback.");
		}

		return new(lines, eventCount, threadId, rip, callbackCount, first);
	}

	private static (int? ThreadId, ulong? Rip) ParseEvent(string[] lines, int eventCount)
	{
		if (eventCount == 0)
		{
			return lines[16].Length == 0 && lines[17].Length == 0 ? (null, null)
				: throw new InvalidDataException("The diagnostic reported an event without an event count.");
		}

		if (!int.TryParse(lines[16], NumberStyles.None, CultureInfo.InvariantCulture, out int threadId) || threadId <= 0 ||
			lines[17].Length is < 1 or > 16 ||
			!ulong.TryParse(lines[17], NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out ulong rip) || rip == 0)
		{
			throw new InvalidDataException("The private debugger diagnostic event identity is invalid.");
		}

		return (threadId, rip);
	}

	private static LiveDebuggerTransitionRawCallback? ParseFirstCallback(string[] lines, int callbackCount)
	{
		LiveDebuggerTransitionRawValue thread = ParseRaw(lines, 19);
		LiveDebuggerTransitionRawValue rip = ParseRaw(lines, 22);
		LiveDebuggerTransitionRawValue eip = ParseRaw(lines, 25);
		if (callbackCount == 0)
		{
			if (thread != LiveDebuggerTransitionRawValue.None || rip != LiveDebuggerTransitionRawValue.None ||
				eip != LiveDebuggerTransitionRawValue.None)
			{
				throw new InvalidDataException("The diagnostic reported callback values without a callback.");
			}
			return null;
		}

		return new(thread, rip, eip);
	}

	private static LiveDebuggerTransitionRawValue ParseRaw(string[] lines, int index)
	{
		string type = lines[index];
		string mathType = lines[index + 1];
		string value = lines[index + 2];
		if (type.Length is < 1 or > 16 || mathType is not ("integer" or "float" or "not_number") || value.Length > 64 ||
			value.Any(character => character is < ' ' or > '~'))
		{
			throw new InvalidDataException("The diagnostic callback primitive value is invalid.");
		}
		if (type == "none")
		{
			if (mathType != "not_number" || value.Length != 0)
			{
				throw new InvalidDataException("The diagnostic callback absence marker is invalid.");
			}
			return LiveDebuggerTransitionRawValue.None;
		}
		return new(type, mathType, value);
	}
}

internal sealed record LiveDebuggerTransitionRawCallback(LiveDebuggerTransitionRawValue ThreadId,
	LiveDebuggerTransitionRawValue Rip, LiveDebuggerTransitionRawValue Eip);

internal sealed record LiveDebuggerTransitionRawValue(string Type, string MathType, string Value)
{
	internal static readonly LiveDebuggerTransitionRawValue None = new("none", "not_number", string.Empty);
}
