using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace CheatEngine.Mcp.Hosting.Discovery;

/// <summary>Aggregate discovery outcomes, without record contents, paths, endpoints or credentials.</summary>
internal sealed class InstanceDiscoveryDiagnostics
{
	[Description("ready when the registry was readable, missing when it does not exist, unavailable when it could not be read.")]
	[AllowedValues("ready", "missing", "unavailable")]
	public string RegistryStatus { get; set; } = "ready";

	[Description("Registry files found before inspection; discovery may stop before every file is examined.")]
	public int RecordsFound
	{
		get; set;
	}

	[Description("Valid records whose process identity was confirmed; only these are eligible for backend verification.")]
	public int Candidates
	{
		get; set;
	}

	[Description("Malformed, oversized, invalid or unreadable records omitted from this discovery.")]
	public int RejectedRecords
	{
		get; set;
	}

	[Description("Records omitted because their process exited or its start time no longer matches.")]
	public int ExpiredRecords
	{
		get; set;
	}

	[Description("Records omitted because the process identity could not be inspected; the records were preserved.")]
	public int UnobservableProcesses
	{
		get; set;
	}

	[Description("Candidates whose backend was unreachable or did not confirm its identity; no record details are exposed.")]
	public int UnavailableInstances
	{
		get; set;
	}

	[Description("Candidates whose backend verification did not finish within this discovery; retry when discoveryIncomplete is true.")]
	public int PendingInstances
	{
		get; set;
	}
}
