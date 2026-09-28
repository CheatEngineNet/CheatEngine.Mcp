using System.Runtime.Versioning;

namespace CheatEngine.Mcp.Tests.LiveQualification;

[CollectionDefinition(Name, DisableParallelization = true)]
[SupportedOSPlatform("windows")]
public sealed class LiveQualificationSerialGroup : ICollectionFixture<LiveQualificationFixture>
{
	public const string Name = "Live qualification";
}
