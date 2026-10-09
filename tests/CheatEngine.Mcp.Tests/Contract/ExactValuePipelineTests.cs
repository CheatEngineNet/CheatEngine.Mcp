using System.Globalization;
using System.Text.Json;

using CheatEngine.Client.Memory;
using CheatEngine.Client.Results;
using CheatEngine.Mcp.Core.Contract;
using CheatEngine.Mcp.Tests.Support;
using CheatEngine.Mcp.Tests.Tools.Memory;
using CheatEngine.Mcp.Tests.Tools.Pointer;
using CheatEngine.SDK.Engine.Values;

using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace CheatEngine.Mcp.Tests.Contract;

/// <summary>Exact textual numbers and unavailable values across real MCP serialization and gateway routing.</summary>
[Collection(nameof(SerialTestGroup))]
public sealed class ExactValuePipelineTests
{
	private static CancellationToken Token => TestContext.Current.CancellationToken;

	[Theory]
	[InlineData("int64", "-9223372036854775808", "20000000000001")]
	[InlineData("int64", "9223372036854775807", "20000000000001")]
	[InlineData("uint64", "9007199254740993", "20000000000001")]
	[InlineData("uint64", "18446744073709551615", "20000000000001")]
	[InlineData("pointer", "FFFFFFFFFFFFFFFF", "20000000000001")]
	[InlineData("uint8", "0", "FFFFFFFFFFFFFFFF")]
	[InlineData("int32", "0", "1000")]
	[InlineData("string", "", "1000")]
	public async Task MemoryRead_ExactAndEmptyValues_RemainStringsThroughClientAndGateway(string valueType,
		string expected, string address)
	{
		TargetDouble target = new();
		target.Memory = (method, arguments) =>
		{
			if (valueType == "string")
			{
				Assert.Equal(nameof(IMemoryClient.ReadString), method.Name);
				return string.Empty;
			}

			Assert.Equal(nameof(IMemoryClient.ReadPrimitive), method.Name);
			Assert.Equal(ulong.Parse(address, NumberStyles.HexNumber, CultureInfo.InvariantCulture),
				Assert.IsType<Address>(arguments[0]).ToUInt64());
			return valueType switch
			{
				"int64" => (object) long.Parse(expected, CultureInfo.InvariantCulture),
				"uint64" => ulong.Parse(expected, CultureInfo.InvariantCulture),
				"pointer" => new Address(ulong.MaxValue),
				"uint8" => (byte) 0,
				"int32" => 0,
				_ => throw new InvalidOperationException("Unexpected value type.")
			};
		};
		using TestActivation activation = new(target.Client);
		await using TestMcpPipeline pipeline = await TestMcpPipeline.StartAsync(activation.Manifest,
			McpPrimitiveBinding.FromTargets(activation.Targets));
		string arguments = $$"""{"address":"{{address}}","valueType":"{{valueType}}"}""";

		CallToolResult direct = await pipeline.CallAsync(CheatEngineToolNames.MemoryRead, arguments);
		JsonElement routed = await AssertGatewayRoundTripAsync(CheatEngineToolNames.MemoryRead, arguments, direct);

		Assert.Equal(address, routed.GetProperty("address").GetString());
		Assert.Equal(valueType, routed.GetProperty("valueType").GetString());
		Assert.Equal(JsonValueKind.String, routed.GetProperty("value").ValueKind);
		Assert.Equal(expected, routed.GetProperty("value").GetString());
		Assert.False(routed.TryGetProperty("values", out _));
		Assert.Equal(1, target.Dispatcher.Calls);
		Assert.Equal(0, target.Mutations);
	}

	[Theory]
	[InlineData("0", "FFFFFFFFFFFFFFFF", "FFFFFFFFFFFFFFFF")]
	[InlineData("-8000000000000000", "FFFFFFFFFFFFFFFF", "7FFFFFFFFFFFFFFF")]
	[InlineData("-1", "20000000000001", "20000000000000")]
	public async Task PointerReadChain_SignedOffsetsAndWideAddresses_RoundTripWithoutNumericConversion(string offset,
		string pointerValue, string expectedAddress)
	{
		await using PointerFixture fixture = new();
		fixture.PutPointer(PointerFixture.ModuleBase + 0x100,
			ulong.Parse(pointerValue, NumberStyles.HexNumber, CultureInfo.InvariantCulture));
		await using TestMcpPipeline pipeline = await fixture.StartPipelineAsync();
		string arguments = $$"""{"base":"game.exe+100","offsets":["{{offset}}"]}""";

		CallToolResult direct = await pipeline.CallAsync(CheatEngineToolNames.PointerReadChain, arguments);
		JsonElement routed = await AssertGatewayRoundTripAsync(CheatEngineToolNames.PointerReadChain, arguments,
			direct);

		JsonElement hop = Assert.Single(routed.GetProperty("hops").EnumerateArray());
		Assert.Equal(pointerValue, hop.GetProperty("pointer").GetString());
		Assert.Equal(offset, hop.GetProperty("offset").GetString());
		Assert.Equal(expectedAddress, routed.GetProperty("address").GetString());
		Assert.False(routed.TryGetProperty("value", out _));
		Assert.Equal(1, fixture.Dispatches);
	}

	[Fact]
	public async Task MemoryReadBatch_ZeroEmptyAndUnavailable_StayDistinctAcrossClientAndGateway()
	{
		TargetDouble target = new();
		int stringReads = 0;
		target.Memory = (method, arguments) =>
		{
			if (method.Name == nameof(IMemoryClient.ReadPrimitiveBatchDetailed))
			{
				return new MemoryPrimitiveBatchReadOutcome<int>(1, [0], null, null);
			}

			Assert.Equal(nameof(IMemoryClient.TryReadString), method.Name);
			bool available = ++stringReads == 1;
			arguments[1] = available ? string.Empty : null;
			arguments[2] = available ? default(CheatEngineFailure) :
				new CheatEngineFailure(CheatEngineFailureKind.MemoryReadFailed, "Memory.ReadString",
					"The fixture page is unreadable.", hostEffect: CheatEngineHostEffect.NotApplied);
			return available;
		};
		using TestActivation activation = new(target.Client);
		await using TestMcpPipeline pipeline = await TestMcpPipeline.StartAsync(activation.Manifest,
			McpPrimitiveBinding.FromTargets(activation.Targets));
		const string arguments = """
			{"items":[{"address":"1000","valueType":"int32"},{"address":"2000","valueType":"string"},{"address":"3000","valueType":"string"}]}
			""";

		CallToolResult direct = await pipeline.CallAsync(CheatEngineToolNames.MemoryReadBatch, arguments);
		JsonElement routed = await AssertGatewayRoundTripAsync(CheatEngineToolNames.MemoryReadBatch, arguments, direct);

		Assert.Equal(1, routed.GetProperty("failed").GetInt32());
		JsonElement items = routed.GetProperty("items");
		Assert.Equal(3, items.GetArrayLength());
		Assert.Equal("0", items[0].GetProperty("value").GetString());
		Assert.False(items[0].TryGetProperty("error", out _));
		Assert.Equal(string.Empty, items[1].GetProperty("value").GetString());
		Assert.False(items[1].TryGetProperty("error", out _));
		Assert.False(items[2].TryGetProperty("value", out _));
		Assert.Equal("memory_read_failed", items[2].GetProperty("error").GetProperty("kind").GetString());
		Assert.Equal(2, stringReads);
		Assert.Equal(0, target.Mutations);
	}

	private static async Task<JsonElement> AssertGatewayRoundTripAsync(string tool, string argumentsJson,
		CallToolResult direct)
	{
		Assert.False(direct.IsError is true, Assert.IsType<TextContentBlock>(Assert.Single(direct.Content)).Text);
		JsonElement expected = Assert.IsType<JsonElement>(direct.StructuredContent);
		await using GatewayTestHost gateway = await GatewayTestHost.StartAsync();
		await using FakeBackend backend = await FakeBackend.StartAsync(gateway.Registry, "exact-values");
		await using McpClient client = await gateway.ConnectAsync();
		backend.Respond = () => direct;
		Dictionary<string, object?> routedArguments = backend.RoutedArguments();
		using JsonDocument arguments = JsonDocument.Parse(argumentsJson);
		foreach (JsonProperty property in arguments.RootElement.EnumerateObject())
		{
			routedArguments.Add(property.Name, property.Value.Clone());
		}

		CallToolResult routed = await client.CallToolAsync(tool, routedArguments, cancellationToken: Token);

		Assert.NotEqual(true, routed.IsError);
		JsonElement actual = Assert.IsType<JsonElement>(routed.StructuredContent);
		Assert.Equal(expected.GetRawText(), actual.GetRawText());
		Assert.Equal(Assert.IsType<TextContentBlock>(Assert.Single(direct.Content)).Text,
			Assert.IsType<TextContentBlock>(Assert.Single(routed.Content)).Text);
		Assert.Equal(routedArguments.Count - 1, backend.LastArguments.Count);
		foreach (JsonProperty property in arguments.RootElement.EnumerateObject())
		{
			Assert.True(JsonElement.DeepEquals(property.Value, backend.LastArguments[property.Name]));
		}

		Assert.Equal(1, backend.ForwardedCallCount);
		return actual.Clone();
	}
}
