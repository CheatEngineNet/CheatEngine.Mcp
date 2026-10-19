using System.Runtime.Versioning;

namespace CheatEngine.Mcp.Tests.LiveQualification;

[SupportedOSPlatform("windows")]
public sealed class LiveDebuggerExecutionQualificationTests
{
	[Fact]
	public void Manifest_RejectsOutOfRangeThreadAndAddresses()
	{
		LiveDebuggerExecutionQualification.Manifest valid = new(42, 7, 0x1000, 0x1009, 0x1017, 0x1037, "0x1108", "0x1104");
		valid.Validate(42);
		Assert.Throws<InvalidDataException>(() => (valid with { ThreadId = 0 }).Validate(42));
		Assert.Throws<InvalidDataException>(() => (valid with { Entry = 0x1037 }).Validate(42));
		Assert.Throws<InvalidDataException>(() => (valid with { CodeEnd = 0x1000 }).Validate(42));
		Assert.Throws<InvalidDataException>(() => (valid with { ProcessId = 43 }).Validate(42));
		Assert.Throws<InvalidDataException>(() => (valid with { ProgressAddress = "2000" }).Validate(42));
		Assert.Throws<InvalidDataException>(() => (valid with { Entry = 0x100A }).Validate(42));
		Assert.Throws<InvalidDataException>(() => (valid with { RunTo = 0x1018 }).Validate(42));
	}

	[Fact]
	public void Manifest_RangeIsHalfOpen()
	{
		LiveDebuggerExecutionQualification.Manifest manifest = new(42, 7, 0x1000, 0x1009, 0x1017, 0x1037, "0x1108", "0x1104");
		Assert.True(manifest.Entry >= manifest.CodeStart && manifest.Entry < manifest.CodeEnd);
		Assert.Throws<InvalidDataException>(() => (manifest with { RunTo = manifest.CodeEnd }).Validate(42));
	}
}
