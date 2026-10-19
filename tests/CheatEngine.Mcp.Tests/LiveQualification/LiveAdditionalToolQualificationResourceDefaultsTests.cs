using System.Runtime.Versioning;
using System.Text.Json.Nodes;

namespace CheatEngine.Mcp.Tests.LiveQualification;

[SupportedOSPlatform("windows")]
public sealed class LiveAdditionalToolQualificationResourceDefaultsTests
{
	[Fact]
	public void AssertOwnedAllocationResource_AcceptsSchemaDefaultFieldsWhenOmitted()
	{
		JsonNode resource = JsonNode.Parse("""
			{
			  "id": "allocation-test-1",
			  "kind": "allocation",
			  "name": "owned",
			  "address": "1234",
			  "size": 16
			}
			""")!;

		LiveAdditionalToolQualification.AssertOwnedAllocationResource(resource, "allocation-test-1", "owned", "1234", 16, 42);
	}

	[Theory]
	[InlineData("orphaned", "true")]
	[InlineData("requiresManualRecovery", "true")]
	[InlineData("orphaned", "null")]
	[InlineData("requiresManualRecovery", "null")]
	[InlineData("id", "\"wrong\"")]
	[InlineData("processId", "43")]
	[InlineData("size", "17")]
	public void AssertOwnedAllocationResource_RejectsWrongFields(string property, string replacement)
	{
		JsonObject resource = new()
		{
			["id"] = "allocation-test-1",
			["kind"] = "allocation",
			["name"] = "owned",
			["address"] = "1234",
			["size"] = 16,
			["processId"] = 42
		};
		resource[property] = JsonNode.Parse(replacement);

		Assert.ThrowsAny<Exception>(() => LiveAdditionalToolQualification.AssertOwnedAllocationResource(
			resource, "allocation-test-1", "owned", "1234", 16, 42));
	}

	[Fact]
	public void AssertOwnedAllocationResource_RejectsRequiredFieldOmission()
	{
		JsonNode resource = JsonNode.Parse("""
			{
			  "id": "allocation-test-1",
			  "kind": "allocation",
			  "name": "owned",
			  "address": "1234",
			  "processId": 42
			}
			""")!;

		Assert.ThrowsAny<Exception>(() => LiveAdditionalToolQualification.AssertOwnedAllocationResource(
			resource, "allocation-test-1", "owned", "1234", 16, 42));
	}
}
