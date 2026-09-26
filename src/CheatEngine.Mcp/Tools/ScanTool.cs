using System.ComponentModel;
using System.Globalization;

using CheatEngine.Client;
using CheatEngine.Client.Results;
using CheatEngine.Client.Scanning;
using CheatEngine.SDK.Engine.Inspection;
using CheatEngine.SDK.Engine.Values;

using ModelContextProtocol.Server;

namespace CheatEngine.Mcp.Tools;

[McpServerToolType]
public sealed class ScanTool : IDisposable
{
	private const int MaximumScanners = 32;
	private const int MaximumScannerNameLength = 256;
	private const int MaximumAobBytes = 64 * 1024;
	private const int MaximumScanValueCharacters = 1024 * 1024;
	private readonly ICheatEngineClient _client;
	private readonly TargetResources? _targetResources;
	private readonly Dictionary<string, IValueScanSession> _sessions = new(StringComparer.Ordinal);
	private readonly Dictionary<string, ValueScanValueType> _valueTypes = new(StringComparer.Ordinal);
	private bool _disposed;

	public ScanTool(ICheatEngineClient client, TargetResources? targetResources = null)
	{
		ArgumentNullException.ThrowIfNull(client);
		_client = client;
		_targetResources = targetResources;
	}

	[McpServerTool(Name = "aob_scan"), Description("Run a bounded AOB scan using CheatEngine.Client.")]
	public object AobScan([Description("Hexadecimal bytes and ?? wildcard tokens.")] string pattern,
		[Description("Maximum copied matches (1-65535).")] int maximumResults = 1000,
		[Description("Optional module name that bounds the scan.")] string? moduleName = null,
		[Description("Optional inclusive first match address; requires endAddress.")] string? startAddress = null,
		[Description("Optional inclusive last match address; requires startAddress.")] string? endAddress = null)
	{
		return ToolExecution.Run(_client, () =>
		{
			if (maximumResults is < 1 or > 65_535)
			{
				return ToolExecution.Error("maximumResults must be between 1 and 65535.");
			}

			if (pattern.Length > (MaximumAobBytes * 3))
			{
				return ToolExecution.Error("pattern must not exceed 65536 bytes.");
			}

			if ((startAddress is null) != (endAddress is null))
			{
				return ToolExecution.Error("startAddress and endAddress must be supplied together.");
			}

			ModuleName? module = string.IsNullOrWhiteSpace(moduleName) ? null : new ModuleName(moduleName);
			AobScanRange? range = startAddress is null ? null : new AobScanRange(ToolExecution.Address(_client, startAddress), ToolExecution.Address(_client, endAddress!));
			AobPattern parsedPattern = new(pattern);
			if (parsedPattern.ByteLength > MaximumAobBytes)
			{
				return ToolExecution.Error("pattern must not exceed 65536 bytes.");
			}

			AobScanResult result = _client.Patterns.Scan(new AobScanRequest(parsedPattern, maximumResults, module, range));
			string[] addresses = result.Matches.Select(address => $"0x{address.Value:X}").ToArray();
			return new
			{
				success = true,
				addresses,
				truncated = result.IsTruncated
			};
		});
	}

	[McpServerTool(Name = "aob_scan_unique"), Description("Find at most one AOB match; truncated results are not proof of uniqueness.")]
	public object AobScanUnique([Description("Hexadecimal bytes and ?? wildcard tokens.")] string pattern,
		[Description("Optional module name that bounds the scan.")] string? moduleName = null)
	{
		return ToolExecution.Run(_client, () =>
		{
			if (pattern.Length > (MaximumAobBytes * 3))
			{
				return ToolExecution.Error("pattern must not exceed 65536 bytes.");
			}

			ModuleName? module = string.IsNullOrWhiteSpace(moduleName) ? null : new ModuleName(moduleName);
			AobPattern parsedPattern = new(pattern);
			if (parsedPattern.ByteLength > MaximumAobBytes)
			{
				return ToolExecution.Error("pattern must not exceed 65536 bytes.");
			}

			AobScanResult result = _client.Patterns.Scan(new AobScanRequest(parsedPattern, 2, module));
			if (result.IsTruncated || result.Matches.Length > 1)
			{
				return ToolExecution.Error("The AOB pattern is not proven unique.");
			}

			Address? address = result.Matches.Length == 0 ? null : result.Matches[0];
			return new
			{
				success = true,
				found = address.HasValue,
				address = address is { } value ? $"0x{value.Value:X}" : null
			};
		});
	}

	[McpServerTool(Name = "memory_scan"), Description("Start a named Client value scan. Results are owned until reset.")]
	public object MemoryScan([Description("Unique name for this server-owned scan session.")] string scannerName,
		[Description("byte, int16, int32, int64, float, double, string, wstring, or bytes.")] string valueType,
		[Description("Value for exact, between, greater, or less comparisons; omit for unknown initial value.")] string? value = null,
		[Description("exact, unknown, between, greater, or less.")] string comparison = "exact",
		[Description("Inclusive upper value for a between comparison.")] string? upperValue = null)
	{
		return ToolExecution.Run(_client, () =>
		{
			if (ValidateScannerName(scannerName) is { } scannerNameError)
			{
				return ToolExecution.Error(scannerNameError);
			}

			if (ValidateValueLength(value, nameof(value)) is { } valueError)
			{
				return ToolExecution.Error(valueError);
			}

			if (ValidateValueLength(upperValue, nameof(upperValue)) is { } upperValueError)
			{
				return ToolExecution.Error(upperValueError);
			}

			ValueScanFirstRequest request = CreateFirstRequest(valueType, comparison, value, upperValue);
			if (!_sessions.TryGetValue(scannerName, out IValueScanSession? session))
			{
				if (_sessions.Count >= MaximumScanners)
				{
					return ToolExecution.Error("At most 32 named scan sessions may be active.");
				}

				session = _client.ValueScans.CreateSession();
				_sessions.Add(scannerName, session);
				_targetResources?.Track(session, () => RemoveSession(scannerName), new
				{
					kind = "scan",
					name = scannerName
				});
			}
			if (session.State != ValueScanSessionState.Created)
			{
				return ToolExecution.Error("Reset the named scan before starting another first scan.");
			}

			session.FirstScan(request);
			_valueTypes[scannerName] = request.ValueType;
			return ScanSummary(session);
		});
	}

	[McpServerTool(Name = "next_memory_scan"), Description("Narrow a named Client value scan with a comparison.")]
	public object NextMemoryScan([Description("Name of an existing completed scan session.")] string scannerName,
		[Description("Value for exact, between, greater, less, increasedBy, or decreasedBy comparisons; omit for state comparisons.")] string? value = null,
		[Description("exact, between, greater, less, increased, decreased, increasedBy, decreasedBy, changed, or unchanged.")] string comparison = "exact",
		[Description("Inclusive upper value for a between comparison.")] string? upperValue = null)
	{
		return ToolExecution.Run(_client, () =>
		{
			if (!_sessions.TryGetValue(scannerName, out IValueScanSession? session) || !_valueTypes.TryGetValue(scannerName, out ValueScanValueType type))
			{
				return ToolExecution.Error("No scan exists with that scannerName.");
			}

			if (ValidateValueLength(value, nameof(value)) is { } valueError)
			{
				return ToolExecution.Error(valueError);
			}

			if (ValidateValueLength(upperValue, nameof(upperValue)) is { } upperValueError)
			{
				return ToolExecution.Error(upperValueError);
			}

			if (session.State != ValueScanSessionState.ResultsReady)
			{
				return ToolExecution.Error("The named scan has no completed results to narrow.");
			}

			ValueScanNextRequest request = CreateNextRequest(type, comparison, value, upperValue);
			session.NextScan(request);
			return ScanSummary(session);
		});
	}

	[McpServerTool(Name = "get_memory_scan_results"), Description("Read one bounded page from a named value scan.")]
	public object GetMemoryScanResults([Description("Name of an existing completed scan session.")] string scannerName,
		[Description("Zero-based result index.")] long startIndex = 0,
		[Description("Maximum copied results (1-1024).")] int maximumResults = 1000)
	{
		return ToolExecution.Run(_client, () =>
		{
			if (!_sessions.TryGetValue(scannerName, out IValueScanSession? session))
			{
				return ToolExecution.Error("No scan exists with that scannerName.");
			}

			if (maximumResults is < 1 or > 1024)
			{
				return ToolExecution.Error("maximumResults must be between 1 and 1024.");
			}

			ValueScanPage page = session.Read(new ValueScanReadRequest(startIndex, maximumResults));
			object[] results = page.Matches.Select(match => (object) new { address = $"0x{match.Address.Value:X}", value = match.ValueText }).ToArray();
			return new
			{
				success = true,
				count = page.ResultCount,
				results,
				nextStartIndex = page.NextStartIndex,
				hasMore = page.HasMore
			};
		});
	}

	[McpServerTool(Name = "reset_memory_scan"), Description("Release a named value-scan session and its resources.")]
	public object ResetMemoryScan([Description("Name of the server-owned scan session to release.")] string scannerName)
	{
		return ToolExecution.Run(_client, () =>
		{
			if (!_sessions.TryGetValue(scannerName, out IValueScanSession? session))
			{
				return ToolExecution.Error("No scan exists with that scannerName.");
			}

			LeaseReleaseOutcome release = session.Release();
			if (!release.IsRetryable)
			{
				_targetResources?.Forget(session);
				RemoveSession(scannerName);
			}

			return new
			{
				success = release.IsComplete,
				scannerName,
				release,
				requiresManualRecovery = release.RequiresManualRecovery
			};
		});
	}

	public void Dispose()
	{
		if (_disposed)
		{
			return;
		}

		_disposed = true;
		foreach ((string name, IValueScanSession session) in _sessions.ToArray())
		{
			LeaseReleaseOutcome outcome = session.Release();
			if (!outcome.IsRetryable)
			{
				_targetResources?.Forget(session);
				RemoveSession(name);
			}
		}
	}

	private static object ScanSummary(IValueScanSession session) => new { success = true, count = session.GetResultCount() };

	private void RemoveSession(string name)
	{
		_sessions.Remove(name);
		_valueTypes.Remove(name);
	}

	private static string? ValidateScannerName(string scannerName)
	{
		if (string.IsNullOrWhiteSpace(scannerName))
		{
			return "scannerName is required.";
		}

		return scannerName.Length > MaximumScannerNameLength ? "scannerName must not exceed 256 characters." : null;
	}

	private static string? ValidateValueLength(string? value, string parameterName) =>
		value is { Length: > MaximumScanValueCharacters } ? $"{parameterName} must not exceed 1048576 characters." : null;

	private static ValueScanFirstRequest CreateFirstRequest(string valueType, string comparison, string? value,
		string? upperValue)
	{
		ValueScanValueType type = ParseValueType(valueType);
		return NormalizeComparison(comparison) switch
		{
			"exact" => ValueScanFirstRequest.Exact(ParseRequiredValue(type, value, nameof(value))),
			"unknown" => RequireNoValues(value, upperValue, "unknown") is { } error
				? throw new ArgumentException(error) : ValueScanFirstRequest.UnknownInitialValue(type),
			"between" => ValueScanFirstRequest.Between(ParseRequiredValue(type, value, nameof(value)),
				ParseRequiredValue(type, upperValue, nameof(upperValue))),
			"greater" => ValueScanFirstRequest.BiggerThan(ParseRequiredValue(type, value, nameof(value))),
			"less" => ValueScanFirstRequest.SmallerThan(ParseRequiredValue(type, value, nameof(value))),
			_ => throw new ArgumentException("comparison must be exact, unknown, between, greater, or less.")
		};
	}

	private static ValueScanNextRequest CreateNextRequest(ValueScanValueType type, string comparison, string? value,
		string? upperValue)
	{
		return NormalizeComparison(comparison) switch
		{
			"exact" => ValueScanNextRequest.Exact(ParseRequiredValue(type, value, nameof(value))),
			"between" => ValueScanNextRequest.Between(ParseRequiredValue(type, value, nameof(value)),
				ParseRequiredValue(type, upperValue, nameof(upperValue))),
			"greater" => ValueScanNextRequest.BiggerThan(ParseRequiredValue(type, value, nameof(value))),
			"less" => ValueScanNextRequest.SmallerThan(ParseRequiredValue(type, value, nameof(value))),
			"increased" => RequireNoValues(value, upperValue, "increased") is { } error
				? throw new ArgumentException(error) : ValueScanNextRequest.Increased(),
			"decreased" => RequireNoValues(value, upperValue, "decreased") is { } error
				? throw new ArgumentException(error) : ValueScanNextRequest.Decreased(),
			"increasedby" => ValueScanNextRequest.IncreasedBy(ParseRequiredValue(type, value, nameof(value))),
			"decreasedby" => ValueScanNextRequest.DecreasedBy(ParseRequiredValue(type, value, nameof(value))),
			"changed" => RequireNoValues(value, upperValue, "changed") is { } error
				? throw new ArgumentException(error) : ValueScanNextRequest.Changed(),
			"unchanged" => RequireNoValues(value, upperValue, "unchanged") is { } error
				? throw new ArgumentException(error) : ValueScanNextRequest.Unchanged(),
			_ => throw new ArgumentException("comparison must be exact, between, greater, less, increased, decreased, increasedBy, decreasedBy, changed, or unchanged.")
		};
	}

	private static string NormalizeComparison(string? comparison) => comparison?.Trim().ToLowerInvariant().Replace("_", string.Empty, StringComparison.Ordinal) switch
	{
		null => string.Empty,
		"unknowninitial" or "unknowninitialvalue" => "unknown",
		"greaterthan" or "bigger" or "biggerthan" => "greater",
		"lessthan" or "smaller" or "smallerthan" => "less",
		string value => value
	};

	private static string? RequireNoValues(string? value, string? upperValue, string comparison) =>
		value is null && upperValue is null ? null : $"comparison '{comparison}' does not accept value or upperValue.";

	private static ValueScanValue ParseRequiredValue(ValueScanValueType type, string? value, string parameterName)
	{
		if (value is null)
		{
			throw new ArgumentException($"{parameterName} is required for this comparison.");
		}

		return ParseValue(type, value);
	}

	private static ValueScanValueType ParseValueType(string valueType) => valueType.ToLowerInvariant() switch
	{
		"byte" or "integer8" => ValueScanValueType.Integer8,
		"int16" or "integer16" => ValueScanValueType.Integer16,
		"int32" or "integer32" or "int" => ValueScanValueType.Integer32,
		"int64" or "integer64" or "long" => ValueScanValueType.Integer64,
		"float" or "singlefloat" => ValueScanValueType.SingleFloat,
		"double" or "doublefloat" => ValueScanValueType.DoubleFloat,
		"string" or "utf8string" => ValueScanValueType.Utf8String,
		"wstring" or "utf16string" => ValueScanValueType.Utf16String,
		"bytes" or "bytearray" => ValueScanValueType.ByteArray,
		_ => throw new ArgumentException("valueType must be byte, int16, int32, int64, float, double, string, wstring, or bytes.")
	};

	private static ValueScanValue ParseValue(ValueScanValueType valueType, string value)
	{
		return valueType switch
		{
			ValueScanValueType.Integer8 => ValueScanValue.FromByte(byte.Parse(value, CultureInfo.InvariantCulture)),
			ValueScanValueType.Integer16 => ValueScanValue.FromInt16(short.Parse(value, CultureInfo.InvariantCulture)),
			ValueScanValueType.Integer32 => ValueScanValue.FromInt32(int.Parse(value, CultureInfo.InvariantCulture)),
			ValueScanValueType.Integer64 => ValueScanValue.FromInt64(long.Parse(value, CultureInfo.InvariantCulture)),
			ValueScanValueType.SingleFloat => ValueScanValue.FromSingle(float.Parse(value, CultureInfo.InvariantCulture), 6),
			ValueScanValueType.DoubleFloat => ValueScanValue.FromDouble(double.Parse(value, CultureInfo.InvariantCulture), 12),
			ValueScanValueType.Utf8String => ValueScanValue.FromUtf8String(value),
			ValueScanValueType.Utf16String => ValueScanValue.FromUtf16String(value),
			ValueScanValueType.ByteArray => ValueScanValue.FromBytes(ParseBytes(value)),
			_ => throw new ArgumentOutOfRangeException(nameof(valueType), valueType, "valueType is not supported.")
		};
	}

	private static byte[] ParseBytes(string value) => value.Split([' ', ','], StringSplitOptions.RemoveEmptyEntries).Select(token => byte.Parse(token, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture)).ToArray();
}
