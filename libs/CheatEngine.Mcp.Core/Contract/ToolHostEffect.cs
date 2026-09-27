using System.Text.Json.Serialization;

namespace CheatEngine.Mcp.Core.Contract;

/// <summary>
///     What a failed call did to Cheat Engine or the target, one for one with the Client's
///     <c>CheatEngineHostEffect</c>; the wire value is the <c>snake_case</c> member name.
/// </summary>
[JsonConverter(typeof(ContractEnumConverter<ToolHostEffect>))]
public enum ToolHostEffect
{
	/// <summary>No Cheat Engine work started.</summary>
	NotStarted,

	/// <summary>Cheat Engine was asked, but the change was not applied.</summary>
	NotApplied,

	/// <summary>Cheat Engine work started and may have changed state.</summary>
	Started,

	/// <summary>Cheat Engine work completed.</summary>
	Completed,

	/// <summary>The operation ran, but its cleanup could not be confirmed.</summary>
	CleanupUnconfirmed,

	/// <summary>Whether Cheat Engine work started is unknown.</summary>
	Unknown
}
