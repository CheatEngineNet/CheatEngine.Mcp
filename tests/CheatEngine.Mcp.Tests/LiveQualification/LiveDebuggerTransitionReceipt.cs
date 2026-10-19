using System.Globalization;

namespace CheatEngine.Mcp.Tests.LiveQualification;

/// <summary>Parses the bounded private diagnostic receipt without treating advisory state as a stop oracle.</summary>
internal sealed record LiveDebuggerTransitionReceipt(string[] Lines, int EventCount, int? ThreadId, ulong? Rip)
{
	internal static LiveDebuggerTransitionReceipt Parse(string[] lines, string nonce, string command)
	{
		if (lines.Length is < 3 or > 18 || lines.Any(line => line.Length > 256) ||
			lines[0] != "ok" || lines[1] != nonce || lines[2] != command)
		{
			throw new InvalidDataException("The private debugger diagnostic returned a mismatched or failed receipt.");
		}

		if (command is "install" or "restore")
		{
			return lines.Length == 3 ? new(lines, 0, null, null)
				: throw new InvalidDataException("The diagnostic acknowledgement contains unexpected fields.");
		}

		if (command != "sample" || lines.Length != 18 ||
			!int.TryParse(lines[15], NumberStyles.None, CultureInfo.InvariantCulture, out int count) || count is < 0 or > 1)
		{
			throw new InvalidDataException("The debugger diagnostic event count is invalid.");
		}

		if (count == 0)
		{
			return lines[16].Length == 0 && lines[17].Length == 0 ? new(lines, 0, null, null)
				: throw new InvalidDataException("The diagnostic reported an event without an event count.");
		}

		if (!int.TryParse(lines[16], NumberStyles.None, CultureInfo.InvariantCulture, out int threadId) || threadId <= 0 ||
			lines[17].Length is < 1 or > 16 ||
			!ulong.TryParse(lines[17], NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out ulong rip) || rip == 0)
		{
			throw new InvalidDataException("The private debugger diagnostic event identity is invalid.");
		}

		return new(lines, count, threadId, rip);
	}
}
