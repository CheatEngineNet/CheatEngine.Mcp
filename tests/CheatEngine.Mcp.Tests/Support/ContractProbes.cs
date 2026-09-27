using System.ComponentModel;

using CheatEngine.Client.Results;
using CheatEngine.Mcp.Core.Contract;

using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace CheatEngine.Mcp.Tests.Support;

/// <summary>A v2 result record: an object-shaped output schema and structured content.</summary>
/// <param name="Text">The echoed text.</param>
/// <param name="Values">The echoed values.</param>
/// <param name="Flag">The echoed flag.</param>
/// <param name="Count">The echoed count.</param>
/// <param name="Label">The echoed label; omitted when null.</param>
public sealed record ContractProbeResult(string Text, string[] Values, bool Flag, int Count, string? Label);

/// <summary>The details of the probe's partial effect.</summary>
/// <param name="Completed">The number of completed steps.</param>
/// <param name="FailedIndex">The index of the failed step.</param>
public sealed record ContractProbeDetails(int Completed, int FailedIndex);

/// <summary>
///     Static probe tools that drive the Core filters through a real MCP server: one v2 tool, one that raises the failure
///     its argument names, and one legacy tool.
/// </summary>
[McpServerToolType]
public sealed class ContractProbeTool
{
	// v2 probes carry frozen catalog names: the startup validator admits no other v2 tool name.
	internal const string ConvertName = CheatEngineToolNames.UtilConvertValue;
	internal const string FailName = CheatEngineToolNames.UtilCalculate;
	internal const string LegacyName = "legacy_probe";
	internal const string SecretMessage = @"C:\Users\secret\token-1234";

	[McpServerTool(Name = ConvertName, Title = "Convert a probe value", ReadOnly = true, Destructive = false,
		Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
	[McpMeta(McpDispatchClass.MetaKey, McpDispatchClass.Short)]
	[Description("Echoes its arguments as a v2 record.")]
	public static ContractProbeResult Convert(
		[Description("Text echoed back; never rewritten.")]
		string text,
		[Description("Values echoed back.")] string[]? values = null,
		[Description("A flag echoed back.")] bool flag = false,
		[Description("A count echoed back.")] int count = 1,
		[Description("A label echoed back.")] string? label = "default")
	{
		return new ContractProbeResult(text, values ?? [], flag, count, label);
	}

	[McpServerTool(Name = FailName, Title = "Raise a probe failure", ReadOnly = true, Destructive = false,
		Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
	[McpMeta(McpDispatchClass.MetaKey, McpDispatchClass.Short)]
	[Description("Raises the failure its argument names.")]
	public static ContractProbeResult Fail([Description("The failure to raise.")] string failure)
	{
		throw failure switch
		{
			"not_found" => CheatEngineToolException.NotFound("The probe object is missing.", "List probes first."),
			"limit" => CheatEngineToolException.LimitExceeded("count", "must be at most 4."),
			"partial" => CheatEngineToolException.PartialEffect("Only the first step completed.",
				ToolHostEffect.Started, new ContractProbeDetails(1, 1), TestJsonContext.Default.ContractProbeDetails),
			"client_write" => new CheatEngineFailure(CheatEngineFailureKind.MemoryWriteFailed, "Memory.WriteBytes",
				"The write was refused.", hostEffect: CheatEngineHostEffect.Started).ToException(),
			"client_cancelled" => new CheatEngineFailure(CheatEngineFailureKind.Cancelled, "Memory.ReadBytes",
				"The read was cancelled.", hostEffect: CheatEngineHostEffect.NotStarted).ToException(),
			"argument" => new ArgumentException("The probe argument is malformed.", nameof(failure)),
			"protocol" => new McpProtocolException("The probe request is invalid.", McpErrorCode.InvalidRequest),
			_ => new InvalidOperationException(SecretMessage)
		};
	}

	[McpServerTool(Name = LegacyName)]
	[Description("A legacy tool that reports its result in band.")]
	public static object Legacy([Description("A value echoed back.")] int value)
	{
		return new
		{
			success = true,
			value
		};
	}
}

/// <summary>A static (Local) probe resource whose URI selects the failure it raises.</summary>
[McpServerResourceType]
public sealed class ContractProbeResource
{
	internal const string UriPrefix = "cheatengine://docs/probes/";

	[McpServerResource(UriTemplate = UriPrefix + "{kind}", Name = "probe_resource", Title = "Probe resource",
		MimeType = "text/markdown")]
	[Description("Reads a probe resource or raises the failure its kind names.")]
	public static string Read([Description("The failure kind, or ok.")] string kind)
	{
		return kind switch
		{
			"ok" => "probe",
			"not_found" => throw CheatEngineToolException.NotFound("The probe resource is missing.", "List probes."),
			"invalid" => throw CheatEngineToolException.InvalidArgument("kind", "is not a probe kind."),
			"client" => throw new CheatEngineFailure(CheatEngineFailureKind.TargetNotAttached, "Process.Current",
				"No process is attached.", hostEffect: CheatEngineHostEffect.NotStarted).ToException(),
			_ => throw CheatEngineToolException.Busy("The probe is busy.")
		};
	}
}
