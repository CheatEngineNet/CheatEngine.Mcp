using CheatEngine.Mcp.Core.Contract;
using CheatEngine.Mcp.Tools.Il2Cpp;

namespace CheatEngine.Mcp.Tests.Tools.Il2Cpp;

/// <summary>Behavior coverage for the narrow, deliberately non-C# dump_cs_v1 parser.</summary>
public sealed class Il2CppDumpParserTests
{
	private static CancellationToken Token => TestContext.Current.CancellationToken;

	[Fact]
	public void Parse_RepresentativeDump_PreservesStableTypeMemberAndRvaFacts()
	{
		Parsed parsed = Il2CppDumpParser.Parse("""
// Image 0: Assembly-CSharp.dll - 0
// Image 1: Game.dll - 200
// Namespace: Example.Game
[Serializable]
public class Player<T> : Base // TypeDefIndex: 123
{
// Fields
public int Health; // 0x10
public static int Count; // 0x0
public const int MaxCount = 99; // 0x0
// Properties
public int Ignored { get; }
// Methods
// RVA: 0x1234 Offset: 0x834 VA: 0x180001234
public int Heal(List<T> values) { }
// RVA: -1 Offset: -1
public abstract void Missing();
}
// Namespace:
public interface IService // TypeDefIndex: 200
{
// Methods
// RVA: 0x2000 Offset: 0x1000
public void Run();
}
""", Token);

		Assert.Equal(2, parsed.Types.Length);
		Assert.Equal(new Il2CppType("Assembly-CSharp.dll:123", "Assembly-CSharp.dll", "Example.Game", "Player<T>", "class", 123), parsed.Types[0]);
		Assert.Equal(new Il2CppMember("Assembly-CSharp.dll:123:field:0", "Assembly-CSharp.dll:123", "field", "Health", "public int Health", "10", null), parsed.Members[0]);
		Assert.Null(parsed.Members[1].Offset);
		Assert.Equal(new Il2CppMember("Assembly-CSharp.dll:123:field:2", "Assembly-CSharp.dll:123", "field", "MaxCount", "public const int MaxCount = 99", null, null), parsed.Members[2]);
		Assert.Equal("1234", parsed.Members[3].Rva);
		Assert.Null(parsed.Members[4].Rva);
		Assert.Equal("Game.dll:200:method:0", parsed.Members[5].MemberId);
	}

	[Theory]
	[InlineData("// Image 0: A.dll - 0\npublic class A // TypeDefIndex: 1\n{\n// Methods\npublic void Missing() { }\n}", "immediately follow")]
	[InlineData("// Image 0: A.dll - 0\npublic class A // TypeDefIndex: 1\n{\n}\npublic class B // TypeDefIndex: 1\n{\n}\n", "duplicate")]
	[InlineData("public class A // TypeDefIndex: 1\n{\n}\n", "not a supported")]
	public void Parse_UnsupportedOrAmbiguousShape_RefusesTheWholeImport(string text, string message)
	{
		CheatEngineToolException error = Assert.Throws<CheatEngineToolException>(() => Il2CppDumpParser.Parse(text, Token));

		Assert.Equal(ToolErrorKind.InvalidArgument, error.Error.Kind);
		Assert.Contains(message, error.Error.Message, StringComparison.OrdinalIgnoreCase);
	}

	[Fact]
	public void Parse_AllImageHeadersBeforeTypes_AssignsEachTypeToItsTypeDefIndexRange()
	{
		Parsed parsed = Il2CppDumpParser.Parse("""
// Image 0: First.dll - 0
// Image 1: Second.dll - 10
// Namespace: N
public class First // TypeDefIndex: 1
{
}
public class Second // TypeDefIndex: 10
{
}
""", Token);

		Assert.Equal("First.dll:1", parsed.Types[0].TypeId);
		Assert.Equal("Second.dll:10", parsed.Types[1].TypeId);
	}

	[Theory]
	[InlineData("// Image 0: A.dll - 0\npublic class A // TypeDefIndex: 0\n{\n// Methods\n// RVA: 0x1\n// RVA: 0x2\npublic void M() { }\n}\n")]
	[InlineData("// Image 0: A.dll - 0\npublic class A // TypeDefIndex: 0\n{\n// Methods\n// RVA: 0x1\n}\n")]
	[InlineData("// Image 0: A.dll - 0\npublic class A // TypeDefIndex: 0\n{\n// Fields\ninvalid\n}\n")]
	[InlineData("// Image 0: A.dll - 0\npublic class A // TypeDefIndex: 0\n{\n// Methods\n// RVA: 0x100000000\npublic void M() { }\n}\n")]
	public void Parse_MalformedTopologyOrRva_IsRefused(string text)
	{
		CheatEngineToolException error = Assert.Throws<CheatEngineToolException>(() => Il2CppDumpParser.Parse(text, Token));

		Assert.Equal(ToolErrorKind.InvalidArgument, error.Error.Kind);
	}

	[Fact]
	public void Parse_EmptyImageAndUnspacedTypeIndex_PreserveCorrectOwnerAndWholeIndex()
	{
		Parsed parsed = Il2CppDumpParser.Parse("// Image 0: Empty.dll - 0\n// Image 1: Game.dll - 0\npublic class A // TypeDefIndex:123\n{\n}\n", Token);

		Assert.Equal(new Il2CppType("Game.dll:123", "Game.dll", "", "A", "class", 123), Assert.Single(parsed.Types));
	}

	[Fact]
	public void Parse_QuotedConstantsGenericNamesAndCommentBlocks_KeepOnlyDeclaredMembers()
	{
		Parsed parsed = Il2CppDumpParser.Parse("""
// Image 0: A.dll - 0
public class A // TypeDefIndex: 0
{
// Fields
public const string Url = "https://example.invalid/path";
// Methods
// RVA: 0x20
public T Read<T, U>(string text = ")") { }
/* GenericInstMethod :
// RVA: 0x9999
public T Extra() { }
*/
}
""", Token);

		Assert.Equal(2, parsed.Members.Length);
		Assert.Equal("Url", parsed.Members[0].Name);
		Assert.Contains("https://example.invalid/path", parsed.Members[0].Signature, StringComparison.Ordinal);
		Assert.Null(parsed.Members[0].Offset);
		Assert.Equal("Read<T, U>", parsed.Members[1].Name);
		Assert.Equal("20", parsed.Members[1].Rva);
	}

	[Theory]
	[InlineData("// Fields\npublic int Health // 0x10")]
	[InlineData("// Fields\npublic int Health; // 0x1G")]
	[InlineData("// Fields\npublic const string Text = \"unterminated;")]
	[InlineData("// Methods\n// RVA: 0x20\npublic void M( {")]
	[InlineData("// Methods\n// RVA: 0x20\npublic void M() {")]
	[InlineData("// Methods\n// RVA: 0x20\npublic void M() { ; }")]
	public void Parse_TruncatedOrMalformedDeclaration_RejectsWholeDump(string body)
	{
		string source = "// Image 0: A.dll - 0\npublic class A // TypeDefIndex: 0\n{\n" + body + "\n}\n";

		Assert.Equal(ToolErrorKind.InvalidArgument, Assert.Throws<CheatEngineToolException>(() =>
			Il2CppDumpParser.Parse(source, Token)).Error.Kind);
	}

	[Theory]
	[InlineData("// Image : A.dll - 0\npublic class A // TypeDefIndex: 0\n{\n}\n")]
	[InlineData("// Image 0: A.dll - 0\npublic class A // TypeDefIndex:\n{\n}\n")]
	[InlineData("// Image 0: A.dll - 0\npublic class A // TypeDefIndex: 0\n{\n}\n// Image 1: B.dll - 1")]
	[InlineData("// Image 0: A.dll - 0\npublic class A // TypeDefIndex: 0\n{\n// Methods\n// RVA: 0x1\0\npublic void M() { }\n}\n")]
	public void Parse_MalformedHeaderOrIndex_ReturnsTypedRefusal(string source)
	{
		Assert.Equal(ToolErrorKind.InvalidArgument, Assert.Throws<CheatEngineToolException>(() =>
			Il2CppDumpParser.Parse(source, Token)).Error.Kind);
	}

	[Theory]
	[InlineData("public\tstatic int\tCount; // 0x0", "Count", null)]
	[InlineData("public\tconst int\tCount = 1; // 0x0", "Count", null)]
	[InlineData("public string Text = \" static \"; // 0x10", "Text", "10")]
	[InlineData("public string Text = \" const \"; // 0x20", "Text", "20")]
	public void Parse_FieldModifiers_UseTokensBeforeNameAndIgnoreInitializers(string field, string name, string? offset)
	{
		string source = "// Image 0: A.dll - 0\npublic class A // TypeDefIndex: 0\n{\n// Fields\n" + field + "\n}\n";

		Il2CppMember member = Assert.Single(Il2CppDumpParser.Parse(source, Token).Members);

		Assert.Equal(name, member.Name);
		Assert.Equal(offset, member.Offset);
	}

	[Fact]
	public void Parse_CancelledBeforeWork_LeavesNoResult()
	{
		using CancellationTokenSource cancelled = new();
		cancelled.Cancel();

		Assert.Throws<OperationCanceledException>(() => Il2CppDumpParser.Parse("// Image 0: A.dll - 0", cancelled.Token));
	}
}
