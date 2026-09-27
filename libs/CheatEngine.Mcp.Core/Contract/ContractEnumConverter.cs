using System.Text.Json;
using System.Text.Json.Serialization;

namespace CheatEngine.Mcp.Core.Contract;

/// <summary>
///     The string converter of every MCP contract enum: values are <c>snake_case</c> names and integer input is
///     rejected. Policy-derived names are read in any case; a name set with
///     <see cref="JsonStringEnumMemberNameAttribute" />
///     must match exactly. Attach it to the enum type with <see cref="JsonConverterAttribute" />, so it also applies
///     inside source-generated contexts and in every options instance.
/// </summary>
/// <typeparam name="TEnum">The contract enum.</typeparam>
public sealed class ContractEnumConverter<TEnum>()
	: JsonStringEnumConverter<TEnum>(JsonNamingPolicy.SnakeCaseLower, false)
	where TEnum : struct, Enum;
