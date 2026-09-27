using System.Text.Json.Serialization;

using CheatEngine.Mcp.Core.Contract;

namespace CheatEngine.Mcp.Core.Values;

/// <summary>How much a heavy reader returns per item.</summary>
[JsonConverter(typeof(ContractEnumConverter<ResultFormat>))]
public enum ResultFormat
{
	/// <summary>The fields most callers need.</summary>
	Concise,

	/// <summary>Every available field.</summary>
	Detailed
}
