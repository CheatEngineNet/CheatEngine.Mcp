using System.Runtime.InteropServices;
using System.Text;
using System.Xml;

using CheatEngine.Mcp.Core.Contract;
using CheatEngine.Mcp.Core.Features;
using CheatEngine.Mcp.Core.Files;

namespace CheatEngine.Mcp.Core.Tables;

/// <summary>
///     Inspects a cheat table file before the Client loads it, and derives the exposure switches the load needs. It fails
///     closed: whatever it cannot read completely needs unsafe Lua, target code execution and Auto Assembler.
/// </summary>
/// <remarks>
///     <para>
///         Cheat Engine 7.7 picks the loader by extension (<c>OpenSave.pas</c> <c>LoadTable</c>): <c>.CT</c> is XML when
///         its first five bytes read <c>&lt;?XML</c> in any case, a pre-5.6 binary table when it starts with
///         <c>CHEATENGINE</c>, and otherwise a protected table that it decrypts and decompresses; <c>.CETRAINER</c> is
///         always protected and its Lua script always runs; <c>.XML</c> is parsed as XML. Any other extension is refused
///         here, as Cheat Engine would refuse it.
///     </para>
///     <para>
///         An inspected XML table needs: <see cref="McpFeature.UnsafeLua" /> for a <c>LuaScript</c> (Cheat Engine may run
///         it
///         on load) or <c>Forms</c> (their events call Lua); <see cref="McpFeature.TargetCodeExecution" /> for the
///         <c>UsesMono</c> option (see <see cref="MonoAutoAttachProbe" />); <see cref="McpFeature.AutoAssembler" /> and
///         the
///         <see cref="AutoAssemblerScriptClassifier" /> result of each <c>AssemblerScript</c>, since a record's script
///         runs
///         when the record is activated, and a table saved with its activation state reactivates it. Element and attribute
///         names are matched without case, anywhere in the document, which can only find more than Cheat Engine reads.
///     </para>
///     <para>
///         Opaque, needing all three switches: <c>.CETRAINER</c>, protected and legacy binary tables, a <c>CheatTable</c>
///         root marked <c>obfuscated</c> (Cheat Engine decodes its scripts with compiled code), an <c>AssemblerScript</c>
///         with an attribute other than <c>Async</c>, a document type declaration (entities could hide content), malformed
///         XML, a root other than <c>CheatTable</c>, and a file over the size limit.
///     </para>
///     <para>
///         Parsing uses <see cref="XmlReader" /> only: no DTD, no resolver, a character limit, no reflection. Inspect the
///         bytes of a <see cref="HeldFile" /> and keep it held until the load returns, so Cheat Engine opens the inspected
///         file.
///     </para>
/// </remarks>
public static class CheatTableInspector
{
	/// <summary>The largest table inspected; open the file with this limit.</summary>
	public const int MaximumTableBytes = 64 * 1024 * 1024;

	private const string OpaqueSubject = "the table cannot be inspected";

	/// <summary>Inspects a held table file.</summary>
	/// <param name="file">The file, opened with at most <see cref="MaximumTableBytes" /> bytes.</param>
	/// <returns>The inspection.</returns>
	/// <exception cref="CheatEngineToolException">
	///     <c>invalid_argument</c> when the extension is not one Cheat Engine loads (<c>.CT</c>, <c>.XML</c>,
	///     <c>.CETRAINER</c>).
	/// </exception>
	public static CheatTableInspection Inspect(HeldFile file)
	{
		ArgumentNullException.ThrowIfNull(file);
		return Inspect(file.FullPath, file.Content, file.IsComplete && file.Length <= MaximumTableBytes);
	}

	/// <summary>Inspects table bytes read from <paramref name="fileName" />.</summary>
	/// <param name="fileName">The table's file name or path; only its extension is used.</param>
	/// <param name="content">The file's bytes.</param>
	/// <param name="isComplete">Whether <paramref name="content" /> is the whole file.</param>
	/// <returns>The inspection.</returns>
	/// <exception cref="CheatEngineToolException">
	///     <c>invalid_argument</c> when the extension is not one Cheat Engine loads.
	/// </exception>
	public static CheatTableInspection Inspect(string fileName, ReadOnlyMemory<byte> content, bool isComplete)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
		string extension = Path.GetExtension(fileName);
		if (extension.Equals(".cetrainer", StringComparison.OrdinalIgnoreCase))
		{
			return Opaque(CheatTableFormat.Trainer,
				"it is a .CETRAINER, which is protected and whose Lua script Cheat Engine always runs");
		}

		bool isTable = extension.Equals(".ct", StringComparison.OrdinalIgnoreCase);
		if (!isTable && !extension.Equals(".xml", StringComparison.OrdinalIgnoreCase))
		{
			throw CheatEngineToolException.InvalidArgument("path",
				"Cheat Engine loads only .CT, .XML and .CETRAINER tables.");
		}

		if (!isComplete || content.Length > MaximumTableBytes)
		{
			return Opaque(CheatTableFormat.TooLarge,
				$"it is larger than {MaximumTableBytes / (1024 * 1024)} MiB, the inspection limit");
		}

		ReadOnlySpan<byte> bytes = content.Span;
		if (isTable && !StartsWithIgnoreCase(bytes, "<?xml"u8))
		{
			return StartsWithIgnoreCase(bytes, "CHEATENGINE"u8)
				? Opaque(CheatTableFormat.LegacyBinary, "it is a binary table from before Cheat Engine 5.6")
				: Opaque(CheatTableFormat.Protected, "it is compressed or protected");
		}

		return InspectXml(content);
	}

	private static CheatTableInspection InspectXml(ReadOnlyMemory<byte> content)
	{
		XmlReaderSettings settings = new()
		{
			DtdProcessing = DtdProcessing.Prohibit,
			XmlResolver = null,
			IgnoreComments = true,
			IgnoreProcessingInstructions = true,
			IgnoreWhitespace = true,
			MaxCharactersInDocument = MaximumTableBytes,
			CloseInput = true
		};
		McpFeatureRequirementsBuilder requirements = new();
		bool containsLua = false;
		bool containsForms = false;
		bool usesMono = false;
		int records = 0;
		int scripts = 0;
		bool root = true;
		try
		{
			using XmlReader reader = XmlReader.Create(OpenStream(content), settings);
			reader.MoveToContent();
			while (!reader.EOF)
			{
				if (reader.NodeType is not XmlNodeType.Element)
				{
					reader.Read();
					continue;
				}

				string name = reader.LocalName;
				if (root)
				{
					root = false;
					if (!Is(name, "CheatTable"))
					{
						return Opaque(CheatTableFormat.Unparseable, "its root element is not CheatTable");
					}

					if (HasAttribute(reader, "obfuscated"))
					{
						return Opaque(CheatTableFormat.Obfuscated,
							"it is obfuscated, and Cheat Engine decodes it with compiled code");
					}
				}

				usesMono |= SetsUsesMono(reader);
				if (Is(name, "LuaScript") || Is(name, "LuaScriptEntry"))
				{
					containsLua = true;
					requirements.Add(McpFeature.UnsafeLua,
						"the table has a Lua script, which Cheat Engine can run when it loads the table");
				}
				else if (Is(name, "Forms") && !reader.IsEmptyElement)
				{
					containsForms = true;
					requirements.Add(McpFeature.UnsafeLua, "the table has forms, whose events call Lua functions");
				}
				else if (Is(name, "CheatEntry"))
				{
					records++;
				}
				else if (Is(name, "AssemblerScript"))
				{
					if (HasAttributeOtherThan(reader, "Async"))
					{
						return Opaque(CheatTableFormat.Obfuscated,
							"an Auto Assembler script of the table carries an unknown encoding attribute");
					}

					scripts++;
					requirements.Add(McpFeature.AutoAssembler,
						"the table has Auto Assembler scripts, which run when their records are activated");
					AutoAssemblerScriptClassifier.AddTo(requirements, reader.ReadElementContentAsString(),
						"an Auto Assembler script of the table");
					continue;
				}
				else if (Is(name, "UsesMono"))
				{
					usesMono |= IsSet(reader.ReadElementContentAsString());
					continue;
				}

				reader.Read();
			}
		}
		catch (XmlException)
		{
			return Opaque(CheatTableFormat.Unparseable,
				"it is not well-formed XML within the limits, or it has a document type declaration");
		}

		if (root)
		{
			return Opaque(CheatTableFormat.Unparseable, "it has no CheatTable element");
		}

		if (usesMono)
		{
			requirements.Add(McpFeature.TargetCodeExecution,
				"the table sets UsesMono, so Cheat Engine injects its Mono data collector into an open process");
		}

		return new CheatTableInspection(CheatTableFormat.Xml, containsLua, containsForms, usesMono, records, scripts,
			requirements.Build());
	}

	private static CheatTableInspection Opaque(CheatTableFormat format, string why)
	{
		string reason = $"{OpaqueSubject} because {why}";
		McpFeatureRequirementsBuilder requirements = new();
		requirements.Add(McpFeature.UnsafeLua, reason);
		requirements.Add(McpFeature.AutoAssembler, reason);
		requirements.Add(McpFeature.TargetCodeExecution, reason);
		return new CheatTableInspection(format, true, true, true, 0, 0, requirements.Build());
	}

	private static MemoryStream OpenStream(ReadOnlyMemory<byte> content)
	{
		return MemoryMarshal.TryGetArray(content, out ArraySegment<byte> segment)
			? new MemoryStream(segment.Array!, segment.Offset, segment.Count, false)
			: new MemoryStream(content.ToArray(), false);
	}

	/// <summary>Whether any attribute of the current element named <c>UsesMono</c> is set.</summary>
	private static bool SetsUsesMono(XmlReader reader)
	{
		bool set = false;
		if (reader.MoveToFirstAttribute())
		{
			do
			{
				set |= Is(reader.LocalName, "UsesMono") && IsSet(reader.Value);
			} while (reader.MoveToNextAttribute());

			reader.MoveToElement();
		}

		return set;
	}

	private static bool HasAttribute(XmlReader reader, string name)
	{
		bool found = false;
		if (reader.MoveToFirstAttribute())
		{
			do
			{
				found |= Is(reader.LocalName, name);
			} while (reader.MoveToNextAttribute());

			reader.MoveToElement();
		}

		return found;
	}

	private static bool HasAttributeOtherThan(XmlReader reader, string name)
	{
		bool found = false;
		if (reader.MoveToFirstAttribute())
		{
			do
			{
				found |= !Is(reader.LocalName, name) && !Is(reader.Prefix, "xmlns") && !Is(reader.LocalName, "xmlns");
			} while (reader.MoveToNextAttribute());

			reader.MoveToElement();
		}

		return found;
	}

	/// <summary>
	///     A table option counts as set unless it reads <c>0</c> or <c>false</c>: Cheat Engine tests the option in Lua,
	///     where any value other than <c>nil</c> and <c>false</c> is true.
	/// </summary>
	private static bool IsSet(string value)
	{
		ReadOnlySpan<char> trimmed = value.AsSpan().Trim();
		return !trimmed.Equals("0", StringComparison.Ordinal) &&
		       !trimmed.Equals("false", StringComparison.OrdinalIgnoreCase);
	}

	private static bool Is(string actual, string expected)
	{
		return actual.Equals(expected, StringComparison.OrdinalIgnoreCase);
	}

	private static bool StartsWithIgnoreCase(ReadOnlySpan<byte> bytes, ReadOnlySpan<byte> prefix)
	{
		return bytes.Length >= prefix.Length && Ascii.EqualsIgnoreCase(bytes[..prefix.Length], prefix);
	}
}
