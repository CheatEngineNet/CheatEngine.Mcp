using System.Collections.Immutable;
using System.Text.Json;

using CheatEngine.Client.Inspection;
using CheatEngine.Client.Memory;
using CheatEngine.Client.Results;
using CheatEngine.Mcp.Core.Contract;
using CheatEngine.Mcp.Tests.Support;
using CheatEngine.SDK.Engine.Inspection;
using CheatEngine.SDK.Engine.Values;

using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

using Xunit.Sdk;

namespace CheatEngine.Mcp.Tests.Tools.Memory;

/// <summary>The memory and AOB tools through the plugin's real activation and a real MCP server: results and envelopes.</summary>
[Collection(nameof(SerialTestGroup))]
public sealed class MemoryPipelineTests
{
	[Fact]
	public async Task MemoryRead_Int32_ReturnsTheStructuredRecord()
	{
		TargetDouble target = new();
		target.Memory = (_, _) => 1337;
		await using Served served = await Served.StartAsync(target);

		CallToolResult result = await served.Pipeline.CallAsync(CheatEngineToolNames.MemoryRead,
			"""{"address":"401000","valueType":"int32"}""");

		Assert.NotEqual(true, result.IsError);
		JsonElement content = Assert.IsType<JsonElement>(result.StructuredContent);
		Assert.Equal(("401000", "int32", "1337"), (content.GetProperty("address").GetString(),
			content.GetProperty("valueType").GetString(), content.GetProperty("value").GetString()));
		Assert.False(content.TryGetProperty("values", out _));
	}

	[Fact]
	public async Task MemoryRead_BytesWithoutSize_IsAnInvalidArgumentEnvelopeWithoutDispatch()
	{
		TargetDouble target = new();
		await using Served served = await Served.StartAsync(target);

		ToolError error = TestMcpPipeline.AssertError(await served.Pipeline.CallAsync(CheatEngineToolNames.MemoryRead,
			"""{"address":"401000","valueType":"bytes"}"""), ToolErrorKind.InvalidArgument);

		Assert.Equal((ToolHostEffect.NotStarted, false), (error.HostEffect, error.Retryable));
		Assert.Equal("size", error.Details!.Value.GetProperty("parameter").GetString());
		Assert.Equal(0, target.Dispatcher.Calls);
	}

	[Fact]
	public async Task MemoryWriteBatch_FailureAfterAPrefix_IsAPartialEffectEnvelopeWithProgress()
	{
		TargetDouble target = new();
		target.Memory = (_, _) => new MemoryPrimitiveBatchWriteOutcome(2, 1, 1,
			new CheatEngineFailure(CheatEngineFailureKind.MemoryWriteFailed, "Memory.WritePrimitiveBatch",
				"The page is read-only.", hostEffect: CheatEngineHostEffect.Started),
			MemoryBatchWriteEffectState.Partial);
		await using Served served = await Served.StartAsync(target);

		ToolError error = TestMcpPipeline.AssertError(await served.Pipeline.CallAsync(
				CheatEngineToolNames.MemoryWriteBatch,
				"""{"items":[{"address":"1000","valueType":"int32","value":"1"},{"address":"2000","valueType":"int32","value":"2"}]}"""),
			ToolErrorKind.PartialEffect);

		Assert.Equal((ToolHostEffect.Started, false), (error.HostEffect, error.Retryable));
		JsonElement details = error.Details!.Value;
		Assert.Equal((1, 1, "partial"), (details.GetProperty("completed").GetInt32(),
			details.GetProperty("failedIndex").GetInt32(), details.GetProperty("effectState").GetString()));
	}

	[Fact]
	public async Task MemoryDumpToFile_NoWriteRoot_IsRefusedWithoutReadingTheTarget()
	{
		TargetDouble target = new();
		await using Served served = await Served.StartAsync(target);
		string path = Path.Combine(served.Activation.DataDirectory, "..", "dump.bin");

		ToolError error = TestMcpPipeline.AssertError(await served.Pipeline.CallAsync(
				CheatEngineToolNames.MemoryDumpToFile,
				$$"""{"address":"1000","size":16,"path":{{JsonSerializer.Serialize(path)}}}"""),
			ToolErrorKind.InvalidArgument);

		Assert.Equal("path", error.Details!.Value.GetProperty("parameter").GetString());
		Assert.Equal(0, target.Dispatcher.Calls);
	}

	[Fact]
	public async Task MemoryFree_UnknownName_IsANotFoundEnvelope()
	{
		TargetDouble target = new();
		await using Served served = await Served.StartAsync(target);

		ToolError error = TestMcpPipeline.AssertError(
			await served.Pipeline.CallAsync(CheatEngineToolNames.MemoryFree, """{"name":"never"}"""),
			ToolErrorKind.NotFound);

		Assert.Equal((ToolHostEffect.NotStarted, false), (error.HostEffect, error.Retryable));
	}

	[Fact]
	public async Task AobGenerateSignature_OutsideAnyModule_IsAnInvalidArgumentEnvelopeWithoutLua()
	{
		TargetDouble target = new();
		target.Inspection = (method, _) => method.Name == nameof(IInspectionClient.GetModules)
			? ImmutableArray.Create(new ModuleInfo("game.exe", new Address(0x400000), new MemorySize(0x1000), true,
				@"C:\game\game.exe"))
			: throw new XunitException($"Unexpected {method.Name}.");
		await using Served served = await Served.StartAsync(target);

		ToolError error = TestMcpPipeline.AssertError(
			await served.Pipeline.CallAsync(CheatEngineToolNames.AobGenerateSignature, """{"address":"900000"}"""),
			ToolErrorKind.InvalidArgument);

		Assert.Equal("address", error.Details!.Value.GetProperty("parameter").GetString());
		Assert.Equal(0, target.LuaCalls);
	}

	[Fact]
	public async Task MemorySnapshots_CreateCompareListDelete_ReturnTheStructuredRecords()
	{
		TargetDouble target = new();
		byte[] memory = new byte[8];
		target.Memory = (_, arguments) =>
		{
			MemoryBytesReadRequest request = (MemoryBytesReadRequest) arguments[0]!;
			return new MemoryBytesReadOutcome(request.Length, memory.AsSpan(0, request.Length).ToImmutableArray(),
				null);
		};
		await using Served served = await Served.StartAsync(target);

		CallToolResult created = await served.Pipeline.CallAsync(CheatEngineToolNames.MemoryCreateSnapshot,
			"""{"name":"before","address":"1000","size":8}""");
		memory[4] = 3;
		await served.Pipeline.CallAsync(CheatEngineToolNames.MemoryCreateSnapshot,
			"""{"name":"after","address":"1000","size":8}""");
		CallToolResult compared = await served.Pipeline.CallAsync(CheatEngineToolNames.MemoryCompareSnapshot,
			"""{"name":"before","compareTo":"after","change":"increased"}""");
		CallToolResult listed = await served.Pipeline.CallAsync(CheatEngineToolNames.MemoryListSnapshots);
		CallToolResult deleted = await served.Pipeline.CallAsync(CheatEngineToolNames.MemoryDeleteSnapshot,
			"""{"name":"before"}""");

		JsonElement info = Assert.IsType<JsonElement>(created.StructuredContent);
		Assert.Equal(("1000", 8, 42), (info.GetProperty("address").GetString(), info.GetProperty("size").GetInt32(),
			info.GetProperty("processId").GetInt32()));
		JsonElement diff = Assert.IsType<JsonElement>(compared.StructuredContent);
		Assert.Equal(("increased", "int32", 1, "after"), (diff.GetProperty("change").GetString(),
			diff.GetProperty("valueType").GetString(), diff.GetProperty("total").GetInt32(),
			diff.GetProperty("compareTo").GetString()));
		JsonElement change = diff.GetProperty("changes")[0];
		Assert.Equal(("1004", "4", "0", "3"), (change.GetProperty("address").GetString(),
			change.GetProperty("offset").GetString(), change.GetProperty("before").GetString(),
			change.GetProperty("after").GetString()));
		Assert.False(diff.TryGetProperty("nextOffset", out _));
		Assert.Equal(2, Assert.IsType<JsonElement>(listed.StructuredContent).GetProperty("snapshots")
			.GetArrayLength());
		Assert.Equal(8, Assert.IsType<JsonElement>(deleted.StructuredContent).GetProperty("freedBytes").GetInt32());
	}

	[Fact]
	public async Task MemoryCompareSnapshot_UnknownName_IsANotFoundEnvelopeWithoutDispatch()
	{
		TargetDouble target = new();
		await using Served served = await Served.StartAsync(target);

		ToolError error = TestMcpPipeline.AssertError(await served.Pipeline.CallAsync(
			CheatEngineToolNames.MemoryCompareSnapshot, """{"name":"never"}"""), ToolErrorKind.NotFound);

		Assert.Equal((ToolHostEffect.NotStarted, false), (error.HostEffect, error.Retryable));
		Assert.Equal(0, target.Dispatcher.Calls);
	}

	[Fact]
	public async Task MemoryReadSamples_OneTick_ReturnsTheSeries()
	{
		TargetDouble target = new();
		target.Memory = (_, _) => new MemoryPrimitiveBatchReadOutcome<int>(1, [77], null, null);
		await using Served served = await Served.StartAsync(target);

		CallToolResult result = await served.Pipeline.CallAsync(CheatEngineToolNames.MemoryReadSamples,
			"""{"addresses":["1000"],"valueType":"int32","intervalMs":1000,"durationMs":100}""");

		Assert.NotEqual(true, result.IsError);
		JsonElement content = Assert.IsType<JsonElement>(result.StructuredContent);
		Assert.Equal(1, content.GetProperty("ticks").GetInt32());
		JsonElement series = content.GetProperty("series")[0];
		Assert.Equal(("1000", "77", 1), (series.GetProperty("address").GetString(),
			series.GetProperty("first").GetString(), series.GetProperty("changes").GetArrayLength()));
		Assert.False(series.TryGetProperty("error", out _));
	}

	[Fact]
	public async Task MemoryRead_BigEndian_ReadsTheUnsignedIntegerAndReturnsTheSwappedValue()
	{
		TargetDouble target = new();
		target.Memory = (method, _) =>
		{
			Assert.Equal(typeof(uint), method.GetGenericArguments()[0]);
			return 0x64000000U;
		};
		await using Served served = await Served.StartAsync(target);

		CallToolResult result = await served.Pipeline.CallAsync(CheatEngineToolNames.MemoryRead,
			"""{"address":"401000","valueType":"int32","byteOrder":"big_endian"}""");

		Assert.NotEqual(true, result.IsError);
		Assert.Equal("100", Assert.IsType<JsonElement>(result.StructuredContent).GetProperty("value").GetString());
	}

	[Theory]
	[InlineData(CheatEngineToolNames.MemoryReadSamples, """{"addresses":["1000"],"valueType":"bytes"}""")]
	[InlineData(CheatEngineToolNames.MemoryCompareSnapshot, """{"name":"before","valueType":"wstring"}""")]
	[InlineData(CheatEngineToolNames.MemoryRead, """{"address":"1000","valueType":"int32","byteOrder":"middle"}""")]
	public async Task NarrowedVocabulary_ValueOutsideTheSchema_IsAnInvalidArgumentEnvelopeWithoutDispatch(string tool,
		string arguments)
	{
		TargetDouble target = new();
		await using Served served = await Served.StartAsync(target);

		ToolError error = TestMcpPipeline.AssertError(await served.Pipeline.CallAsync(tool, arguments),
			ToolErrorKind.InvalidArgument);

		Assert.Equal(ToolHostEffect.NotStarted, error.HostEffect);
		Assert.Equal(0, target.Dispatcher.Calls);
	}

	[Fact]
	public async Task ListTools_FixedTypeTools_PublishOnlyTheFixedValueTypesAndEveryByteOrderIsLittleByDefault()
	{
		TargetDouble target = new();
		await using Served served = await Served.StartAsync(target);

		IList<McpClientTool> tools =
			await served.Pipeline.Client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);

		string[] fixedTypes =
			["int8", "uint8", "int16", "uint16", "int32", "uint32", "int64", "uint64", "float", "double", "pointer"];
		Assert.Equal(fixedTypes, Enumeration(Property(tools, CheatEngineToolNames.MemoryReadSamples, "valueType")));
		Assert.Equal(fixedTypes,
			Enumeration(Property(tools, CheatEngineToolNames.MemoryCompareSnapshot, "valueType")));
		Assert.Equal(fixedTypes, Enumeration(Schema(tools, CheatEngineToolNames.MemoryCompareSnapshot, false)
			.GetProperty("properties").GetProperty("valueType")));
		JsonElement[] orders =
		[
			Property(tools, CheatEngineToolNames.MemoryRead, "byteOrder"),
			Property(tools, CheatEngineToolNames.MemoryWrite, "byteOrder"),
			Property(tools, CheatEngineToolNames.MemoryReadSamples, "byteOrder"),
			Property(tools, CheatEngineToolNames.MemoryReadBatch, "items").GetProperty("items")
				.GetProperty("properties").GetProperty("byteOrder"),
			Property(tools, CheatEngineToolNames.MemoryWriteBatch, "items").GetProperty("items")
				.GetProperty("properties").GetProperty("byteOrder")
		];
		foreach (JsonElement order in orders)
		{
			Assert.Equal(["little_endian", "big_endian"], Enumeration(order));
			Assert.Equal("little_endian", order.GetProperty("default").GetString());
		}
	}

	private static JsonElement Schema(IList<McpClientTool> tools, string name, bool input = true)
	{
		Tool tool = tools.Single(tool => tool.Name == name).ProtocolTool;
		return input ? tool.InputSchema : tool.OutputSchema!.Value;
	}

	private static JsonElement Property(IList<McpClientTool> tools, string name, string property)
	{
		return Schema(tools, name).GetProperty("properties").GetProperty(property);
	}

	private static string?[] Enumeration(JsonElement schema)
	{
		return [.. schema.GetProperty("enum").EnumerateArray().Select(static value => value.GetString())];
	}

	/// <summary>The plugin activation over the double, served by a real MCP server.</summary>
	private sealed class Served : IAsyncDisposable
	{
		private Served(TestActivation activation, TestMcpPipeline pipeline)
		{
			Activation = activation;
			Pipeline = pipeline;
		}

		internal TestActivation Activation
		{
			get;
		}

		internal TestMcpPipeline Pipeline
		{
			get;
		}

		public async ValueTask DisposeAsync()
		{
			await Pipeline.DisposeAsync();
			Activation.Dispose();
		}

		internal static async Task<Served> StartAsync(TargetDouble target)
		{
			TestActivation activation = new(target.Client);
			try
			{
				return new Served(activation,
					await TestMcpPipeline.StartAsync(activation.Manifest,
						McpPrimitiveBinding.FromTargets(activation.Targets)));
			}
			catch
			{
				activation.Dispose();
				throw;
			}
		}
	}
}
