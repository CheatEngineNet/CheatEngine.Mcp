using System.Globalization;
using System.Text;

using CheatEngine.Mcp.Core.Contract;
using CheatEngine.Mcp.Tools.Il2Cpp;

namespace CheatEngine.Mcp.Tests.Fuzzing;

/// <summary>Fixed-seed, bounded generated cases for the strict dump_cs_v1 parser.</summary>
public sealed class Il2CppDumpFuzzTests
{
	private const int Seed = 20261019;
	private const int Cases = 96;
	private static CancellationToken Token => TestContext.Current.CancellationToken;

	[Fact]
	public void Parse_GeneratedValidDumps_PreservesEveryDeclaredTypeAndMember()
	{
		Random random = new(Seed);
		for (int caseIndex = 0; caseIndex < Cases; caseIndex++)
		{
			(string source, Expected expected) = Valid(random, caseIndex);
			try
			{
				Parsed parsed = Il2CppDumpParser.Parse(source, Token);
				Assert.Equal(expected.Types.Length, parsed.Types.Length);
				Assert.Equal(expected.Members.Length, parsed.Members.Length);
				Assert.Equal(expected.Types, parsed.Types.Select(type => (type.TypeId, type.Name)).ToArray());
				Assert.Equal(expected.Members, parsed.Members.Select(member => (member.MemberId, member.Name, member.Rva, member.Offset)).ToArray());
			}
			catch (Exception error)
			{
				Assert.Fail($"Valid dump seed {Seed}, case {caseIndex}: {error}\n{source}");
			}
		}
	}

	[Fact]
	public void Parse_GeneratedMalformedDumps_RefusesEveryRequiredTokenRemoval()
	{
		Random random = new(Seed);
		for (int caseIndex = 0; caseIndex < Cases; caseIndex++)
		{
			string malformed = Malformed(random, caseIndex);
			try
			{
				CheatEngineToolException error = Assert.Throws<CheatEngineToolException>(() => Il2CppDumpParser.Parse(malformed, Token));
				Assert.Equal(ToolErrorKind.InvalidArgument, error.Error.Kind);
				Assert.Equal("path", error.Error.Details!.Value.GetProperty("parameter").GetString());
			}
			catch (Exception error)
			{
				Assert.Fail($"Malformed dump seed {Seed}, case {caseIndex}: {error}\n{malformed}");
			}
		}
	}

	private static (string Source, Expected Expected) Valid(Random random, int caseIndex)
	{
		int images = random.Next(1, 4);
		int typeCount = random.Next(1, 4);
		int[] starts = [.. Enumerable.Range(0, images).Select(index => index == 0 ? 0 : random.Next(0, typeCount)).Order()];
		StringBuilder source = new();
		if (caseIndex % 5 == 0)
		{
			source.Append('\uFEFF');
		}

		for (int image = 0; image < images; image++)
		{
			source.AppendLine(CultureInfo.InvariantCulture, $"// Image {image}: Image{image}.dll - {starts[image]}");
		}

		source.AppendLine("// Namespace: Fuzz");
		List<(string, string)> types = [];
		List<(string, string, string?, string?)> members = [];
		for (int typeIndex = 0; typeIndex < typeCount; typeIndex++)
		{
			int image = Enumerable.Range(0, images).Last(index => starts[index] <= typeIndex);
			string name = caseIndex % 3 == 0 ? $"Type{typeIndex}<T>" : $"Type_{caseIndex}_{typeIndex}";
			source.AppendLine(CultureInfo.InvariantCulture, $"public class {name} // TypeDefIndex: {typeIndex}");
			source.AppendLine("{");
			source.AppendLine("// Fields");
			bool staticField = (caseIndex + typeIndex) % 2 == 0;
			source.AppendLine(staticField ? "public static int Count; // 0x0" : "public int Value; // 0x10");
			source.AppendLine("// Methods");
			uint rva = (uint) random.NextInt64(1, uint.MaxValue);
			source.AppendLine(CultureInfo.InvariantCulture, $"// RVA: 0x{rva:X} Offset: -1");
			source.AppendLine(caseIndex % 4 == 0 ? "public void Run<T>(T value) { }" : "public int Run(int value) { }");
			source.AppendLine("}");
			string typeId = $"Image{image}.dll:{typeIndex}";
			types.Add((typeId, name));
			members.Add(($"{typeId}:field:0", staticField ? "Count" : "Value", null, staticField ? null : "10"));
			members.Add(($"{typeId}:method:1", caseIndex % 4 == 0 ? "Run<T>" : "Run", rva.ToString("X", CultureInfo.InvariantCulture), null));
		}
		return (source.ToString(), new([.. types], [.. members]));
	}

	private static string Malformed(Random random, int caseIndex)
	{
		(string source, _) = Valid(random, caseIndex);
		return (caseIndex % 4) switch
		{
			0 => source.Replace("; // 0x", " // 0x", StringComparison.Ordinal),
			1 => source[..source.LastIndexOf('}')],
			2 => source.Replace("TypeDefIndex: 0", "TypeDefIndex: -1", StringComparison.Ordinal),
			_ => source.Replace("// RVA: 0x", "// RVA: 0x100000000", StringComparison.Ordinal)
		};
	}

	private sealed record Expected((string TypeId, string Name)[] Types,
		(string MemberId, string Name, string? Rva, string? Offset)[] Members);
}
