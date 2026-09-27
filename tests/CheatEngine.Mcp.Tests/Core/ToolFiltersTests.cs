using System.Text.Json;
using System.Text.Json.Nodes;

using CheatEngine.Mcp.Core.Contract;
using CheatEngine.Mcp.Tests.Support;

using ModelContextProtocol;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace CheatEngine.Mcp.Tests.Core;

/// <summary>The Core request filters, observed through a real MCP server and client.</summary>
public sealed class ToolFiltersTests
{
	[Fact]
	public async Task CallTool_V2Record_ReturnsStructuredContentMatchingTheText()
	{
		await using TestMcpPipeline pipeline = await TestMcpPipeline.StartAsync();

		CallToolResult result = await pipeline.CallAsync(ContractProbeTool.ConvertName, """{"text":"a"}""");

		Assert.NotEqual(true, result.IsError);
		JsonElement structured = Assert.IsType<JsonElement>(result.StructuredContent);
		TextContentBlock text = Assert.IsType<TextContentBlock>(Assert.Single(result.Content));
		Assert.True(JsonNode.DeepEquals(JsonNode.Parse(structured.GetRawText()), JsonNode.Parse(text.Text)));
		Assert.Equal("""{"text":"a","values":[],"flag":false,"count":1,"label":"default"}""", text.Text);
	}

	[Fact]
	public async Task ListTools_V2Record_PublishesAnObjectOutputSchema()
	{
		await using TestMcpPipeline pipeline = await TestMcpPipeline.StartAsync();

		IList<McpClientTool> tools =
			await pipeline.Client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);

		Tool probe = tools.Single(static tool => tool.Name == ContractProbeTool.ConvertName).ProtocolTool;
		Assert.Equal("object", probe.OutputSchema!.Value.GetProperty("type").GetString());
	}

	[Fact]
	public async Task CallTool_ToolException_ReturnsTheEnvelopeWithoutStructuredContent()
	{
		await using TestMcpPipeline pipeline = await TestMcpPipeline.StartAsync();

		CallToolResult result = await pipeline.CallAsync(ContractProbeTool.FailName, """{"failure":"not_found"}""");

		ToolError error = TestMcpPipeline.AssertError(result, ToolErrorKind.NotFound);
		Assert.Equal(
			"""{"error":{"kind":"not_found","message":"The probe object is missing.","hostEffect":"not_started","retryable":false,"hint":"List probes first."}}""",
			Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text);
		Assert.Equal(ToolHostEffect.NotStarted, error.HostEffect);
	}

	[Fact]
	public async Task CallTool_PartialEffect_CarriesItsDetails()
	{
		await using TestMcpPipeline pipeline = await TestMcpPipeline.StartAsync();

		CallToolResult result = await pipeline.CallAsync(ContractProbeTool.FailName, """{"failure":"partial"}""");

		ToolError error = TestMcpPipeline.AssertError(result, ToolErrorKind.PartialEffect);
		Assert.Equal(ToolHostEffect.Started, error.HostEffect);
		Assert.False(error.Retryable);
		Assert.Equal("""{"completed":1,"failedIndex":1}""", error.Details!.Value.GetRawText());
	}

	[Fact]
	public async Task CallTool_ClientFailure_KeepsItsKindOperationAndHostEffect()
	{
		await using TestMcpPipeline pipeline = await TestMcpPipeline.StartAsync();

		CallToolResult result = await pipeline.CallAsync(ContractProbeTool.FailName, """{"failure":"client_write"}""");

		ToolError error = TestMcpPipeline.AssertError(result, ToolErrorKind.MemoryWriteFailed);
		Assert.Equal("Memory.WriteBytes", error.Operation);
		Assert.Equal(ToolHostEffect.Started, error.HostEffect);
		Assert.False(error.Retryable);
		Assert.Equal(ToolFailureMapping.MemoryWriteHint, error.Hint);
	}

	[Fact]
	public async Task CallTool_ClientCancellationBeforeWork_IsRetryableCancelled()
	{
		await using TestMcpPipeline pipeline = await TestMcpPipeline.StartAsync();

		CallToolResult result =
			await pipeline.CallAsync(ContractProbeTool.FailName, """{"failure":"client_cancelled"}""");

		ToolError error = TestMcpPipeline.AssertError(result, ToolErrorKind.Cancelled);
		Assert.Equal(ToolHostEffect.NotStarted, error.HostEffect);
		Assert.True(error.Retryable);
	}

	[Theory]
	[InlineData("{}")]
	[InlineData("""{"text":"a","count":{"nested":true}}""")]
	[InlineData("""{"text":"a","count":"twelve"}""")]
	public async Task CallTool_ArgumentBindingFailure_IsInvalidArgumentNotStarted(string arguments)
	{
		await using TestMcpPipeline pipeline = await TestMcpPipeline.StartAsync();

		CallToolResult result = await pipeline.CallAsync(ContractProbeTool.ConvertName, arguments);

		ToolError error = TestMcpPipeline.AssertError(result, ToolErrorKind.InvalidArgument);
		Assert.Equal(ToolHostEffect.NotStarted, error.HostEffect);
		Assert.False(error.Retryable);
	}

	[Fact]
	public async Task CallTool_UnexpectedException_IsInternalUnknownWithoutItsMessage()
	{
		await using TestMcpPipeline pipeline = await TestMcpPipeline.StartAsync();

		CallToolResult result = await pipeline.CallAsync(ContractProbeTool.FailName, """{"failure":"boom"}""");

		ToolError error = TestMcpPipeline.AssertError(result, ToolErrorKind.Internal);
		Assert.Equal(ToolHostEffect.Unknown, error.HostEffect);
		Assert.Equal(CheatEngineToolFilters.UnexpectedMessage, error.Message);
		Assert.DoesNotContain(ContractProbeTool.SecretMessage,
			Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text, StringComparison.Ordinal);
	}

	[Fact]
	public async Task CallTool_ProtocolException_StaysAJsonRpcError()
	{
		await using TestMcpPipeline pipeline = await TestMcpPipeline.StartAsync();

		McpProtocolException exception = await Assert.ThrowsAsync<McpProtocolException>(() =>
			pipeline.CallAsync(ContractProbeTool.FailName, """{"failure":"protocol"}"""));

		Assert.Equal(McpErrorCode.InvalidRequest, exception.ErrorCode);
	}

	[Fact]
	public async Task CallTool_StringifiedArrayNullAndBooleanText_AreRepairedBeforeBinding()
	{
		await using TestMcpPipeline pipeline = await TestMcpPipeline.StartAsync();

		CallToolResult result = await pipeline.CallAsync(ContractProbeTool.ConvertName,
			"""{"text":"[\"kept\"]","values":"[\"a\",\"b\"]","flag":"true","count":"3","label":null}""");

		Assert.NotEqual(true, result.IsError);
		Assert.Equal("""{"text":"[\u0022kept\u0022]","values":["a","b"],"flag":true,"count":3,"label":"default"}""",
			Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text);
	}

	[Fact]
	public async Task CallTool_SourceGeneratedJsonPipeline_ServesRegisteredRecords()
	{
		await using TestMcpPipeline pipeline = await TestMcpPipeline.StartAsync();

		CallToolResult result = await pipeline.CallAsync(ContractProbeTool.ConvertName, """{"text":"strict"}""");

		Assert.NotEqual(true, result.IsError);
		Assert.Equal("strict", Assert.IsType<JsonElement>(result.StructuredContent).GetProperty("text").GetString());
	}

	[Theory]
	[InlineData("not_found", McpErrorCode.ResourceNotFound, "not_found", "not_started", false)]
	[InlineData("invalid", McpErrorCode.InvalidParams, "invalid_argument", "not_started", false)]
	[InlineData("busy", McpErrorCode.InternalError, "busy", "not_started", true)]
	[InlineData("client", McpErrorCode.InternalError, "not_attached", "not_started", false)]
	public async Task ReadResource_ToolFailure_MapsCodeAndData(string kind, McpErrorCode code, string errorKind,
		string hostEffect, bool retryable)
	{
		await using TestMcpPipeline pipeline = await TestMcpPipeline.StartAsync();

		McpProtocolException exception = await Assert.ThrowsAsync<McpProtocolException>(() =>
			pipeline.Client.ReadResourceAsync(ContractProbeResource.UriPrefix + kind,
				cancellationToken: TestContext.Current.CancellationToken).AsTask());

		Assert.Equal(code, exception.ErrorCode);
		Assert.Equal(errorKind, exception.Data["kind"]);
		Assert.Equal(hostEffect, exception.Data["hostEffect"]);
		Assert.Equal(retryable, exception.Data["retryable"]);
		Assert.All(exception.Data.Values.Cast<object?>(), static value => Assert.True(value is string or bool));
	}

	[Fact]
	public async Task ReadResource_Success_IsUnchanged()
	{
		await using TestMcpPipeline pipeline = await TestMcpPipeline.StartAsync();

		ReadResourceResult result = await pipeline.Client.ReadResourceAsync(ContractProbeResource.UriPrefix + "ok",
			cancellationToken: TestContext.Current.CancellationToken);

		Assert.Equal("probe", Assert.IsType<TextResourceContents>(Assert.Single(result.Contents)).Text);
	}

	[Theory]
	[InlineData(ToolErrorKind.NotFound, null, McpErrorCode.ResourceNotFound)]
	[InlineData(ToolErrorKind.NotFound, "2025-06-18", McpErrorCode.ResourceNotFound)]
	[InlineData(ToolErrorKind.NotFound, "2025-11-25", McpErrorCode.ResourceNotFound)]
	[InlineData(ToolErrorKind.NotFound, "2026-07-28", McpErrorCode.InvalidParams)]
	[InlineData(ToolErrorKind.InvalidArgument, "2025-06-18", McpErrorCode.InvalidParams)]
	[InlineData(ToolErrorKind.Internal, "2026-07-28", McpErrorCode.InternalError)]
	[InlineData(ToolErrorKind.NotAttached, "2025-06-18", McpErrorCode.InternalError)]
	public void ResourceErrorCode_KindAndProtocolVersion_SelectTheJsonRpcCode(ToolErrorKind kind, string? version,
		McpErrorCode expected)
	{
		Assert.Equal(expected, CheatEngineToolFilters.ResourceErrorCode(kind, version));
	}
}
