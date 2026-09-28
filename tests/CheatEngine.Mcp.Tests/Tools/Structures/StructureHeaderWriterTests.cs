using CheatEngine.Mcp.Tools.Structures;

namespace CheatEngine.Mcp.Tests.Tools.Structures;

/// <summary>The managed C generator: member placement, padding, identifiers, embedding order and comments.</summary>
public sealed class StructureHeaderWriterTests
{
	private const int VtByte = 0;
	private const int VtWord = 1;
	private const int VtDword = 2;
	private const int VtQword = 3;
	private const int VtSingle = 4;
	private const int VtDouble = 5;
	private const int VtWideString = 7;
	private const int VtBinary = 9;
	private const int VtPointer = 12;
	private const int VtCustom = 13;

	private const string Signed = "dtSignedInteger";
	private const string Unsigned = "dtUnsignedInteger";
	private const string Hexadecimal = "dtHexadecimal";

	[Fact]
	public void Write_RepresentativeStructures_PlacesEveryMemberAtItsOffset()
	{
		StructureLuaHeaderStructure[] structures =
		[
			new("Player data", 0x40,
			[
				new StructureLuaHeaderElement(8, VtDword, 4, "health", Signed),
				new StructureLuaHeaderElement(0, VtPointer, 8, "vtable"),
				new StructureLuaHeaderElement(0xC, VtSingle, 4, "max hp"),
				new StructureLuaHeaderElement(0x10, VtPointer, 8, "weapon", Child: "Weapon"),
				new StructureLuaHeaderElement(0x18, VtByte, 1, Display: Unsigned),
				new StructureLuaHeaderElement(0x18, VtBinary, 1, "flags", BitStart: 3, BitSize: 2),
				new StructureLuaHeaderElement(0x20, VtWideString, 0x10, "name"),
				new StructureLuaHeaderElement(0x30, VtPointer, 0xC, "origin", Child: "Vec3", Nested: true),
				new StructureLuaHeaderElement(-8, VtQword, 8, "header", Unsigned)
			]),
			new("Weapon", 0x10,
			[
				new StructureLuaHeaderElement(0, VtPointer, 8, "owner", Child: "Player data"),
				new StructureLuaHeaderElement(8, VtDword, 4, "int", Hexadecimal),
				new StructureLuaHeaderElement(0xC, VtCustom, 4, "int", CustomType: "BE \"u32\"")
			]),
			new("Vec3", 0xC,
			[
				new StructureLuaHeaderElement(0, VtSingle, 4, "x"),
				new StructureLuaHeaderElement(4, VtSingle, 4, "y"),
				new StructureLuaHeaderElement(8, VtSingle, 4, "z")
			]),
			new("Empty", 0, [])
		];

		string text = StructureHeaderWriter.Write(structures);

		Assert.Equal("""
		             // C declarations of 4 Cheat Engine structures, written by CheatEngine.Mcp.
		             // Offsets and sizes are hexadecimal. Packing is 1 byte and gaps are padding arrays, so each
		             // member sits at its Cheat Engine offset. Pointer members take the compiler's pointer size:
		             // compile for the target's bitness.
		             #pragma once

		             #include <stdint.h>

		             typedef struct Player_data Player_data;
		             typedef struct Weapon Weapon;
		             typedef struct Vec3 Vec3;
		             typedef struct Empty Empty;

		             #pragma pack(push, 1)

		             // "Vec3": 0xC bytes, 3 elements
		             struct Vec3
		             {
		             	float x; // 0x0 float
		             	float y; // 0x4 float
		             	float z; // 0x8 float
		             };
		             // static_assert(sizeof(Vec3) == 0xC, "Vec3");

		             // "Player data": 0x40 bytes, 9 elements
		             struct Player_data
		             {
		             	// -0x8 uint64 "header": before the structure's start, not declared
		             	void *vtable; // 0x0 pointer
		             	int32_t health; // 0x8 int32
		             	float max_hp; // 0xC float "max hp"
		             	Weapon *weapon; // 0x10 pointer to Weapon
		             	uint8_t field_18; // 0x18 uint8
		             	// 0x18 binary bits 3-4 "flags": overlaps the member before it, not declared
		             	uint8_t pad_19[0x7]; // 0x19 padding
		             	uint16_t name[0x8]; // 0x20 wstring
		             	Vec3 origin; // 0x30 nested Vec3
		             	uint8_t pad_3C[0x4]; // 0x3C padding
		             };
		             // static_assert(sizeof(Player_data) == 0x40, "Player_data");

		             // "Weapon": 0x10 bytes, 3 elements
		             struct Weapon
		             {
		             	Player_data *owner; // 0x0 pointer to Player_data
		             	uint32_t int_; // 0x8 uint32 "int"
		             	uint32_t int__2; // 0xC custom "BE ?u32?" "int"
		             };
		             // static_assert(sizeof(Weapon) == 0x10, "Weapon");

		             // "Empty": 0x0 bytes, 0 elements
		             struct Empty
		             {
		             	uint8_t unused; // C needs a member; the structure has none
		             };

		             #pragma pack(pop)

		             """.ReplaceLineEndings("\n"), text);
	}

	[Fact]
	public void Write_NestedCycleAndSizeMismatch_EmbedOnlyWhatFits()
	{
		StructureLuaHeaderStructure[] structures =
		[
			new("A", 8, [new StructureLuaHeaderElement(0, VtPointer, 8, "b", Child: "B", Nested: true)]),
			new("B", 8, [new StructureLuaHeaderElement(0, VtPointer, 8, "a", Child: "A", Nested: true)]),
			new("C", 6, [new StructureLuaHeaderElement(0, VtPointer, 6, "b", Child: "B", Nested: true)])
		];

		string text = StructureHeaderWriter.Write(structures);

		int b = text.IndexOf("struct B\n", StringComparison.Ordinal);
		int a = text.IndexOf("struct A\n", StringComparison.Ordinal);
		Assert.True(b >= 0 && a > b, "An embedded structure must be defined before the structure that embeds it.");
		Assert.Contains("\tuint8_t a[0x8]; // 0x0 nested A as bytes\n", text, StringComparison.Ordinal);
		Assert.Contains("\tB b; // 0x0 nested B\n", text, StringComparison.Ordinal);
		Assert.Contains("\tuint8_t b[0x6]; // 0x0 nested B as bytes\n", text, StringComparison.Ordinal);
	}

	[Fact]
	public void Write_NestedChildWhoseEarlierElementEndsPastItsSize_IsBytesSoLaterMembersKeepTheirOffsets()
	{
		// Cheat Engine's Size of U ends at b, its highest-offset element (2 + 2), while a reaches 8, so C's sizeof(U)
		// is 8: embedding it in 4 bytes would move x from 0x4 to 0x8.
		StructureLuaHeaderStructure[] structures =
		[
			new("P", 8,
			[
				new StructureLuaHeaderElement(0, VtPointer, 4, "u", Child: "U", Nested: true),
				new StructureLuaHeaderElement(4, VtDword, 4, "x")
			]),
			new("U", 4,
			[
				new StructureLuaHeaderElement(0, VtQword, 8, "a"),
				new StructureLuaHeaderElement(2, VtWord, 2, "b")
			])
		];

		string text = StructureHeaderWriter.Write(structures);

		Assert.Contains("struct P\n{\n\tuint8_t u[0x4]; // 0x0 nested U as bytes\n\tuint32_t x; // 0x4 uint32\n};\n" +
						"// static_assert(sizeof(P) == 0x8, \"P\");\n", text, StringComparison.Ordinal);
		Assert.Contains("struct U\n{\n\tuint64_t a; // 0x0 uint64\n" +
						"\t// 0x2 uint16 \"b\": overlaps the member before it, not declared\n};\n" +
						"// An element ends at 0x8, past Cheat Engine's size 0x4, which ends at the element with the " +
						"highest offset.\n// static_assert(sizeof(U) == 0x8, \"U\");\n", text,
			StringComparison.Ordinal);
	}

	[Fact]
	public void Write_NestedChildThatFitsItsSizeWithAnOverlap_IsStillEmbedded()
	{
		// b overlaps a but ends inside U's size, so sizeof(U) is still 8.
		StructureLuaHeaderStructure[] structures =
		[
			new("P", 0xC,
			[
				new StructureLuaHeaderElement(0, VtPointer, 8, "u", Child: "U", Nested: true),
				new StructureLuaHeaderElement(8, VtDword, 4, "x")
			]),
			new("U", 8,
			[
				new StructureLuaHeaderElement(0, VtDword, 4, "a"),
				new StructureLuaHeaderElement(2, VtWord, 2, "b"),
				new StructureLuaHeaderElement(4, VtDword, 4, "c")
			])
		];

		string text = StructureHeaderWriter.Write(structures);

		Assert.Contains("\tU u; // 0x0 nested U\n\tuint32_t x; // 0x8 uint32\n", text, StringComparison.Ordinal);
		Assert.Contains("// static_assert(sizeof(U) == 0x8, \"U\");\n", text, StringComparison.Ordinal);
		Assert.DoesNotContain("An element ends at", text, StringComparison.Ordinal);
	}

	[Theory]
	[InlineData(0x10L, "uint8_t *weapon; // 0x10 pointer to Weapon+0x10")]
	[InlineData(-8L, "uint8_t *weapon; // 0x10 pointer to Weapon-0x8")]
	[InlineData(0L, "Weapon *weapon; // 0x10 pointer to Weapon")]
	public void Write_PointerIntoItsChildStructure_IsABytePointerCommentedWithTheStart(long childStart,
		string declaration)
	{
		StructureLuaHeaderStructure[] structures =
		[
			new("Player", 0x18, [new StructureLuaHeaderElement(0x10, VtPointer, 8, "weapon", Child: "Weapon",
				ChildStart: childStart)]),
			new("Weapon", 0x20, [new StructureLuaHeaderElement(0x10, VtDword, 4, "ammo")])
		];

		string text = StructureHeaderWriter.Write(structures);

		Assert.Contains("\t" + declaration + "\n", text, StringComparison.Ordinal);
	}

	[Fact]
	public void Write_PointerWithAStartButNoCopiedChild_StaysAVoidPointer()
	{
		string text = StructureHeaderWriter.Write(
		[
			new StructureLuaHeaderStructure("Player", 8,
				[new StructureLuaHeaderElement(0, VtPointer, 8, "weapon", Child: "Missing", ChildStart: 0x10)])
		]);

		Assert.Contains("\tvoid *weapon; // 0x0 pointer\n", text, StringComparison.Ordinal);
	}

	[Theory]
	[InlineData(VtDword, Signed, 12, "int32_t v[0x3]")]
	[InlineData(VtDword, Unsigned, 6, "uint8_t v[0x6]")]
	[InlineData(VtDouble, null, 8, "double v")]
	[InlineData(VtWideString, null, 7, "uint8_t v[0x7]")]
	[InlineData(VtPointer, null, 4, "void *v")]
	[InlineData(VtPointer, null, 6, "uint8_t v[0x6]")]
	[InlineData(VtCustom, null, 2, "uint16_t v")]
	[InlineData(VtCustom, null, 3, "uint8_t v[0x3]")]
	[InlineData(10, null, 8, "uint64_t v")]
	[InlineData(8, null, 5, "uint8_t v[0x5]")]
	[InlineData(6, null, 5, "char v[0x5]")]
	public void Write_ElementTypeAndSize_PickTheDeclaration(int vartype, string? display, int byteSize,
		string declaration)
	{
		StructureLuaHeaderStructure structure =
			new("S", byteSize, [new StructureLuaHeaderElement(0, vartype, byteSize, "v", display)]);

		string text = StructureHeaderWriter.Write([structure]);

		Assert.Contains("\t" + declaration + "; // 0x0 ", text, StringComparison.Ordinal);
	}

	[Theory]
	[InlineData("health", "health")]
	[InlineData("9lives", "_9lives")]
	[InlineData("class", "class_")]
	[InlineData("uint8_t", "uint8_t_")]
	[InlineData("intmax_t", "intmax_t_")]
	[InlineData("uintmax_t", "uintmax_t_")]
	[InlineData("int_least32_t", "int_least32_t_")]
	[InlineData("uint_fast8_t", "uint_fast8_t_")]
	[InlineData("SIZE_MAX", "SIZE_MAX_")]
	[InlineData("INT32_MAX", "INT32_MAX_")]
	[InlineData("UINT8_MAX", "UINT8_MAX_")]
	[InlineData("INT_FAST16_MIN", "INT_FAST16_MIN_")]
	[InlineData("UINT_LEAST64_MAX", "UINT_LEAST64_MAX_")]
	[InlineData("INTPTR_MAX", "INTPTR_MAX_")]
	[InlineData("WCHAR_MAX", "WCHAR_MAX_")]
	[InlineData("UINT32_WIDTH", "UINT32_WIDTH_")]
	[InlineData("HP_MAX", "HP_MAX")]
	[InlineData("int_least7_t", "int_least7_t")]
	[InlineData("a-b c", "a_b_c")]
	[InlineData("\u00E9t\u00E9", "_t_")]
	[InlineData("", "fallback")]
	[InlineData(null, "fallback")]
	public void Identifier_CheatEngineName_BecomesAValidCIdentifier(string? name, string expected)
	{
		Assert.Equal(expected, StructureHeaderWriter.Identifier(name, "fallback"));
	}

	[Fact]
	public void Write_StructuresWhoseNamesSanitizeAlike_GetDistinctTypeNames()
	{
		string text = StructureHeaderWriter.Write(
		[
			new StructureLuaHeaderStructure("a b", 4, [new StructureLuaHeaderElement(0, VtDword, 4, "x", Signed)]),
			new StructureLuaHeaderStructure("a_b", 8,
				[new StructureLuaHeaderElement(0, VtPointer, 8, "other", Child: "a b")]),
			new StructureLuaHeaderStructure("", 4, [new StructureLuaHeaderElement(0, VtDword, 4, "y", Signed)])
		]);

		Assert.Contains("typedef struct a_b a_b;\ntypedef struct a_b_2 a_b_2;\ntypedef struct structure_2 structure_2;\n",
			text, StringComparison.Ordinal);
		Assert.Contains("\ta_b *other; // 0x0 pointer to a_b\n", text, StringComparison.Ordinal);
		Assert.Contains("// \"\": 0x4 bytes, 1 element\nstruct structure_2\n", text, StringComparison.Ordinal);
	}
}
