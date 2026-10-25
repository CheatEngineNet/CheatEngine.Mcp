using System.Runtime.Versioning;

namespace CheatEngine.Mcp.Tests.LiveQualification;

[SupportedOSPlatform("windows")]
public sealed class LiveActivePluginLifecycleQualificationTests
{
	[Theory]
	[InlineData("296A0FA0000", 0x296A0FA0000UL)]
	[InlineData("0x296A0FA0000", 0x296A0FA0000UL)]
	[InlineData("0X296A0FA0000", 0x296A0FA0000UL)]
	public void ParseHex_AcceptsPrefixedAndUnprefixedAllocationAddresses(string text, ulong expected)
	{
		Assert.Equal(expected, LiveActivePluginLifecycleQualification.ParseHex(text));
	}

	[Theory]
	[InlineData("")]
	[InlineData("0x")]
	[InlineData("not-hex")]
	[InlineData("0")]
	public void ParseHex_RejectsInvalidAddresses(string text)
	{
		Assert.Throws<InvalidDataException>(() => LiveActivePluginLifecycleQualification.ParseHex(text));
	}

	[Fact]
	public void ParseResponse_RecognizesTheDisabledStatusMenuAndAbsentBreakpoint()
	{
		LivePluginDisabledObservation observation = LivePluginDisabledObservation.ParseResponse(
			ObservationResponse("disabled", "absent", "4321", "1", "number:1=number:22136"), 17, "0x1234", 4321);

		Assert.False(observation.PluginChecked);
		Assert.True(observation.StatusMenuPresent);
		Assert.True(observation.StatusMenuDisabled);
		Assert.False(observation.BreakpointPresent);
		Assert.Equal(4321, observation.OpenedProcessId);
		Assert.Equal(1, observation.BreakpointCount);
		Assert.Equal("number:1=number:22136", Assert.Single(observation.BreakpointSamples));
		Assert.False(observation.CleanupRemovalSucceeded);
	}

	[Theory]
	[InlineData("wrong")]
	[InlineData("ok\n17\nobserve\n0x1234\ndisabled\npresent\nabsent")]
	public void ParseResponse_RejectsMalformedOrLegacyStatusObservations(string response)
	{
		Assert.Throws<InvalidOperationException>(() =>
			LivePluginDisabledObservation.ParseResponse(response, 17, "0x1234", 4321));
	}

	[Fact]
	public void ParseResponse_RetainsBreakpointPresenceAsAFailedCleanupObservation()
	{
		LivePluginDisabledObservation observation = LivePluginDisabledObservation.ParseResponse(
			ObservationResponse("disabled", "present", "4321", "1", "number:1=number:4660"), 17, "0x1234", 4321);

		Assert.True(observation.StatusMenuDisabled);
		Assert.True(observation.BreakpointPresent);
		Assert.Equal("number:1=number:4660", Assert.Single(observation.BreakpointSamples));
	}

	[Fact]
	public void ParseResponse_ReportsWhetherTheNativeBreakpointRemovalSucceeded()
	{
		LivePluginDisabledObservation failed = LivePluginDisabledObservation.ParseResponse(
			ObservationResponse("disabled", "absent", "4321", "0", "", ["called", "number", "88", "false",
				"boolean", "false", "", "1", "before", "0", "", "fields="]), 17, "0x1234", 4321);
		LivePluginDisabledObservation succeeded = LivePluginDisabledObservation.ParseResponse(
			ObservationResponse("disabled", "absent", "4321", "0", "", ["called", "table", "table:88", "true",
				"boolean", "true", "", "1", "before", "0", "", "fields="]), 17, "0x1234", 4321);

		Assert.False(failed.CleanupRemovalSucceeded);
		Assert.True(succeeded.CleanupRemovalSucceeded);
	}

	[Theory]
	[InlineData("not-a-pid", "1", "number:4660")]
	[InlineData("0", "-1", "")]
	[InlineData("4321", "0", "number:4660")]
	[InlineData("9999", "1", "number:4660")]
	public void ParseResponse_RejectsInvalidProcessAndBreakpointDiagnostics(string processId, string count, string samples)
	{
		string response = ObservationResponse("disabled", "absent", processId, count, samples);

		Assert.Throws<InvalidOperationException>(() => LivePluginDisabledObservation.ParseResponse(response, 17, "0x1234", 4321));
	}

	private static string ObservationResponse(string status, string breakpoint, string processId, string count, string samples,
		string[]? cleanupProbeFields = null)
	{
		cleanupProbeFields ??= ["uninstalled", "", "", "", "", "", "", "", "", "", "", ""];
		return string.Join('\n', new[]
			{
				"ok", "17", "observe", "0x1234", "disabled", status, breakpoint, processId, count, samples
			}
			.Concat(cleanupProbeFields)
			.Append($"raw:{count}:{samples}"));
	}
}
