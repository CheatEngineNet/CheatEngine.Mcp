using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using System.Text.Json;

using CheatEngine.Client.Inspection;
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
		TargetDouble target = new()
		{
			Memory = (method, arguments) =>
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
			}
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
			arguments[2] = available ? default :
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

	[Theory]
	[InlineData("utf8", 7)]
	[InlineData("utf16_le", 8)]
	public async Task MemoryFindStrings_UnicodeAndWideAddress_RoundTripAcrossClientAndGateway(string encoding,
		int expectedBytes)
	{
		TargetDouble target = new();
		byte[] bytes = (encoding == "utf8" ? Encoding.UTF8 : Encoding.Unicode).GetBytes("hé😊");
		target.Memory = (method, arguments) =>
		{
			Assert.Equal(nameof(IMemoryClient.ReadBytes), method.Name);
			MemoryBytesReadRequest request = Assert.IsType<MemoryBytesReadRequest>(arguments[0]);
			Assert.Equal(0x20000000000001UL, request.Address.ToUInt64());
			Assert.Equal(expectedBytes, request.Length);
			return bytes.ToImmutableArray();
		};
		using TestActivation activation = new(target.Client);
		await using TestMcpPipeline pipeline = await TestMcpPipeline.StartAsync(activation.Manifest,
			McpPrimitiveBinding.FromTargets(activation.Targets));
		string arguments = $$"""{"address":"20000000000001","size":{{expectedBytes}},"encoding":"{{encoding}}","minLength":3}""";

		CallToolResult direct = await pipeline.CallAsync(CheatEngineToolNames.MemoryFindStrings, arguments);
		JsonElement routed = await AssertGatewayRoundTripAsync(CheatEngineToolNames.MemoryFindStrings, arguments, direct);

		Assert.Equal(encoding, routed.GetProperty("encoding").GetString());
		Assert.Equal(1, routed.GetProperty("total").GetInt32());
		Assert.False(routed.GetProperty("truncated").GetBoolean());
		Assert.False(routed.TryGetProperty("nextOffset", out _));
		JsonElement match = Assert.Single(routed.GetProperty("strings").EnumerateArray());
		Assert.Equal("20000000000001", match.GetProperty("address").GetString());
		Assert.Equal("hé😊", match.GetProperty("text").GetString());
		Assert.Equal(expectedBytes, match.GetProperty("byteLength").GetInt32());
		Assert.True(match.GetProperty("startsAtRangeBoundary").GetBoolean());
		Assert.True(match.GetProperty("endsAtRangeBoundary").GetBoolean());
		Assert.Single(target.CallsTo("Memory"));
		Assert.Equal(0, target.Mutations);
	}

	[Fact]
	public async Task MemoryReadVtable_WidePointersNullAndUnreadableSlots_RoundTripAcrossClientAndGateway()
	{
		TargetDouble target = new()
		{
			Memory = (method, arguments) =>
			{
				Assert.Equal(nameof(IMemoryClient.ReadBytesDetailed), method.Name);
				MemoryBytesReadRequest request = Assert.IsType<MemoryBytesReadRequest>(arguments[0]);
				Assert.Equal(8, request.Length);
				return request.Address.ToUInt64() switch
				{
					0x20000000000001 => new MemoryBytesReadOutcome(8, [.. BitConverter.GetBytes(ulong.MaxValue)], null),
					0x20000000000009 => new MemoryBytesReadOutcome(8, [.. new byte[8]], null),
					0x20000000000011 => new MemoryBytesReadOutcome(8, [], new CheatEngineFailure(
						CheatEngineFailureKind.MemoryReadFailed, "Memory.ReadBytesDetailed", "Unreadable fixture slot.",
						hostEffect: CheatEngineHostEffect.Completed)),
					_ => throw new InvalidOperationException("Unexpected vtable slot.")
				};
			},
			Inspection = (method, arguments) =>
				{
					Assert.Equal(nameof(IInspectionClient.TryResolveName), method.Name);
					Assert.Equal(ulong.MaxValue, Assert.IsType<Address>(arguments[0]).ToUInt64());
					arguments[1] = "game!VirtualMethod";
					arguments[2] = default(CheatEngineFailure);
					return true;
				}
		};
		using TestActivation activation = new(target.Client);
		await using TestMcpPipeline pipeline = await TestMcpPipeline.StartAsync(activation.Manifest,
			McpPrimitiveBinding.FromTargets(activation.Targets));
		const string arguments = """{"address":"20000000000001","addressIsVtable":true,"count":3}""";

		CallToolResult direct = await pipeline.CallAsync(CheatEngineToolNames.MemoryReadVtable, arguments);
		JsonElement routed = await AssertGatewayRoundTripAsync(CheatEngineToolNames.MemoryReadVtable, arguments, direct);

		Assert.Equal("20000000000001", routed.GetProperty("vtableAddress").GetString());
		Assert.Equal(8, routed.GetProperty("pointerBytes").GetInt32());
		JsonElement slots = routed.GetProperty("slots");
		Assert.Equal(3, slots.GetArrayLength());
		Assert.Equal("FFFFFFFFFFFFFFFF", slots[0].GetProperty("functionAddress").GetString());
		Assert.Equal("game!VirtualMethod", slots[0].GetProperty("symbol").GetString());
		Assert.Equal("0", slots[1].GetProperty("functionAddress").GetString());
		Assert.False(slots[1].TryGetProperty("symbol", out _));
		Assert.False(slots[2].TryGetProperty("functionAddress", out _));
		Assert.Equal("memory_read_failed", slots[2].GetProperty("error").GetProperty("kind").GetString());
		Assert.Equal(3, target.CallsTo("Memory").Length);
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
