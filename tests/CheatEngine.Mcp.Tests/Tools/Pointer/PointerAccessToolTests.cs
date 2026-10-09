using System.Text.Json;

using CheatEngine.Mcp.Core.Contract;
using CheatEngine.Mcp.Tests.Support;

using ModelContextProtocol.Protocol;

namespace CheatEngine.Mcp.Tests.Tools.Pointer;

/// <summary>Portable input validation for <c>pointer_get_access_info</c>.</summary>
public sealed class PointerAccessToolTests
{
	[Theory]
	[InlineData("", "401000", 4, "x64", ToolErrorKind.InvalidArgument, "instructionText")]
	[InlineData("mov eax,[rax]", "nope", 4, "x64", ToolErrorKind.InvalidArgument, "instructionAddress")]
	[InlineData("mov eax,[rax]", "401000", 0, "x64", ToolErrorKind.InvalidArgument, "instructionLength")]
	[InlineData("mov eax,[rax]", "401000", 4, "arm64", ToolErrorKind.InvalidArgument, "architecture")]
	public async Task GetAccessInfo_InvalidManagedInput_RefusesBeforeDispatch(string text, string address, int length,
		string architecture, ToolErrorKind kind, string parameter)
	{
		await using PointerFixture fixture = new();
		await using TestMcpPipeline pipeline = await fixture.StartPipelineAsync();
		string arguments = JsonSerializer.Serialize(new
		{
			instructionText = text,
			instructionAddress = address,
			instructionLength = length,
			architecture,
			registers = new Dictionary<string, string>()
		});

		ToolError error = TestMcpPipeline.AssertError(
			await pipeline.CallAsync(CheatEngineToolNames.PointerGetAccessInfo, arguments), kind);

		Assert.Equal(parameter, error.Details!.Value.GetProperty("parameter").GetString());
		Assert.Equal(0, fixture.Dispatches);
	}

	[Fact]
	public async Task GetAccessInfo_ContradictoryBytesLength_ReturnsTypedResultBeforeDispatch()
	{
		await using PointerFixture fixture = new();
		await using TestMcpPipeline pipeline = await fixture.StartPipelineAsync();

		CallToolResult result = await pipeline.CallAsync(CheatEngineToolNames.PointerGetAccessInfo,
			"""{"instructionText":"mov eax,[rax]","instructionAddress":"401000","instructionLength":4,"architecture":"x64","instructionBytes":"48 8B","registers":{"RAX":"1000"}}""");

		Assert.NotEqual(true, result.IsError);
		JsonElement content = Assert.IsType<JsonElement>(result.StructuredContent);
		Assert.Equal("contradictory_facts", content.GetProperty("status").GetString());
		Assert.Equal(0, fixture.Dispatches);
	}
}
