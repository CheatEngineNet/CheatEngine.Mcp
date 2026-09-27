using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CheatEngine.Mcp.Core.Composition;

/// <summary>
///     Transition shim for the legacy tools: reproduces the SDK's reflection-mode, catch-all
///     <see cref="JsonStringEnumConverter" /> exactly, but only for enums without a type-level
///     <see cref="JsonConverterAttribute" />. Contract enums therefore keep their own
///     <c>ContractEnumConverter</c>, which an options-level catch-all would otherwise override. Strict options never
///     contain it; it disappears with the last legacy tool.
/// </summary>
[RequiresDynamicCode("Creates JsonStringEnumConverter instances for enum types discovered at run time.")]
internal sealed class LegacyEnumConverterFactory : JsonConverterFactory
{
	private readonly JsonStringEnumConverter _legacy = new();

	/// <inheritdoc />
	public override bool CanConvert(Type typeToConvert)
	{
		ArgumentNullException.ThrowIfNull(typeToConvert);
		return typeToConvert.IsEnum && !typeToConvert.IsDefined(typeof(JsonConverterAttribute), false);
	}

	/// <inheritdoc />
	public override JsonConverter? CreateConverter(Type typeToConvert, JsonSerializerOptions options)
	{
		return _legacy.CreateConverter(typeToConvert, options);
	}
}
