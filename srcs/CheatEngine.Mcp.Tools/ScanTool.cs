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
	private const int MaximumScanValueCharacters = 1024 * 1024;
	private readonly ICheatEngineClient _client;
	private readonly Dictionary<string, IValueScanSession> _sessions = new(StringComparer.Ordinal);
	private readonly TargetResources? _targetResources;
	private readonly Dictionary<string, ValueScanValueType> _valueTypes = new(StringComparer.Ordinal);
	private bool _disposed;

	public ScanTool(ICheatEngineClient client, TargetResources? targetResources = null)
	{
		ArgumentNullException.ThrowIfNull(client);
		_client = client;
		_targetResources = targetResources;
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

	[McpServerTool(Name = "memory_scan")]
	[Description(
		"Start a value scan. The default main scanner uses CE's visible scan tab and UI options; poll its status for completion. Other names create independent Client sessions without changing the UI. Reset explicitly before another first scan.")]
	public object MemoryScan(
		[Description(
			"main (default) for the visible CE scan tab, or a unique case-sensitive name for an independent session (up to 32). The name main is reserved.")]
		string scannerName = "main",
		[Description("byte, int16, int32 (default), int64, float, double, string, wstring, or bytes.")]
		string valueType = "int32",
		[Description("Value for exact, between, greater, or less comparisons; omit for unknown initial value.")]
		string? value = null,
		[Description("exact, unknown, between, greater, or less.")]
		string comparison = "exact",
		[Description("Inclusive upper value for a between comparison.")]
		string? upperValue = null)
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
			if (scannerName == "main")
			{
				return MainScanner.First(_client, request);
			}

			if (!_sessions.TryGetValue(scannerName, out IValueScanSession? session))
			{
				if (_sessions.Count >= MaximumScanners)
				{
					return ToolExecution.Error("At most 32 named scan sessions may be active.");
				}

				session = _client.ValueScans.CreateSession();
				_sessions.Add(scannerName, session);
				_targetResources?.Track(session, "scan", () => RemoveSession(scannerName), scannerName);
			}

			if (session.State != ValueScanSessionState.Created)
			{
				return ToolExecution.Error("Reset the named scan before starting another first scan.");
			}

			session.FirstScan(request);
			_valueTypes[scannerName] = request.ValueType;
			return ScanSummary(scannerName, session);
		});
	}

	[McpServerTool(Name = "next_memory_scan")]
	[Description(
		"Narrow main's visible CE scan (including a manually started scan), or a named independent Client scan. Poll main's status until completion.")]
	public object NextMemoryScan(
		[Description("main (default) for the visible CE scan tab, or an existing independent scanner name.")]
		string scannerName = "main",
		[Description(
			"Value for exact, between, greater, less, increasedBy, or decreasedBy comparisons; omit for state comparisons.")]
		string? value = null,
		[Description(
			"exact, between, greater, less, increased, decreased, increasedBy, decreasedBy, changed, or unchanged.")]
		string comparison = "exact",
		[Description("Inclusive upper value for a between comparison.")]
		string? upperValue = null)
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

			if (scannerName == "main")
			{
				ValueScanValueType mainType = ParseValueType(MainScanner.ValueType(_client));
				return MainScanner.Next(_client, CreateNextRequest(mainType, comparison, value, upperValue));
			}

			if (!_sessions.TryGetValue(scannerName, out IValueScanSession? session) ||
				!_valueTypes.TryGetValue(scannerName, out ValueScanValueType type))
			{
				return ToolExecution.Error("No scan exists with that scannerName.");
			}

			if (session.State != ValueScanSessionState.ResultsReady)
			{
				return ToolExecution.Error("The named scan has no completed results to narrow.");
			}

			ValueScanNextRequest request = CreateNextRequest(type, comparison, value, upperValue);
			session.NextScan(request);
			return ScanSummary(scannerName, session);
		});
	}

	[McpServerTool(Name = "get_memory_scan_results")]
	[Description(
		"Read a bounded page from main's visible CE found list, including manual scans, or an independent Client session. Main must have completed; unknown-initial baselines need a next scan first.")]
	public object GetMemoryScanResults(
		[Description("main (default) for the visible CE scan tab, or an existing independent scanner name.")]
		string scannerName = "main",
		[Description("Zero-based result index.")]
		long startIndex = 0,
		[Description("Maximum copied results (1-1024).")]
		int maximumResults = 1000)
	{
		return ToolExecution.Run(_client, () =>
		{
			if (ValidateScannerName(scannerName) is { } scannerNameError)
			{
				return ToolExecution.Error(scannerNameError);
			}

			if (maximumResults is < 1 or > 1024)
			{
				return ToolExecution.Error("maximumResults must be between 1 and 1024.");
			}

			if (startIndex < 0 || startIndex > long.MaxValue - maximumResults)
			{
				return ToolExecution.Error("startIndex must be nonnegative and leave room for maximumResults.");
			}

			if (scannerName == "main")
			{
				return MainScanner.Read(_client, startIndex, maximumResults);
			}

			if (!_sessions.TryGetValue(scannerName, out IValueScanSession? session))
			{
				return ToolExecution.Error("No scan exists with that scannerName.");
			}

			ValueScanPage page = session.Read(new ValueScanReadRequest(startIndex, maximumResults));
			object[] results = page.Matches.Select(match =>
				(object) new
				{
					address = $"0x{match.Address.Value:X}",
					value = match.ValueText
				}).ToArray();
			return new
			{
				success = true,
				scannerName,
				mode = "independent",
				count = page.ResultCount,
				results,
				nextStartIndex = page.NextStartIndex,
				hasMore = page.HasMore
			};
		});
	}

	[McpServerTool(Name = "reset_memory_scan")]
	[Description(
		"Reset main through CE's New Scan action (clearing visible results), or release one independent Client session. Refuses to reset a running UI scan; cancel it in CE first.")]
	public object ResetMemoryScan(
		[Description("main (default) for the visible CE scan tab, or the independent scanner name to release.")]
		string scannerName = "main")
	{
		return ToolExecution.Run(_client, () =>
		{
			if (ValidateScannerName(scannerName) is { } scannerNameError)
			{
				return ToolExecution.Error(scannerNameError);
			}

			if (scannerName == "main")
			{
				return MainScanner.Reset(_client);
			}

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

	[McpServerTool(Name = "get_memory_scan_status")]
	[Description(
		"Get main's live UI scan state or an independent session's state. Poll main until ResultsReady or BaselineReady before reading or narrowing; Scanning is still in progress.")]
	public object GetMemoryScanStatus(
		[Description("main (default) for the visible CE scan tab, or an existing independent scanner name.")]
		string scannerName = "main")
	{
		return ToolExecution.Run(_client, () =>
		{
			if (ValidateScannerName(scannerName) is { } error)
			{
				return ToolExecution.Error(error);
			}

			return scannerName == "main" ? MainScanner.Status(_client)
				: _sessions.TryGetValue(scannerName, out IValueScanSession? session) ? ScanSummary(scannerName, session)
				: ToolExecution.Error("No scan exists with that scannerName.");
		});
	}

	[McpServerTool(Name = "list_memory_scanners")]
	[Description(
		"List main (the currently visible CE scan tab) and up to 32 independent Client scan sessions in this instance. Independent scanner names do not refer to CE UI tabs.")]
	public object ListMemoryScanners()
	{
		return ToolExecution.Run(_client, () =>
		{
			List<object> scanners = [MainScanner.Status(_client)];
			scanners.AddRange(_sessions.Select(pair => ScanSummary(pair.Key, pair.Value)));
			return new
			{
				success = true,
				scanners
			};
		});
	}

	internal object? PrepareForTargetChange()
	{
		return MainScanner.PrepareForTargetChange(_client);
	}

	private object ScanSummary(string name, IValueScanSession session)
	{
		return new
		{
			success = true,
			scannerName = name,
			mode = "independent",
			state = session.State.ToString(),
			resultsReady = session.State == ValueScanSessionState.ResultsReady,
			count = session.State == ValueScanSessionState.ResultsReady ? (ulong?) session.GetResultCount() : null,
			valueType = _valueTypes.TryGetValue(name, out ValueScanValueType type) ? MainScanner.TypeName(type) : null
		};
	}

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

	private static string? ValidateValueLength(string? value, string parameterName)
	{
		return value is { Length: > MaximumScanValueCharacters }
			? $"{parameterName} must not exceed 1048576 characters."
			: null;
	}

	private static ValueScanFirstRequest CreateFirstRequest(string valueType, string comparison, string? value,
		string? upperValue)
	{
		ValueScanValueType type = ParseValueType(valueType);
		return NormalizeComparison(comparison) switch
		{
			"exact" => ValueScanFirstRequest.Exact(ParseRequiredValue(type, value, nameof(value))),
			"unknown" => RequireNoValues(value, upperValue, "unknown") is { } error
				? throw new ArgumentException(error)
				: ValueScanFirstRequest.UnknownInitialValue(type),
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
				? throw new ArgumentException(error)
				: ValueScanNextRequest.Increased(),
			"decreased" => RequireNoValues(value, upperValue, "decreased") is { } error
				? throw new ArgumentException(error)
				: ValueScanNextRequest.Decreased(),
			"increasedby" => ValueScanNextRequest.IncreasedBy(ParseRequiredValue(type, value, nameof(value))),
			"decreasedby" => ValueScanNextRequest.DecreasedBy(ParseRequiredValue(type, value, nameof(value))),
			"changed" => RequireNoValues(value, upperValue, "changed") is { } error
				? throw new ArgumentException(error)
				: ValueScanNextRequest.Changed(),
			"unchanged" => RequireNoValues(value, upperValue, "unchanged") is { } error
				? throw new ArgumentException(error)
				: ValueScanNextRequest.Unchanged(),
			_ => throw new ArgumentException(
				"comparison must be exact, between, greater, less, increased, decreased, increasedBy, decreasedBy, changed, or unchanged.")
		};
	}

	private static string NormalizeComparison(string? comparison)
	{
		return comparison?.Trim().ToLowerInvariant().Replace("_", string.Empty, StringComparison.Ordinal) switch
		{
			null => string.Empty,
			"unknowninitial" or "unknowninitialvalue" => "unknown",
			"greaterthan" or "bigger" or "biggerthan" => "greater",
			"lessthan" or "smaller" or "smallerthan" => "less",
			string value => value
		};
	}

	private static string? RequireNoValues(string? value, string? upperValue, string comparison)
	{
		return value is null && upperValue is null
			? null
			: $"comparison '{comparison}' does not accept value or upperValue.";
	}

	private static ValueScanValue ParseRequiredValue(ValueScanValueType type, string? value, string parameterName)
	{
		if (value is null)
		{
			throw new ArgumentException($"{parameterName} is required for this comparison.");
		}

		return ParseValue(type, value);
	}

	private static ValueScanValueType ParseValueType(string valueType)
	{
		return valueType.ToLowerInvariant() switch
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
			_ => throw new ArgumentException(
				"valueType must be byte, int16, int32, int64, float, double, string, wstring, or bytes.")
		};
	}

	private static ValueScanValue ParseValue(ValueScanValueType valueType, string value)
	{
		return valueType switch
		{
			ValueScanValueType.Integer8 => ValueScanValue.FromByte(byte.Parse(value, CultureInfo.InvariantCulture)),
			ValueScanValueType.Integer16 => ValueScanValue.FromInt16(short.Parse(value, CultureInfo.InvariantCulture)),
			ValueScanValueType.Integer32 => ValueScanValue.FromInt32(int.Parse(value, CultureInfo.InvariantCulture)),
			ValueScanValueType.Integer64 => ValueScanValue.FromInt64(long.Parse(value, CultureInfo.InvariantCulture)),
			ValueScanValueType.SingleFloat => ValueScanValue.FromSingle(
				float.Parse(value, CultureInfo.InvariantCulture), 6),
			ValueScanValueType.DoubleFloat => ValueScanValue.FromDouble(
				double.Parse(value, CultureInfo.InvariantCulture), 12),
			ValueScanValueType.Utf8String => ValueScanValue.FromUtf8String(value),
			ValueScanValueType.Utf16String => ValueScanValue.FromUtf16String(value),
			ValueScanValueType.ByteArray => ValueScanValue.FromBytes(ParseBytes(value)),
			_ => throw new ArgumentOutOfRangeException(nameof(valueType), valueType, "valueType is not supported.")
		};
	}

	private static byte[] ParseBytes(string value)
	{
		return value.Split([' ', ','], StringSplitOptions.RemoveEmptyEntries).Select(token =>
			byte.Parse(token, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture)).ToArray();
	}
}
