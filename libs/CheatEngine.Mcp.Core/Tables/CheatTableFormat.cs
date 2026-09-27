namespace CheatEngine.Mcp.Core.Tables;

/// <summary>How <see cref="CheatTableInspector" /> could read a table file.</summary>
/// <remarks>Every format other than <see cref="Xml" /> is opaque: its content could not be inspected.</remarks>
public enum CheatTableFormat
{
	/// <summary>A plain XML table, inspected completely.</summary>
	Xml,

	/// <summary>A <c>.CETRAINER</c> file: protected, and Cheat Engine always runs its Lua script.</summary>
	Trainer,

	/// <summary>A <c>.CT</c> file that is not XML, which Cheat Engine decrypts and decompresses as a protected table.</summary>
	Protected,

	/// <summary>A binary table from before Cheat Engine 5.6 (<c>CHEATENGINE</c> header), which Cheat Engine converts.</summary>
	LegacyBinary,

	/// <summary>An obfuscated table, whose scripts Cheat Engine decodes with compiled code while it loads them.</summary>
	Obfuscated,

	/// <summary>A table that could not be parsed: malformed XML, a document type declaration, or no <c>CheatTable</c> root.</summary>
	Unparseable,

	/// <summary>A table longer than the inspection limit.</summary>
	TooLarge
}
