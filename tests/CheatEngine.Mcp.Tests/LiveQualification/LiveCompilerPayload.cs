using System.Security.Cryptography;
using System.Text;

namespace CheatEngine.Mcp.Tests.LiveQualification;

/// <summary>Reviewed fixed compiler inputs; no live scenario accepts caller-supplied source or references.</summary>
internal static class LiveCompilerPayload
{
	internal const string ValidSource = "public static class CheatEngineMcpCompileCsProbe\n" +
		"{\n" +
		"    public static int Run(string value)\n" +
		"    {\n" +
		"        return value == \"CEMCP-COMPILECS-PROBE-V1\" ? 7319 : -7319;\n" +
		"    }\n" +
		"}";
	internal const string InvalidSource = "public static class CheatEngineMcpCompileCsProbe {";
	internal const string InvalidReference = "CEMCP-NOT-AN-ASSEMBLY";
	internal const string ValidSha256 = "C41BCC0E7D83835327D852627C674D4087F87ADFBB6CC3457C92E953D9C286BE";
	internal const string InvalidSourceSha256 = "17C627E367CFDB33D02316E394E19C6B9DECFE1A3CA11E63ACB882D085F337EF";
	internal const string InvalidReferenceSha256 = "EDC29B5453FDD2C53C796B7E2FBC21389587D9360F148A7D229DDF461EA04194";

	internal static string Sha256(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

	internal static void RequireReviewedHashes()
	{
		if (Sha256(ValidSource) != ValidSha256 || Sha256(InvalidSource) != InvalidSourceSha256 ||
			Sha256(InvalidReference) != InvalidReferenceSha256)
		{
			throw new InvalidOperationException("The fixed compiler qualification payload hash differs from the reviewed policy.");
		}
	}
}
