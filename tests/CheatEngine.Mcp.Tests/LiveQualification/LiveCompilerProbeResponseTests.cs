namespace CheatEngine.Mcp.Tests.LiveQualification;

public sealed class LiveCompilerProbeResponseTests
{
	[Theory]
	[InlineData("oversize")]
	[InlineData("extra-lines")]
	[InlineData("empty")]
	[System.Runtime.Versioning.SupportedOSPlatform("windows")]
	public async Task ReadAsync_OutsideTheResponseBounds_Refuses(string example)
	{
		string contents = example switch
		{
			"oversize" => new string('x', 32_769),
			"extra-lines" => "ok\ntrue\n1\npath\nextra",
			_ => string.Empty
		};
		string path = Path.Combine(Path.GetTempPath(), $"CheatEngine.Mcp.CompilerResponse-{Guid.NewGuid():N}.txt");
		try
		{
			await File.WriteAllTextAsync(path, contents, TestContext.Current.CancellationToken);
			await Assert.ThrowsAsync<InvalidDataException>(() => LiveCompilerProbeResponse.ReadAsync(path, "compile"));
		}
		finally { File.Delete(path); }
	}

	[Fact]
	[System.Runtime.Versioning.SupportedOSPlatform("windows")]
	public async Task ReadAsync_InvalidUtf8_RejectsInsteadOfReplacingBytes()
	{
		string path = Path.Combine(Path.GetTempPath(), $"CheatEngine.Mcp.CompilerResponse-{Guid.NewGuid():N}.txt");
		try
		{
			await File.WriteAllBytesAsync(path, [0xff], TestContext.Current.CancellationToken);
			await Assert.ThrowsAsync<System.Text.DecoderFallbackException>(() => LiveCompilerProbeResponse.ReadAsync(path, "compile"));
		}
		finally { File.Delete(path); }
	}

	[Fact]
	[System.Runtime.Versioning.SupportedOSPlatform("windows")]
	public async Task ReadAsync_ExactByteBound_AcceptsCompleteUtf8Response()
	{
		string path = Path.Combine(Path.GetTempPath(), $"CheatEngine.Mcp.CompilerResponse-{Guid.NewGuid():N}.txt");
		try
		{
			await File.WriteAllTextAsync(path, "ok\ntrue\n1\n" + new string('x', 32_758), TestContext.Current.CancellationToken);
			System.Text.Json.Nodes.JsonNode response = await LiveCompilerProbeResponse.ReadAsync(path, "compile");
			Assert.Equal(32_758, response["assemblyPath"]!.GetValue<string>().Length);
		}
		finally { File.Delete(path); }
	}

	[Fact]
	public void Parse_Status_PreservesTheReceiptTypeReadByTheScenario()
	{
		System.Text.Json.Nodes.JsonNode response = LiveCompilerProbeResponse.Parse(["ok", "true", "7"], "status");
		Assert.True(response["available"]!.GetValue<bool>());
		Assert.Equal(7, response["receiptCount"]!.GetValue<int>());
		Assert.Null(response["assemblyPath"]);
	}

	[Theory]
	[InlineData("true", "assemblyPath")]
	[InlineData("false", "diagnostic")]
	public void Parse_CompileResponse_KeepsSuccessAndFailureDistinct(string available, string expectedField)
	{
		System.Text.Json.Nodes.JsonNode response = LiveCompilerProbeResponse.Parse(["ok", available, "1", "value"], "compile");
		Assert.Equal("value", response[expectedField]!.GetValue<string>());
		Assert.Null(response[expectedField == "assemblyPath" ? "diagnostic" : "assemblyPath"]);
	}

	[Theory]
	[InlineData("status", "ok", "true", "-1", null)]
	[InlineData("status", "ok", "true", "2147483648", null)]
	[InlineData("status", "ok", "true", "1", "unexpected")]
	[InlineData("status", "error", "true", "1", null)]
	[InlineData("status", "ok", "maybe", "1", null)]
	[InlineData("compile", "ok", "true", "1", null)]
	[InlineData("compile", "ok", "true", "1", "")]
	[InlineData("unknown", "ok", "true", "1", null)]
	public void Parse_MalformedOrWrongCommandShape_Refuses(string command, string marker, string available, string receipts, string? value)
	{
		string[] lines = value is null ? [marker, available, receipts] : [marker, available, receipts, value];
		Assert.Throws<InvalidDataException>(() => LiveCompilerProbeResponse.Parse(lines, command));
	}
}
