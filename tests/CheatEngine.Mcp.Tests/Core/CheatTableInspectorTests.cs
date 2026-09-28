using System.IO.Compression;
using System.Security;
using System.Text;

using CheatEngine.Mcp.Core.Contract;
using CheatEngine.Mcp.Core.Features;
using CheatEngine.Mcp.Core.Files;
using CheatEngine.Mcp.Core.Tables;

using Microsoft.Extensions.Options;

namespace CheatEngine.Mcp.Tests.Core;

public sealed class CheatTableInspectorTests
{
	private const string ValueRecords = """
	                                    <?xml version="1.0" encoding="utf-8"?>
	                                    <CheatTable CheatEngineTableVersion="46">
	                                      <CheatEntries>
	                                        <CheatEntry>
	                                          <ID>0</ID>
	                                          <Description>"Health"</Description>
	                                          <VariableType>4 Bytes</VariableType>
	                                          <Address>game.exe+1234</Address>
	                                          <CheatEntries>
	                                            <CheatEntry>
	                                              <ID>1</ID>
	                                              <Description>"Ammo"</Description>
	                                              <VariableType>Float</VariableType>
	                                              <Address>game.exe+1238</Address>
	                                            </CheatEntry>
	                                          </CheatEntries>
	                                        </CheatEntry>
	                                      </CheatEntries>
	                                      <UserdefinedSymbols/>
	                                      <Forms/>
	                                      <Comments>Plain table</Comments>
	                                    </CheatTable>
	                                    """;

	[Fact]
	public void Inspect_ValueRecordsOnly_NeedsNoSwitch()
	{
		CheatTableInspection inspection = Inspect("table.CT", ValueRecords);

		Assert.Equal(CheatTableFormat.Xml, inspection.Format);
		Assert.True(inspection.IsInspected);
		Assert.Empty(inspection.Requirements.Features);
		Assert.Equal((false, false, false, 2, 0), (inspection.ContainsLua, inspection.ContainsForms,
			inspection.UsesMono, inspection.RecordCount, inspection.AssemblerScriptCount));
	}

	[Theory]
	[InlineData("<LuaScript>print('x')</LuaScript>", "UnsafeLua")]
	[InlineData("<LuaScript><LuaScriptEntry Name=\"main\">print('x')</LuaScriptEntry></LuaScript>", "UnsafeLua")]
	[InlineData("<luascript/>", "UnsafeLua")]
	[InlineData("<Forms><UDF1 Class=\"TTRAINERFORM\"/></Forms>", "UnsafeLua")]
	public void Inspect_LuaOrForms_NeedUnsafeLua(string element, string expected)
	{
		CheatTableInspection inspection = Inspect("table.ct", Table(element));

		Assert.Equal(expected, string.Join(",", inspection.Requirements.Features));
		Assert.True(inspection.ContainsLua || inspection.ContainsForms);
		Assert.StartsWith("the table has", inspection.Requirements.ReasonFor(McpFeature.UnsafeLua),
			StringComparison.Ordinal);
	}

	[Theory]
	[InlineData("UsesMono=\"1\"", true)]
	[InlineData("usesmono=\"True\"", true)]
	[InlineData("UsesMono=\"\"", true)]
	[InlineData("UsesMono=\"0\"", false)]
	[InlineData("UsesMono=\" false \"", false)]
	[InlineData("Other=\"1\"", false)]
	public void Inspect_UsesMonoOption_NeedsTargetCodeExecution(string attribute, bool usesMono)
	{
		CheatTableInspection inspection = Inspect("table.ct",
			$"<?xml version=\"1.0\"?><CheatTable CheatEngineTableVersion=\"46\" {attribute}><CheatEntries/></CheatTable>");

		Assert.Equal(usesMono, inspection.UsesMono);
		Assert.Equal(usesMono, inspection.Requirements.Requires(McpFeature.TargetCodeExecution));
		Assert.Equal(usesMono ? 1 : 0, inspection.Requirements.Features.Length);
	}

	[Fact]
	public void Inspect_UsesMonoAnywhere_IsFound()
	{
		Assert.True(Inspect("table.ct", Table("<Options UsesMono=\"1\"/>")).UsesMono);
		Assert.True(Inspect("table.ct", Table("<UsesMono>1</UsesMono>")).UsesMono);
		Assert.False(Inspect("table.ct", Table("<UsesMono>0</UsesMono>")).UsesMono);
	}

	[Theory]
	[InlineData("[ENABLE]\nnop\n[DISABLE]\nnop", "AutoAssembler")]
	[InlineData("[ENABLE]\n{$lua}\nreturn ''\n[DISABLE]", "UnsafeLua,AutoAssembler")]
	[InlineData("[ENABLE]\nloadlibrary(x.dll)\n[DISABLE]", "AutoAssembler,TargetCodeExecution")]
	[InlineData("[ENABLE]\nusemono()\n[DISABLE]", "UnsafeLua,AutoAssembler,TargetCodeExecution")]
	[InlineData("[ENABLE]\nkalloc(x,4)\n[DISABLE]", "AutoAssembler,KernelAccess")]
	public void Inspect_AssemblerScripts_NeedAutoAssemblerAndTheClassifierResult(string script, string expected)
	{
		string record = $"""
		                 <CheatEntries>
		                   <CheatEntry>
		                     <ID>3</ID>
		                     <VariableType>Auto Assembler Script</VariableType>
		                     <AssemblerScript Async="1">{SecurityElement.Escape(script)}</AssemblerScript>
		                   </CheatEntry>
		                   <CheatEntry>
		                     <ID>4</ID>
		                     <VariableType>Auto Assembler Script</VariableType>
		                     <AssemblerScript><![CDATA[[ENABLE]
		                 nop
		                 [DISABLE]]]></AssemblerScript>
		                   </CheatEntry>
		                 </CheatEntries>
		                 """;

		CheatTableInspection inspection = Inspect("table.ct", Table(record));

		Assert.Equal(CheatTableFormat.Xml, inspection.Format);
		Assert.Equal(expected, string.Join(",", inspection.Requirements.Features));
		Assert.Equal((2, 2), (inspection.RecordCount, inspection.AssemblerScriptCount));
		foreach (McpFeature feature in
				 inspection.Requirements.Features.Where(static f => f != McpFeature.AutoAssembler))
		{
			Assert.StartsWith("an Auto Assembler script of the table ", inspection.Requirements.ReasonFor(feature),
				StringComparison.Ordinal);
		}
	}

	[Theory]
	[InlineData("trainer.CETRAINER", "<?xml version=\"1.0\"?><CheatTable/>", CheatTableFormat.Trainer)]
	[InlineData("table.ct", "CHEATENGINE\u0001\u0002binary", CheatTableFormat.LegacyBinary)]
	[InlineData("table.ct", "﻿<?xml version=\"1.0\"?><CheatTable/>", CheatTableFormat.Protected)]
	[InlineData("table.ct", "<CheatTable/>", CheatTableFormat.Protected)]
	[InlineData("table.ct", "", CheatTableFormat.Protected)]
	[InlineData("table.ct", "<?xml version=\"1.0\"?><CheatTable", CheatTableFormat.Unparseable)]
	[InlineData("table.ct", "<?xml version=\"1.0\"?><Other/>", CheatTableFormat.Unparseable)]
	[InlineData("table.ct", "<?xml version=\"1.0\"?>", CheatTableFormat.Unparseable)]
	[InlineData("table.ct", "<?xml version=\"1.0\"?><CheatTable obfuscated=\"1\"><rnd/></CheatTable>",
		CheatTableFormat.Obfuscated)]
	[InlineData("table.ct",
		"<?xml version=\"1.0\"?><CheatTable><CheatEntries><CheatEntry><AssemblerScript Encoded=\"1\">xyz</AssemblerScript></CheatEntry></CheatEntries></CheatTable>",
		CheatTableFormat.Obfuscated)]
	[InlineData("table.ct",
		"<?xml version=\"1.0\"?><CheatTable><CheatEntries><CheatEntry><AssemblerScript>a<b/>c</AssemblerScript></CheatEntry></CheatEntries></CheatTable>",
		CheatTableFormat.Unparseable)]
	public void Inspect_OpaqueTable_NeedsUnsafeLuaAutoAssemblerAndCodeExecution(string name, string content,
		CheatTableFormat format)
	{
		CheatTableInspection inspection = Inspect(name, content);

		AssertOpaque(inspection, format);
	}

	[Fact]
	public void Inspect_DocumentTypeDeclaration_IsOpaqueAndNeverExpanded()
	{
		const string content = """
		                       <?xml version="1.0"?>
		                       <!DOCTYPE CheatTable [<!ENTITY lua "<LuaScript>print(1)</LuaScript>">]>
		                       <CheatTable>&lua;</CheatTable>
		                       """;

		AssertOpaque(Inspect("table.ct", content), CheatTableFormat.Unparseable);
	}

	[Fact]
	public void Inspect_CompressedTable_IsProtected()
	{
		using MemoryStream compressed = new();
		using (ZLibStream zlib = new(compressed, CompressionLevel.Optimal, true))
		{
			zlib.Write(Encoding.UTF8.GetBytes(Table("<LuaScript>print(1)</LuaScript>")));
		}

		CheatTableInspection inspection =
			CheatTableInspector.Inspect("table.ct", compressed.ToArray(), true);

		AssertOpaque(inspection, CheatTableFormat.Protected);
		Assert.Contains("compressed or protected", inspection.Requirements.ReasonFor(McpFeature.UnsafeLua),
			StringComparison.Ordinal);
	}

	[Fact]
	public void Inspect_IncompleteOrOversizedContent_IsTooLarge()
	{
		AssertOpaque(CheatTableInspector.Inspect("table.ct", Encoding.UTF8.GetBytes(ValueRecords), false),
			CheatTableFormat.TooLarge);
	}

	[Fact]
	public void Inspect_XmlExtension_IsParsedWithoutTheMagicCheck()
	{
		CheatTableInspection inspection = Inspect("table.XML", "﻿" + ValueRecords.TrimStart());

		Assert.Equal(CheatTableFormat.Xml, inspection.Format);
		Assert.Equal(2, inspection.RecordCount);
	}

	[Theory]
	[InlineData("trainer.exe")]
	[InlineData("table.txt")]
	[InlineData("table")]
	[InlineData("table.ct.bak")]
	public void Inspect_ExtensionCheatEngineDoesNotLoad_IsInvalidArgument(string name)
	{
		CheatEngineToolException exception =
			Assert.Throws<CheatEngineToolException>(() => Inspect(name, ValueRecords));

		Assert.Equal((ToolErrorKind.InvalidArgument, ToolHostEffect.NotStarted),
			(exception.Error.Kind, exception.Error.HostEffect));
		Assert.Contains(".CETRAINER", exception.Error.Message, StringComparison.Ordinal);
	}

	[Fact]
	public void Enforce_OpaqueTableWithUnsafeLuaOff_NamesWhyItCannotBeInspected()
	{
		CheatTableInspection inspection = Inspect("table.ct", "not a table");
		McpFeatureGate gate = new(Options.Create(new McpFeatureOptions { EnableUnsafeLua = false }));

		CheatEngineToolException exception =
			Assert.Throws<CheatEngineToolException>(() => inspection.Requirements.Enforce(gate, "table_load"));

		Assert.Equal(ToolErrorKind.CapabilityDisabled, exception.Error.Kind);
		Assert.Equal(
			"table_load is disabled by the Mcp:EnableUnsafeLua setting: the table cannot be inspected because it is compressed or protected.",
			exception.Error.Message);
	}

	[Fact]
	public void Inspect_HeldFile_InspectsTheBytesThatStayPinned()
	{
		using McpFilePathsTests.Scratch scratch = new();
		string file = Path.Combine(scratch.CreateFolder("tables"), "game.CT");
		File.WriteAllText(file, Table("<LuaScript>print(1)</LuaScript>"));
		McpFilePaths paths = new(new McpFileOptions(), Path.Combine(scratch.Root, "registry"),
			Path.Combine(scratch.Root, "data"));

		using (HeldFile held = paths.OpenRead(file, "table_load", CheatTableInspector.MaximumTableBytes))
		{
			CheatTableInspection inspection = CheatTableInspector.Inspect(held);

			Assert.True(inspection.ContainsLua);
			Assert.Throws<IOException>(() => File.WriteAllText(file, ValueRecords));
		}

		using (HeldFile small = paths.OpenRead(file, "table_load", 16))
		{
			Assert.Equal(CheatTableFormat.TooLarge, CheatTableInspector.Inspect(small).Format);
		}
	}

	private static CheatTableInspection Inspect(string name, string content)
	{
		return CheatTableInspector.Inspect(name, Encoding.UTF8.GetBytes(content), true);
	}

	private static string Table(string body)
	{
		return
			$"<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<CheatTable CheatEngineTableVersion=\"46\">\n{body}\n</CheatTable>\n";
	}

	private static void AssertOpaque(CheatTableInspection inspection, CheatTableFormat format)
	{
		Assert.Equal(format, inspection.Format);
		Assert.False(inspection.IsInspected);
		McpFeature[] all = [McpFeature.UnsafeLua, McpFeature.AutoAssembler, McpFeature.TargetCodeExecution];
		Assert.Equal(all, inspection.Requirements.Features);
		Assert.True(inspection.ContainsLua && inspection.ContainsForms && inspection.UsesMono);
		Assert.Equal((0, 0), (inspection.RecordCount, inspection.AssemblerScriptCount));
		Assert.StartsWith("the table cannot be inspected because ",
			inspection.Requirements.ReasonFor(McpFeature.TargetCodeExecution), StringComparison.Ordinal);
	}
}
