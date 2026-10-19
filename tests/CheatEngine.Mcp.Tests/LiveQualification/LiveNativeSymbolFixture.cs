using System.Diagnostics;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text.Json.Nodes;

namespace CheatEngine.Mcp.Tests.LiveQualification;

[SupportedOSPlatform("windows")]
internal static class LiveNativeSymbolFixture
{
	private static readonly string[] RequiredExports = ["CemcpFixtureLayoutSize", "CemcpFixtureTick"];
	internal static async Task<NativeSymbolFixture> BuildAsync(string repository, string output, CancellationToken token)
	{
		Directory.CreateDirectory(output);
		LiveCompilerCandidate.RequireNoReparseAncestors(output);
		string script = Path.Combine(repository, "eng", "Build-LiveNativeSymbolFixture.ps1");
		if (!File.Exists(script))
		{
			throw new FileNotFoundException("The native symbol fixture builder is missing.", script);
		}

		ProcessStartInfo start = new("pwsh")
		{
			UseShellExecute = false,
			RedirectStandardOutput = true,
			RedirectStandardError = true,
			CreateNoWindow = true
		};
		start.ArgumentList.Add("-NoProfile");
		start.ArgumentList.Add("-File");
		start.ArgumentList.Add(script);
		start.ArgumentList.Add("-OutputDirectory");
		start.ArgumentList.Add(output);
		using Process process = Process.Start(start) ?? throw new InvalidOperationException("The native symbol fixture builder did not start.");
		Task<string> stdout = process.StandardOutput.ReadToEndAsync(token);
		Task<string> stderr = process.StandardError.ReadToEndAsync(token);
		try
		{
			await process.WaitForExitAsync(token).WaitAsync(TimeSpan.FromSeconds(70), token);
		}
		finally { if (!process.HasExited) { process.Kill(true); await process.WaitForExitAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10), CancellationToken.None); } }
		string outputText = await stdout;
		string error = await stderr;
		if (process.ExitCode != 0)
		{
			throw new InvalidOperationException($"Native symbol fixture build failed: {error}");
		}

		string manifest = outputText.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).LastOrDefault()
			?? throw new InvalidDataException("Native symbol fixture builder returned no manifest path.");
		if (!Path.IsPathFullyQualified(manifest) || !LiveQualificationOptIn.IsSameOrBelow(manifest, output))
		{
			throw new InvalidDataException("The fixture manifest escaped its owned root.");
		}

		JsonNode node = JsonNode.Parse(await File.ReadAllTextAsync(manifest, token)) ?? throw new InvalidDataException("The fixture manifest is empty.");
		string source = Text(node["source"], "path");
		string dll = Text(node["dll"], "path");
		string pdb = Text(node["pdb"], "path");
		string reviewedSource = Path.GetFullPath(Path.Combine(repository, "tests", "CheatEngine.Mcp.LiveTarget", "NativeSymbolFixture", "CemcpNativeSymbolFixture.cpp"));
		if (!string.Equals(Path.GetFullPath(source), reviewedSource, StringComparison.OrdinalIgnoreCase))
		{
			throw new InvalidDataException("The fixture manifest source differs from the reviewed tracked fixture.");
		}

		foreach (string path in new[] { dll, pdb })
		{
			if (!Path.IsPathFullyQualified(path) || !LiveQualificationOptIn.IsSameOrBelow(path, output))
			{
				throw new InvalidDataException("The fixture manifest referenced an artifact outside its owned root.");
			}
		}

		NativeSymbolFixture.RequireBoundedFile(source, 64 * 1024);
		NativeSymbolFixture.RequireBoundedFile(dll, 1024 * 1024);
		NativeSymbolFixture.RequireBoundedFile(pdb, 16 * 1024 * 1024);
		NativeSymbolFixture artifact = new(source, dll, pdb, Text(node["source"], "sha256"), Text(node["dll"], "sha256"), Text(node["pdb"], "sha256"), node["dll"]!["length"]!.GetValue<long>(), node["pdb"]!["length"]!.GetValue<long>(), NativeSymbolFixture.ReadCodeView(dll), NativeSymbolFixture.ReadRequiredExportRvas(dll, RequiredExports));
		artifact.VerifyBeforeHostStart();
		return artifact;
	}
	private static string Text(JsonNode? node, string property) => node?[property]?.GetValue<string>() ?? throw new InvalidDataException($"Missing fixture manifest {property}.");
}
