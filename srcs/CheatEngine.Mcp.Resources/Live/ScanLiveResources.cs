using System.ComponentModel;
using System.Text.Json;

using CheatEngine.Mcp.Core.Contract;
using CheatEngine.Mcp.Core.Targets;
using CheatEngine.Mcp.Tools.Scan;

using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace CheatEngine.Mcp.Resources.Live;

/// <summary>
///     The read-only value-scan projections of one Cheat Engine instance: every scanner and one scanner. The
///     <c>scannerName</c> variable completes from main and the named scanners this activation retains, without any
///     dispatch.
/// </summary>
[McpServerResourceType]
public sealed class ScanLiveResources(ScanTools scans, TargetResources resources) : IMcpCompletionSource
{
	private const string ScannersPath = McpResourceUris.InstancePrefix + "scanners";
	private const string ScannerNameVariable = "scannerName";
	private const string MainScanner = "main";

	// ScanTools retains every named scanner as a resource of this kind, named after the scanner; it exposes no constant
	// yet, so LiveCompletionTests creates scanners through ScanTools itself to catch a change of kind.
	private const string ScannerResourceKind = "scan";

	/// <summary>Reads the state of every scanner.</summary>
	/// <param name="cancellationToken">The resource read cancellation.</param>
	/// <returns>The structured result of <c>scan_list_scanners</c>.</returns>
	[McpServerResource(UriTemplate = ScannersPath, Name = "instance_scanners", Title = "Scanners",
		MimeType = McpResourceUris.JsonMimeType)]
	[McpSourceTool(typeof(ScanTools), CheatEngineToolNames.ScanListScanners)]
	[McpResourceAnnotations(Role.Assistant, Priority = LiveResourceResults.Priority)]
	[Description("The state of main, Cheat Engine's visible scanner, and of every independent scanner of this " +
				 "activation. Its JSON is the structured result of " + CheatEngineToolNames.ScanListScanners + ".")]
	public ReadResourceResult Scanners(CancellationToken cancellationToken = default)
	{
		return LiveResourceResults.Json(ScannersPath, JsonSerializer.Serialize(scans.ListScanners(cancellationToken),
			ScanJsonContext.Default.ScanScannerList));
	}

	/// <summary>Reads the state of one scanner.</summary>
	/// <param name="scannerName">main or an independent scanner's name.</param>
	/// <param name="cancellationToken">The resource read cancellation.</param>
	/// <returns>The structured result of <c>scan_get_status</c>.</returns>
	[McpServerResource(UriTemplate = ScannersPath + "/{scannerName}", Name = "instance_scanner",
		Title = "Scanner status", MimeType = McpResourceUris.JsonMimeType)]
	[McpSourceTool(typeof(ScanTools), CheatEngineToolNames.ScanGetStatus)]
	[McpResourceAnnotations(Role.Assistant, Priority = LiveResourceResults.Priority)]
	[Description("The state of one scanner: main or the percent-encoded name of an independent scanner. Its JSON is " +
				 "the structured result of " + CheatEngineToolNames.ScanGetStatus + ".")]
	public ReadResourceResult Scanner(
		[Description("main or an existing independent scanner name.")]
		[McpCompletion(McpCompletionCost.Memory)]
		string scannerName,
		CancellationToken cancellationToken = default)
	{
		string name = McpResourceQuery.Required(scannerName, nameof(scannerName));
		return LiveResourceResults.Json(ScannersPath + "/" + McpResourceQuery.Segment(name),
			JsonSerializer.Serialize(scans.GetStatus(name, cancellationToken),
				ScanJsonContext.Default.ScanStatusResult));
	}

	/// <summary>
	///     Lists main, then the named scanners this activation retains in ordinal order, for <c>scannerName</c>, from
	///     the retained-resource list alone: no Cheat Engine call. A value scan that <c>pointer_find_references</c>
	///     could not release is retained under that tool's name and is not a scanner, so it is left out.
	/// </summary>
	/// <param name="variable">The completed variable: <c>scannerName</c>.</param>
	/// <param name="cancellationToken">Unused: the retained resources are read from memory.</param>
	/// <returns>The scanner names.</returns>
	public McpCompletionValues ListCompletionValues(string variable, CancellationToken cancellationToken)
	{
		return string.Equals(variable, ScannerNameVariable, StringComparison.Ordinal)
			? new McpCompletionValues(
			[
				MainScanner,
				.. resources.List().Where(static resource => IsNamedScanner(resource))
					.Select(static resource => resource.Name!).Order(StringComparer.Ordinal)
			])
			: throw new ArgumentOutOfRangeException(nameof(variable), variable, "Only scannerName is completed.");
	}

	private static bool IsNamedScanner(TargetResourceDescriptor resource)
	{
		return string.Equals(resource.Kind, ScannerResourceKind, StringComparison.Ordinal) &&
			   resource.State is not TargetResourceState.Ended && !string.IsNullOrEmpty(resource.Name) &&
			   !string.Equals(resource.Name, CheatEngineToolNames.PointerFindReferences, StringComparison.Ordinal);
	}
}
