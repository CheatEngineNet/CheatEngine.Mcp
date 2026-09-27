using System.Collections.Immutable;
using System.Text.Json;

using CheatEngine.Client.Inspection;
using CheatEngine.Client.Memory;
using CheatEngine.Client.Results;
using CheatEngine.Mcp.Core.Contract;
using CheatEngine.Mcp.Tests.Support;
using CheatEngine.SDK.Engine.Inspection;
using CheatEngine.SDK.Engine.Values;

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
