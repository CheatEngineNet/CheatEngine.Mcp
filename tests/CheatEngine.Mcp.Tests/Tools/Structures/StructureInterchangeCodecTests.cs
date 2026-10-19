using System.Text;

using CheatEngine.Mcp.Tools.Structures;

namespace CheatEngine.Mcp.Tests.Tools.Structures;

/// <summary>Independent strict-json regression coverage for the version-one structure interchange codec.</summary>
public sealed class StructureInterchangeCodecTests
{
	[Fact]
	public void SaveLoad_IndependentFixture_PreservesRootsLayoutOrderAndSameOffsetFields()
	{
		StructureInterchangeDocument source = new(1, 8, ["Root"],
		[new("Root", false, [new("0", "first", "UInt32", "Hex", null, null, null, null, null, null), new("0", "second", "Pointer", null, null, "Child", "4", null, null, null)]), new("Child", true, [])]);
		using MemoryStream json = new();
		StructureInterchangeCodec.Save(json, source, TestContext.Current.CancellationToken);
		json.Position = 0;
		StructureInterchangeDocument roundTrip = StructureInterchangeCodec.Load(json, TestContext.Current.CancellationToken);
		Assert.Equal((1, 8, "Root", 2), (roundTrip.Version, roundTrip.PointerWidth, Assert.Single(roundTrip.Roots), roundTrip.Layouts.Length));
		Assert.Equal(["first", "second"], roundTrip.Layouts[0].Fields.Select(field => field.Name));
		Assert.Equal(["0", "0"], roundTrip.Layouts[0].Fields.Select(field => field.Offset));
		Assert.Equal(("Child", "4"), (roundTrip.Layouts[0].Fields[1].ChildStructure, roundTrip.Layouts[0].Fields[1].ChildStructureStart));
	}

	[Theory]
	[InlineData("""{"version":1,"pointerWidth":8,"roots":["A"],"layouts":[{"name":"A","internal":false,"fields":[]}],"extra":true}""")]
	[InlineData("""{"version":1,"pointerWidth":8,"roots":["A"],"layouts":[{"name":"A","name":"B","internal":false,"fields":[]}]}""")]
	[InlineData("""{"version":1,"pointerWidth":8,"roots":["A"],"layouts":[{"name":"A","internal":false,"fields":[{"offset":"0","name":"p","valueType":"Pointer","display":null,"byteSize":null,"childStructure":"Missing","childStructureStart":null,"customType":null,"bitStart":null,"bitSize":null}]}]}""")]
	public void Load_UnknownDuplicateAndMissingChild_AreRefused(string content)
	{
		using MemoryStream json = new(Encoding.UTF8.GetBytes(content));
		Assert.Throws<InvalidDataException>(() => StructureInterchangeCodec.Load(json, TestContext.Current.CancellationToken));
	}

	[Theory]
	[InlineData("""{"version":"1","pointerWidth":8,"roots":["A"],"layouts":[{"name":"A","internal":false,"fields":[]}]}""")]
	[InlineData("""{"version":1,"pointerWidth":null,"roots":["A"],"layouts":[{"name":"A","internal":false,"fields":[]}]}""")]
	[InlineData("""{"version":1,"pointerWidth":8,"roots":["A"],"layouts":[{"name":7,"internal":false,"fields":[]}]}""")]
	[InlineData("""{"version":1,"pointerWidth":8,"roots":["A"],"layouts":[{"name":"A","internal":false,"fields":[{"offset":"0","name":"x","valueType":"UInt32","display":false,"byteSize":null,"childStructure":null,"childStructureStart":null,"customType":null,"bitStart":null,"bitSize":null}]}]}""")]
	[InlineData("""{"version":1,"pointerWidth":8,"roots":"A","layouts":[{"name":"A","internal":false,"fields":[]}]}""")]
	public void Load_WrongRequiredAndOptionalJsonShapes_AreInvalidData(string content)
	{
		using MemoryStream json = new(Encoding.UTF8.GetBytes(content));
		Assert.Throws<InvalidDataException>(() => StructureInterchangeCodec.Load(json, TestContext.Current.CancellationToken));
	}

	[Fact]
	public void Load_TooManyRoots_RejectsBeforeMaterializingAllItems()
	{
		string roots = string.Join(',', Enumerable.Range(0, StructureInterchangeCodec.MaximumLayouts + 1)
			.Select(static index => $"\"R{index}\""));
		using MemoryStream json = new(Encoding.UTF8.GetBytes($"{{\"version\":1,\"pointerWidth\":8,\"roots\":[{roots}],\"layouts\":[]}}"));
		Assert.Throws<InvalidDataException>(() => StructureInterchangeCodec.Load(json, TestContext.Current.CancellationToken));
	}

	[Fact]
	public void Validate_CustomOrBinaryMetadata_IsRefusedRatherThanDropped()
	{
		StructureInterchangeDocument document = new(1, 4, ["A"], [new("A", false, [new("0", "flags", "UInt32", null, null, null, null, null, 0, 1)])]);
		InvalidDataException failure = Assert.Throws<InvalidDataException>(() => StructureInterchangeCodec.Validate(document, TestContext.Current.CancellationToken));
		Assert.Contains("not representable", failure.Message, StringComparison.Ordinal);
	}
}
