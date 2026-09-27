using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

using CheatEngine.Mcp.Core.Contract;
using CheatEngine.Mcp.Core.Values;
using CheatEngine.Mcp.Tests.Support;

using Microsoft.Extensions.AI;

using ModelContextProtocol;
using ModelContextProtocol.Protocol;

namespace CheatEngine.Mcp.Tests.Core;

/// <summary>The wire form of the contract enums, the error envelope and the error result.</summary>
public sealed class ContractJsonTests
{
	private static readonly JsonSerializerOptions Composed =
		CheatEngineMcpJson.CreateOptions(TestComposition.BackendManifest, false);

	[Fact]
	public void ToolErrorKind_EveryMember_IsSnakeCaseOnTheWire()
	{
		Assert.Equal(
		[
			"invalid_argument", "invalid_state", "not_found", "not_attached", "busy", "timeout", "cancelled",
			"capability_disabled", "unsupported", "target_changed", "host_refused", "memory_read_failed",
			"memory_write_failed", "limit_exceeded", "partial_effect", "stopping", "instance_unavailable", "internal"
		], Enum.GetValues<ToolErrorKind>().Select(static kind => Wire(kind)));
	}

	[Fact]
	public void ContractEnums_InCore_CarryTheContractConverter()
	{
		Type[] enums = typeof(ToolError).Assembly.GetTypes()
			.Where(static type => type.IsEnum && type.IsPublic &&
			                      type.Namespace is "CheatEngine.Mcp.Core.Contract" or "CheatEngine.Mcp.Core.Values")
			.ToArray();

		Assert.Equal(4, enums.Length);
		Assert.All(enums, static type => Assert.Equal(typeof(ContractEnumConverter<>).MakeGenericType(type),
			type.GetCustomAttribute<JsonConverterAttribute>()?.ConverterType));
	}

	[Fact]
	public void ToolHostEffect_EveryMember_IsSnakeCaseOnTheWire()
	{
		Assert.Equal(["not_started", "not_applied", "started", "completed", "cleanup_unconfirmed", "unknown"],
			Enum.GetValues<ToolHostEffect>().Select(static effect => Wire(effect)));
	}

	[Fact]
	public void McpValueType_EveryMember_UsesTheContractName()
	{
		Assert.Equal(
		[
			"int8", "uint8", "int16", "uint16", "int32", "uint32", "int64", "uint64", "float", "double", "pointer",
			"string", "wstring", "bytes"
		], Enum.GetValues<McpValueType>().Select(static type => Wire(type)));
		Assert.Equal(["concise", "detailed"], Enum.GetValues<ResultFormat>().Select(static format => Wire(format)));
	}

	[Theory]
	[InlineData("\"uint8\"", McpValueType.UInt8)]
	[InlineData("\"wstring\"", McpValueType.WString)]
	[InlineData("\"DOUBLE\"", McpValueType.Double)]
	[InlineData("\"Int32\"", McpValueType.Int32)]
	public void ContractEnumConverter_PolicyNameInAnyCaseOrExactMemberName_IsRead(string json, McpValueType expected)
	{
		Assert.Equal(expected, JsonSerializer.Deserialize(json, Composed.GetTypeInfo<McpValueType>()));
	}

	[Theory]
	[InlineData("4")]
	[InlineData("\"4\"")]
	[InlineData("\"u_int8\"")]
	public void ContractEnumConverter_IntegerOrUnknownName_IsRejected(string json)
	{
		Assert.Throws<JsonException>(() => JsonSerializer.Deserialize(json, Composed.GetTypeInfo<McpValueType>()));
	}

	[Fact]
	public void ContractEnum_Schema_IsAStringEnumOfWireNames()
	{
		JsonElement schema = AIJsonUtilities.CreateJsonSchema(typeof(ToolHostEffect), serializerOptions: Composed);

		Assert.Equal("string", schema.GetProperty("type").GetString());
		Assert.Equal(["not_started", "not_applied", "started", "completed", "cleanup_unconfirmed", "unknown"],
			schema.GetProperty("enum").EnumerateArray().Select(static value => value.GetString()));
	}

	[Fact]
	public void Serialize_FullError_KeepsTheEnvelopeFieldOrder()
	{
		using JsonDocument details = JsonDocument.Parse("""{"completed":2}""");
		ToolError error = new(ToolErrorKind.PartialEffect, "Two of three writes completed.", "Memory.WriteBytes",
			ToolHostEffect.Started, false, "Read the remaining addresses first.", details.RootElement.Clone());

		Assert.Equal(
			"""{"error":{"kind":"partial_effect","message":"Two of three writes completed.","operation":"Memory.WriteBytes","hostEffect":"started","retryable":false,"hint":"Read the remaining addresses first.","details":{"completed":2}}}""",
			ToolErrorResults.Serialize(error));
	}

	[Fact]
	public void Serialize_MinimalError_OmitsNullFields()
	{
		Assert.Equal("""{"error":{"kind":"busy","message":"Busy.","hostEffect":"not_started","retryable":true}}""",
			ToolErrorResults.Serialize(new ToolError(ToolErrorKind.Busy, "Busy.", null, ToolHostEffect.NotStarted,
				true)));
	}

	[Fact]
	public void Create_Error_IsAnErrorResultWithOneTextBlockAndNoStructuredContent()
	{
		ToolError error = new(ToolErrorKind.NotAttached, "No process is attached.", null, ToolHostEffect.NotStarted,
			false, ToolFailureMapping.AttachHint);

		CallToolResult result = ToolErrorResults.Create(error);

		Assert.True(result.IsError);
		Assert.Null(result.StructuredContent);
		Assert.Equal(ToolErrorResults.Serialize(error),
			Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text);
	}

	[Fact]
	public void TryRead_CreatedResult_RoundTripsTheError()
	{
		using JsonDocument details = JsonDocument.Parse("""{"parameter":"address"}""");
		ToolError error = new(ToolErrorKind.InvalidArgument, "address: is empty.", "Memory.Read",
			ToolHostEffect.NotStarted, false, "Pass an address expression.", details.RootElement.Clone());

		Assert.True(ToolErrorResults.TryRead(ToolErrorResults.Create(error), out ToolError? read));

		Assert.Equal((error.Kind, error.Message, error.Operation, error.HostEffect, error.Retryable, error.Hint),
			(read.Kind, read.Message, read.Operation, read.HostEffect, read.Retryable, read.Hint));
		Assert.Equal(error.Details!.Value.GetRawText(), read.Details!.Value.GetRawText());
	}

	[Fact]
	public void TryRead_SuccessOrForeignError_ReturnsFalse()
	{
		CallToolResult success = new() { Content = [new TextContentBlock { Text = """{"error":{}}""" }] };
		CallToolResult sdkError = new()
		{
			IsError = true, Content = [new TextContentBlock { Text = "An error occurred invoking 'x'." }]
		};
		CallToolResult twoBlocks = new()
		{
			IsError = true,
			Content =
			[
				new TextContentBlock
				{
					Text = ToolErrorResults.Serialize(new ToolError(ToolErrorKind.Busy, "Busy.",
						null, ToolHostEffect.NotStarted, true))
				},
				new TextContentBlock { Text = "extra" }
			]
		};

		Assert.False(ToolErrorResults.TryRead(success, out _));
		Assert.False(ToolErrorResults.TryRead(sdkError, out _));
		Assert.False(ToolErrorResults.TryRead(twoBlocks, out _));
	}

	private static string Wire<TEnum>(TEnum value) where TEnum : struct, Enum
	{
		return JsonSerializer.SerializeToElement(value, Composed.GetTypeInfo<TEnum>()).GetString()!;
	}
}
