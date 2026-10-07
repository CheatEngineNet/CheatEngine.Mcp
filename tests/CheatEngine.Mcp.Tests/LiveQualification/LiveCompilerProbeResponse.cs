using System.Globalization;
using System.Runtime.Versioning;
using System.Text;
using System.Text.Json.Nodes;

namespace CheatEngine.Mcp.Tests.LiveQualification;

/// <summary>Reads only bounded, complete responses from the fixed private compiler bridge.</summary>
internal static class LiveCompilerProbeResponse
{
	[SupportedOSPlatform("windows")]
	internal static async Task<JsonNode> ReadAsync(string path, string command)
	{
		LiveCompilerCandidate.RequireNoReparseAncestors(path);
		using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read);
		LiveCompilerCandidate.RequireNoReparseAncestors(path);
		if (stream.Length > 32_768)
		{
			throw new InvalidDataException("The fixed compiler response exceeds its byte bound.");
		}
		using StreamReader reader = new(stream, new UTF8Encoding(false, true), false);
		List<string> lines = [];
		while (await reader.ReadLineAsync(TestContext.Current.CancellationToken) is { } line)
		{
			lines.Add(line);
			if (lines.Count > 4)
			{
				throw new InvalidDataException("The fixed compiler response has extra lines.");
			}
		}
		return Parse([.. lines], command);
	}

	internal static JsonNode Parse(string[] lines, string command)
	{
		if (command is not ("status" or "compile") || lines.Length != (command == "status" ? 3 : 4) ||
			lines[0] != "ok" || !bool.TryParse(lines[1], out bool available) ||
			!int.TryParse(lines[2], NumberStyles.None, CultureInfo.InvariantCulture, out int receipts) ||
			(command == "compile" && string.IsNullOrEmpty(lines[3])))
		{
			throw new InvalidDataException("Fixed compiler bridge returned an invalid bounded response.");
		}
		JsonObject result = new()
		{
			["available"] = available,
			["receiptCount"] = receipts
		};
		if (command == "compile")
		{
			result[available ? "assemblyPath" : "diagnostic"] = lines[3];
		}
		return result;
	}
}
