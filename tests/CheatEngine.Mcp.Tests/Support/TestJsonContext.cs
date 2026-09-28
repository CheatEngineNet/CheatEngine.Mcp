using System.Text.Json;
using System.Text.Json.Serialization;

namespace CheatEngine.Mcp.Tests.Support;

/// <summary>Source-generated metadata for the result shapes that tests deserialize; no reflection-based JSON.</summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
	RespectNullableAnnotations = true, RespectRequiredConstructorParameters = true)]
[JsonSerializable(typeof(LuaJsonProbe))]
[JsonSerializable(typeof(LuaExecuteOutcome))]
[JsonSerializable(typeof(string[]))]
[JsonSerializable(typeof(bool[]))]
[JsonSerializable(typeof(ContractProbeResult))]
[JsonSerializable(typeof(ContractProbeDetails))]
[JsonSerializable(typeof(LuaPreludeProbe))]
[JsonSerializable(typeof(LuaArgumentProbe))]
internal sealed partial class TestJsonContext : JsonSerializerContext
{
}

/// <summary>What the v2 prelude helpers and runtime values produce.</summary>
internal sealed record LuaPreludeProbe(
	string Hex,
	string Negative,
	bool NilHex,
	string Bytes,
	string Prefix,
	string Text,
	string NotANumber,
	string Infinity,
	string NegativeInfinity,
	double Finite,
	long Integer,
	int Budget,
	bool Expired);

/// <summary>The <c>a</c> table as a fixed v2 script sees it.</summary>
internal sealed record LuaArgumentProbe(int Count, string Text, long Address, bool Flag, string[] Items);

/// <summary>A fixed-script result exercising every JSON value kind.</summary>
internal sealed record LuaJsonProbe(
	long Maximum,
	long Minimum,
	long Address,
	string AddressHex,
	double Ratio,
	bool Flag,
	string[] Tags,
	LuaJsonProbeChild Child,
	int[] Empty);

/// <summary>A nested object holding a <c>table.pack</c> result.</summary>
internal sealed record LuaJsonProbeChild(string Name, long?[] Packed);

/// <summary>The stage-B shape of the two-stage <c>lua_execute</c> proof.</summary>
internal sealed record LuaExecuteOutcome(
	bool Ok,
	string? Phase = null,
	string? Error = null,
	JsonElement[]? ReturnValues = null);
