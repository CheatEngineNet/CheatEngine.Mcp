using System.Diagnostics;
using System.Reflection.PortableExecutable;
using System.Runtime.Versioning;

namespace CheatEngine.Mcp.Tests.LiveQualification.Infrastructure;

/// <summary>
///     Reads the COFF machine with <see cref="PEReader" /> and the version resource with
///     <see cref="FileVersionInfo" />.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class PortableExecutableInspector : IExecutableInspector
{
	/// <summary>The shared instance.</summary>
	internal static PortableExecutableInspector Instance
	{
		get;
	} = new();

	/// <inheritdoc />
	public ExecutableFacts Describe(string path)
	{
		using FileStream stream = File.OpenRead(path);
		using PEReader reader = new(stream);
		return new ExecutableFacts(reader.PEHeaders.CoffHeader.Machine.ToString(),
			FileVersionInfo.GetVersionInfo(path).FileVersion);
	}
}
