namespace CheatEngine.Mcp.Tests.LiveQualification.Infrastructure;

/// <summary>Reads <see cref="ExecutableFacts" />; tests substitute one for fake files.</summary>
internal interface IExecutableInspector
{
	/// <summary>Describes the executable at <paramref name="path" />.</summary>
	public ExecutableFacts Describe(string path);
}
