using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;

namespace CheatEngine.Mcp.Tests.LiveQualification;

/// <summary>Renders a bounded, qualification-only CE selection wrapper before the plugin is loaded.</summary>
internal static class LiveProcessSelectionBridge
{
	private const int MaximumManifestBytes = 4096;
	private const int MaximumReceipts = 32;

	internal static async Task<RenderedProcessSelectionBridge> RenderAsync(string manifestPath,
		CancellationToken cancellationToken = default)
	{
		if (!Path.IsPathFullyQualified(manifestPath))
		{
			throw new ArgumentException("The target manifest path must be absolute.", nameof(manifestPath));
		}

		FileInfo manifestFile = new(manifestPath);
		if (manifestFile.Length is <= 0 or > MaximumManifestBytes)
		{
			throw new InvalidDataException($"The target manifest must contain 1 to {MaximumManifestBytes} bytes.");
		}

		string json = await File.ReadAllTextAsync(manifestPath, cancellationToken);
		if (json.Length > MaximumManifestBytes)
		{
			throw new InvalidDataException($"The target manifest exceeds {MaximumManifestBytes} characters.");
		}

		JsonNode manifest = JsonNode.Parse(json) ?? throw new InvalidDataException("The target manifest is empty.");
		int processId = manifest["processId"]?.GetValue<int>() ??
			throw new InvalidDataException("The target manifest omitted processId.");
		string address = manifest["address"]?.GetValue<string>() ??
			throw new InvalidDataException("The target manifest omitted address.");
		string hexadecimalAddress = address.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? address[2..] : address;
		if (processId <= 0 || !ulong.TryParse(hexadecimalAddress, NumberStyles.AllowHexSpecifier,
			CultureInfo.InvariantCulture, out ulong parsedAddress) || parsedAddress == 0)
		{
			throw new InvalidDataException("The target manifest contains an invalid process identity or address.");
		}

		string directory = Path.GetDirectoryName(manifestPath) ?? throw new InvalidDataException("The manifest has no parent directory.");
		string receiptPath = Path.Combine(directory, Path.GetFileNameWithoutExtension(manifestPath) + "-process-selection.jsonl");
		return new RenderedProcessSelectionBridge(processId, address, receiptPath,
			Render(processId, parsedAddress, receiptPath));
	}

	internal static string Render(int processId, ulong address, string receiptPath)
	{
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(processId);
		if (!Path.IsPathFullyQualified(receiptPath))
		{
			throw new ArgumentException("The receipt path must be absolute.", nameof(receiptPath));
		}

		string target = processId.ToString(CultureInfo.InvariantCulture);
		string probe = address.ToString(CultureInfo.InvariantCulture);
		string output = '"' + string.Concat(Encoding.UTF8.GetBytes(receiptPath)
			.Select(value => "\\" + value.ToString("D3", CultureInfo.InvariantCulture))) + '"';
		return "\n" + $$"""
		local mcp_selection_original_open = openProcess
		local mcp_selection_receipts = 0
		local mcp_selection_target = {{target}}
		local mcp_selection_address = {{probe}}
		local mcp_selection_output = {{output}}
		local function mcp_selection_text(value)
		  local ok, text = pcall(tostring, value)
		  if not ok then text = '<unprintable>' end
		  if #text > 256 then text = string.sub(text, 1, 256) end
		  return text:gsub('[%z\1-\31\\"]', function(character)
		    if character == '\\' then return '\\\\' end
		    if character == '"' then return '\\"' end
		    return string.format('\\u%04X', string.byte(character))
		  end)
		end
		local function mcp_selection_emit(beforeId, beforeHandle, afterId, afterHandle, returned, resultCount, readValue, failure)
		  if mcp_selection_receipts >= {{MaximumReceipts}} then return end
		  mcp_selection_receipts = mcp_selection_receipts + 1
		  pcall(function()
		    local file = assert(io.open(mcp_selection_output, 'ab'))
		    file:write('{"beforeProcessId":"' .. mcp_selection_text(beforeId) .. '","beforeHandle":"' .. mcp_selection_text(beforeHandle) ..
		      '","afterProcessId":"' .. mcp_selection_text(afterId) .. '","afterHandle":"' .. mcp_selection_text(afterHandle) ..
		      '","openReturned":' .. tostring(returned == true) .. ',"resultCount":' .. tostring(resultCount) ..
		      ',"openReturnType":"' .. type(returned) .. '","openReturnValue":"' .. mcp_selection_text(returned) .. '"' ..
		      ',"ceReadSucceeded":' .. tostring(readValue ~= nil) .. ',"ceValue":"' .. mcp_selection_text(readValue) ..
		      '","failure":"' .. mcp_selection_text(failure or '') .. '"}\n')
		    file:close()
		  end)
		end
		openProcess = function(...)
		  if select(1, ...) ~= mcp_selection_target then return mcp_selection_original_open(...) end
		  local beforeId, beforeHandle, afterId, afterHandle, value = '', '', '', '', nil
		  local diagnosticFailure = ''
		  local before = table.pack(pcall(function()
		    beforeId = getOpenedProcessID()
		    beforeHandle = getOpenedProcessHandle()
		  end))
		  if not before[1] then diagnosticFailure = mcp_selection_text(before[2]) end
		  local result = table.pack(pcall(mcp_selection_original_open, ...))
		  local after = table.pack(pcall(function()
		    afterId = getOpenedProcessID()
		    afterHandle = getOpenedProcessHandle()
		    value = readInteger(mcp_selection_address, true)
		  end))
		  if not after[1] and diagnosticFailure == '' then diagnosticFailure = mcp_selection_text(after[2]) end
		  mcp_selection_emit(beforeId, beforeHandle, afterId, afterHandle, result[2], result.n - 1, value,
		    result[1] and diagnosticFailure or result[2])
		  if not result[1] then error(result[2], 0) end
		  return table.unpack(result, 2, result.n)
		end
		""" + "\n";
	}
}

internal sealed record RenderedProcessSelectionBridge(int ProcessId, string Address, string ReceiptPath, string Source);
