using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

using CheatEngine.Mcp.Core.Contract;
using CheatEngine.Mcp.Core.Files;
using CheatEngine.Mcp.Core.Values;
using CheatEngine.Mcp.Tools.Modules;

namespace CheatEngine.Mcp.Tools.Il2Cpp;

/// <summary>Owns the activation-local, immutable <c>dump_cs_v1</c> imports.</summary>
public sealed class Il2CppDumpStore
{
	internal const int MaximumDatasets = 4;
	internal const int MaximumBytes = 16 * 1024 * 1024;
	internal const int MaximumTypes = 20_000;
	internal const int MaximumMembers = 100_000;
	internal const int MaximumLineLength = 8192;
	private readonly Lock _gate = new();
	private readonly Dictionary<string, Dataset> _datasets = new(StringComparer.Ordinal);

	/// <summary>Imports one supported dump.cs file only after its complete parse succeeds.</summary>
	internal Il2CppDumpImport Import(HeldFile file, HeldFile? moduleFile, CancellationToken token)
	{
		ArgumentNullException.ThrowIfNull(file);
		if (!file.IsComplete || file.Length is <= 0 or > MaximumBytes)
		{
			throw CheatEngineToolException.LimitExceeded("path", $"must be a non-empty dump.cs file at most {MaximumBytes} bytes.");
		}

		byte[] bytes = file.Content.ToArray();
		string text;
		try
		{
			text = new UTF8Encoding(false, true).GetString(bytes);
		}
		catch (DecoderFallbackException) { throw CheatEngineToolException.InvalidArgument("path", "must be strict UTF-8 dump_cs_v1 text."); }
		ModuleBuildIdentity? module = moduleFile is null ? null : ModuleBuildBindings.FromFile(moduleFile, token);
		Parsed parsed = Il2CppDumpParser.Parse(text, token);
		string id = $"il2cpp_{Guid.NewGuid():N}";
		Dataset dataset = new(id, Path.GetFileName(file.FullPath), file.Length, Convert.ToHexString(SHA256.HashData(bytes)), module,
			parsed.Types, parsed.Members);
		lock (_gate)
		{
			token.ThrowIfCancellationRequested();
			if (_datasets.Count >= MaximumDatasets)
			{
				throw CheatEngineToolException.LimitExceeded("dataset", "at most four imports may be retained; delete one explicitly first.");
			}
			_datasets.Add(id, dataset);
		}
		return Summary(dataset);
	}

	/// <summary>Gets a type page from immutable data.</summary>
	internal Il2CppTypePage FindTypes(string datasetId, string? nameContains, int offset, int limit)
	{
		Dataset dataset = Require(datasetId);
		string? filter = OptionalFilter(nameContains, "nameContains");
		Il2CppType[] matches = [.. dataset.Types.Where(type => filter is null || type.Name.Contains(filter, StringComparison.OrdinalIgnoreCase))];
		PageSlice<Il2CppType> page = Paging.Slice(matches, offset, limit, 100);
		return new(dataset.Id, page.Total, [.. page.Items], page.NextOffset);
	}

	/// <summary>Gets a field or method page from immutable data.</summary>
	internal Il2CppMemberPage FindMembers(string datasetId, string kind, string? typeId, string? nameContains, int offset, int limit)
	{
		Dataset dataset = Require(datasetId);
		if (kind is not ("method" or "field"))
		{
			throw CheatEngineToolException.InvalidArgument("kind", "must be method or field.");
		}

		if (typeId is not null && !dataset.Types.Any(type => type.TypeId == typeId))
		{
			throw CheatEngineToolException.NotFound("typeId does not name a type in this dataset.");
		}

		string? filter = OptionalFilter(nameContains, "nameContains");
		Il2CppMember[] matches = [.. dataset.Members.Where(member => member.Kind == kind && (typeId is null || member.TypeId == typeId) && (filter is null || member.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)))];
		PageSlice<Il2CppMember> page = Paging.Slice(matches, offset, limit, 100);
		return new(dataset.Id, kind, page.Total, [.. page.Items], page.NextOffset);
	}

	/// <summary>Finds the imported binding for a method, refusing unavailable addresses and unbound datasets.</summary>
	internal ModuleRvaBinding ResolveBinding(string datasetId, string methodId)
	{
		Dataset dataset = Require(datasetId);
		Il2CppMember method = dataset.Members.FirstOrDefault(member => member.MemberId == methodId && member.Kind == "method")
			?? throw CheatEngineToolException.NotFound("methodId does not name a method in this dataset.");
		return dataset.Module is null
			? throw CheatEngineToolException.InvalidState("This dataset has no original module binding; re-import with modulePath before resolving a method.")
			: method.Rva is null
			? throw CheatEngineToolException.InvalidState("This method has no supported RVA in the imported dump.")
			: new(dataset.Module, method.Rva);
	}

	/// <summary>Explicitly releases a retained dataset.</summary>
	internal Il2CppDeleteResult Delete(string datasetId)
	{
		RequireId(datasetId);
		lock (_gate)
		{
			return new(datasetId, _datasets.Remove(datasetId));
		}
	}

	private Dataset Require(string datasetId)
	{
		RequireId(datasetId);
		lock (_gate)
		{
			return _datasets.TryGetValue(datasetId, out Dataset? dataset)
			? dataset : throw CheatEngineToolException.NotFound("datasetId does not name an active imported dump.");
		}
	}

	private static void RequireId(string? value)
	{
		if (string.IsNullOrWhiteSpace(value) || value.Length > 64 || value.Any(char.IsControl))
		{
			throw CheatEngineToolException.InvalidArgument("datasetId", "must be an opaque dataset id.");
		}
	}

	private static string? OptionalFilter(string? value, string parameter)
	{
		return value is null
			? null
			: string.IsNullOrWhiteSpace(value) || value.Length > 256 || value.Any(char.IsControl)
			? throw CheatEngineToolException.InvalidArgument(parameter, "must be 1 to 256 non-control characters when supplied.")
			: value;
	}

	private static Il2CppDumpImport Summary(Dataset dataset) => new(dataset.Id, "dump_cs_v1", 1, dataset.FileName, dataset.Length, dataset.Sha256, dataset.Types.Length, dataset.Members.Length, dataset.Module);
	private sealed record Dataset(string Id, string FileName, long Length, string Sha256, ModuleBuildIdentity? Module, ImmutableArray<Il2CppType> Types, ImmutableArray<Il2CppMember> Members);
}

internal sealed record Parsed(ImmutableArray<Il2CppType> Types, ImmutableArray<Il2CppMember> Members);

/// <summary>Strict parser for the documented, line-oriented dump_cs_v1 subset.</summary>
internal static class Il2CppDumpParser
{
	internal static Parsed Parse(string text, CancellationToken token)
	{
		List<ImageRange> images = [];
		List<Il2CppType> types = [];
		List<Il2CppMember> members = [];
		HashSet<string> typeIds = new(StringComparer.Ordinal);
		HashSet<string> memberIds = new(StringComparer.Ordinal);
		HashSet<string> imageNames = new(StringComparer.OrdinalIgnoreCase);
		string ns = string.Empty;
		string? section = null;
		string? pendingRva = null;
		Il2CppType? current = null;
		int ordinal = 0;
		bool awaitingTypeBrace = false;
		bool sawType = false;
		bool inBlockComment = false;
		using StringReader reader = new(text);
		int index = -1;
		while (reader.ReadLine() is { } raw)
		{
			index++;
			token.ThrowIfCancellationRequested();
			if (raw.Length > Il2CppDumpStore.MaximumLineLength)
			{
				Fail(index, "line exceeds 8192 characters.");
			}

			string line = (index == 0 ? raw.TrimStart('\uFEFF') : raw).Trim();
			if (line.Any(static c => char.IsControl(c) && c != '\t'))
			{
				Fail(index, "control characters are unsupported.");
			}

			if (inBlockComment || line.StartsWith("/*", StringComparison.Ordinal))
			{
				if (pendingRva is not null)
				{
					Fail(index, "a block comment cannot separate an RVA from its method.");
				}

				int end = line.IndexOf("*/", StringComparison.Ordinal);
				if (end >= 0 && !string.IsNullOrWhiteSpace(line[(end + 2)..]))
				{
					Fail(index, "block comments must occupy their own lines.");
				}

				inBlockComment = end < 0;
				continue;
			}
			if (line.StartsWith("// Image ", StringComparison.Ordinal))
			{
				if (current is not null || awaitingTypeBrace || sawType)
				{
					Fail(index, "all image headers must occur before type declarations.");
				}

				ImageRange image = ParseImage(line, index);
				if (images.Count >= 4096)
				{
					throw CheatEngineToolException.LimitExceeded("path", "exceeds 4096 image headers.");
				}

				if (image.Number != images.Count || !imageNames.Add(image.Name) || (images.Count > 0 && image.TypeDefIndexStart < images[^1].TypeDefIndexStart))
				{
					Fail(index, "image headers must be unique and ordered by Image and TypeDefIndexStart.");
				}

				images.Add(image);
				continue;
			}
			if (awaitingTypeBrace && line == "{")
			{
				awaitingTypeBrace = false;
				continue;
			}
			if (awaitingTypeBrace && line.Length > 0)
			{
				Fail(index, "a type declaration must be followed by an opening brace.");
			}

			if (line.StartsWith("// Namespace:", StringComparison.Ordinal))
			{
				if (current is not null)
				{
					Fail(index, "a namespace header cannot occur inside a type.");
				}

				ns = line[13..].Trim();
				continue;
			}
			if (line is "// Fields" or "// Properties" or "// Methods")
			{
				if (current is null || awaitingTypeBrace)
				{
					Fail(index, "a member section requires an open type.");
				}

				if (pendingRva is not null)
				{
					Fail(index, "an RVA record was not consumed by a method.");
				}

				section = line[3..].ToLowerInvariant();
				continue;
			}
			if (line.StartsWith("// RVA:", StringComparison.Ordinal))
			{
				if (current is null || section != "methods" || pendingRva is not null)
				{
					Fail(index, "RVA records must occur once immediately before a method.");
				}

				pendingRva = ParseRva(line, index);
				continue;
			}
			if (line.StartsWith('[') && !line.EndsWith(']'))
			{
				Fail(index, "attribute lines must be complete.");
			}

			if (line.Length == 0 || line.StartsWith('[') || line.StartsWith("//", StringComparison.Ordinal))
			{
				continue;
			}

			if (TryType(line, images, ns, out Il2CppType? type))
			{
				if (current is not null)
				{
					Fail(index, "a type began before the prior type closed.");
				}

				if (!typeIds.Add(type.TypeId))
				{
					Fail(index, "duplicate TypeDefIndex in the same image.");
				}

				if (types.Count > 0 && type.TypeDefIndex < types[^1].TypeDefIndex)
				{
					Fail(index, "TypeDefIndex declarations must be ordered.");
				}

				if (types.Count == Il2CppDumpStore.MaximumTypes)
				{
					throw CheatEngineToolException.LimitExceeded("path", "exceeds 20000 types.");
				}

				sawType = true;
				current = type;
				types.Add(type);
				ordinal = 0;
				section = null;
				pendingRva = null;
				awaitingTypeBrace = true;
				continue;
			}
			if (line == "}")
			{
				if (current is null || awaitingTypeBrace || pendingRva is not null)
				{
					Fail(index, "an unmatched closing brace or dangling RVA was found.");
				}

				current = null;
				section = null;
				continue;
			}
			if (current is null)
			{
				Fail(index, "not a supported dump_cs_v1 type declaration.");
			}

			if (section == "properties")
			{
				continue;
			}

			Il2CppMember? member = section == "fields" ? TryField(line, current, ordinal, out Il2CppMember? field) ? field : null : section == "methods" ? TryMethod(line, current, ordinal, pendingRva, out Il2CppMember? method) ? method : null : null;
			if (member is null)
			{
				Fail(index, "unsupported declaration in a supported member section.");
			}

			if (section == "methods" && pendingRva is null)
			{
				Fail(index, "a method declaration must immediately follow an RVA comment.");
			}

			if (members.Count == Il2CppDumpStore.MaximumMembers)
			{
				throw CheatEngineToolException.LimitExceeded("path", "exceeds 100000 members.");
			}

			if (!memberIds.Add(member.MemberId))
			{
				Fail(index, "duplicate member id.");
			}

			members.Add(member);
			ordinal++;
			pendingRva = null;
		}
		return images.Count == 0 || types.Count == 0
			? throw CheatEngineToolException.InvalidArgument("path", "is not a supported dump_cs_v1 file with image and TypeDefIndex declarations.")
			: current is not null || awaitingTypeBrace || pendingRva is not null || inBlockComment
			? throw CheatEngineToolException.InvalidArgument("path", "dump_cs_v1 ended with an unclosed type, comment or dangling RVA.")
			: new([.. types], [.. members]);
	}

	private static ImageRange ParseImage(string line, int index)
	{
		int colon = line.IndexOf(':');
		int dash = line.LastIndexOf(" - ", StringComparison.Ordinal);
		if (colon <= 9 || dash <= colon + 1 || dash + 3 >= line.Length)
		{
			Fail(index, "image line must be '// Image N: name - TypeDefIndexStart'.");
		}

		ReadOnlySpan<char> numberText = line.AsSpan(9, colon - 9);
		ReadOnlySpan<char> startText = line.AsSpan(dash + 3);
		if (!TryDecimal(numberText, out int number) || !TryDecimal(startText, out int start))
		{
			Fail(index, "image number and TypeDefIndexStart must be non-negative ASCII decimal integers.");
			return null!;
		}

		string image = line[(colon + 1)..dash].Trim();
		if (image.Length == 0 || image.Length > 256 || image.Any(char.IsControl))
		{
			Fail(index, "image name is invalid.");
		}

		return new(number, image, start);
	}

	private static string ParseRva(string line, int index)
	{
		string[] tokens = line[7..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
		if (tokens.Length == 0)
		{
			Fail(index, "RVA is missing.");
		}

		string value = tokens[0];
		if (value == "-1")
		{
			return "-";
		}

		if (!value.StartsWith("0x", StringComparison.OrdinalIgnoreCase) || value.Length is < 3 or > 10 || !value[2..].All(char.IsAsciiHexDigit) || !uint.TryParse(value[2..], NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out uint rva))
		{
			Fail(index, "RVA must be hexadecimal or -1.");
			return string.Empty;
		}
		return HexFormat.Address(rva);
	}

	private static bool TryType(string line, IReadOnlyList<ImageRange> images, string ns, [NotNullWhen(true)] out Il2CppType? type)
	{
		type = null;
		int marker = line.IndexOf("// TypeDefIndex:", StringComparison.Ordinal);
		if (marker < 0 || images.Count == 0)
		{
			return false;
		}

		string head = line[..marker].Trim();
		string suffix = line[(marker + "// TypeDefIndex:".Length)..].Trim();
		if (!suffix.All(char.IsAsciiDigit) || !int.TryParse(suffix, NumberStyles.None, CultureInfo.InvariantCulture, out int index))
		{
			return false;
		}

		string[] keys = [" class ", " struct ", " interface ", " enum "];
		string? key = keys.FirstOrDefault(head.Contains);
		if (key is null)
		{
			return false;
		}

		string name = head[(head.IndexOf(key, StringComparison.Ordinal) + key.Length)..].Trim();
		int inheritance = name.IndexOf(" : ", StringComparison.Ordinal);
		if (inheritance >= 0)
		{
			name = name[..inheritance].Trim();
		}

		if (name.Length == 0)
		{
			return false;
		}

		ImageRange? range = images.LastOrDefault(image => image.TypeDefIndexStart <= index);
		if (range is null)
		{
			return false;
		}

		string kind = key.Trim();
		string id = $"{range.Name}:{index.ToString(CultureInfo.InvariantCulture)}";
		type = new(id, range.Name, ns, name, kind, index);
		return true;
	}

	private static bool TryField(string line, Il2CppType type, int ordinal, [NotNullWhen(true)] out Il2CppMember? member)
	{
		member = null;
		int comment = CommentStart(line);
		if (comment == -2)
		{
			return false;
		}

		string declaration = (comment < 0 ? line : line[..comment]).Trim();
		if (!declaration.EndsWith(';'))
		{
			return false;
		}

		declaration = declaration[..^1].TrimEnd();
		string namedDeclaration = declaration.Split('=', 2)[0].TrimEnd();
		int nameStart = namedDeclaration.Length;
		while (nameStart > 0 && !char.IsWhiteSpace(namedDeclaration[nameStart - 1]))
		{
			nameStart--;
		}
		if (nameStart == 0 || nameStart == namedDeclaration.Length)
		{
			return false;
		}

		string name = namedDeclaration[nameStart..];
		if (name.IndexOfAny(['(', ')', ';', '{', '}']) >= 0)
		{
			return false;
		}

		string[] modifiers = namedDeclaration[..nameStart].Split((char[]?) null, StringSplitOptions.RemoveEmptyEntries);
		bool staticOrConst = Array.Exists(modifiers, static token => token is "static" or "const");
		string? offset = null;
		if (!staticOrConst && comment >= 0)
		{
			string v = line[(comment + 2)..].Trim();
			if (!v.StartsWith("0x", StringComparison.OrdinalIgnoreCase) || v.Length is < 3 or > 10 ||
				!v[2..].All(char.IsAsciiHexDigit) || !uint.TryParse(v[2..], NumberStyles.AllowHexSpecifier,
					CultureInfo.InvariantCulture, out uint parsed))
			{
				return false;
			}

			offset = HexFormat.Address(parsed);
		}
		if (!staticOrConst && offset is null)
		{
			return false;
		}

		member = new($"{type.TypeId}:field:{ordinal}", type.TypeId, "field", name, declaration, offset, null);
		return true;
	}

	private static bool TryMethod(string line, Il2CppType type, int ordinal, string? rva, [NotNullWhen(true)] out Il2CppMember? member)
	{
		member = null;
		int open = line.IndexOf('(');
		if (open <= 0)
		{
			return false;
		}

		int close = MatchingParameterClose(line, open);
		if (close < 0)
		{
			return false;
		}

		string tail = line[(close + 1)..].Trim();
		if (tail != ";" && !(tail.StartsWith('{') && tail.EndsWith('}') && string.IsNullOrWhiteSpace(tail[1..^1])))
		{
			return false;
		}

		string prefix = line[..open].Trim();
		int depth = 0;
		int nameStart = -1;
		for (int i = prefix.Length - 1; i >= 0; i--)
		{
			if (prefix[i] == '>')
			{
				depth++;
			}
			else if (prefix[i] == '<')
			{
				depth--;
			}
			else if (char.IsWhiteSpace(prefix[i]) && depth == 0)
			{
				nameStart = i + 1;
				break;
			}
			if (depth < 0)
			{
				return false;
			}
		}
		if (depth != 0 || nameStart <= 0 || nameStart >= prefix.Length)
		{
			return false;
		}

		string name = prefix[nameStart..];
		string signature = line[..(close + 1)].Trim();
		member = new($"{type.TypeId}:method:{ordinal}", type.TypeId, "method", name, signature, null, rva == "-" ? null : rva);
		return true;
	}

	private static bool TryDecimal(ReadOnlySpan<char> text, out int value)
	{
		value = 0;
		foreach (char digit in text)
		{
			if (!char.IsAsciiDigit(digit))
			{
				return false;
			}
		}
		return int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value);
	}

	private static int CommentStart(string line)
	{
		char quote = '\0';
		for (int i = 0; i < line.Length; i++)
		{
			char c = line[i];
			if (quote != '\0')
			{
				if (c == '\\')
				{
					i++;
				}
				else if (c == quote)
				{
					quote = '\0';
				}
			}
			else if (c is '\'' or '"')
			{
				quote = c;
			}
			else if (c == '/' && i + 1 < line.Length && line[i + 1] == '/')
			{
				return i;
			}
		}
		return quote == '\0' ? -1 : -2;
	}

	private static int MatchingParameterClose(string line, int open)
	{
		int depth = 0;
		char quote = '\0';
		for (int i = open; i < line.Length; i++)
		{
			char c = line[i];
			if (quote != '\0')
			{
				if (c == '\\')
				{
					i++;
				}
				else if (c == quote)
				{
					quote = '\0';
				}
			}
			else if (c is '\'' or '"')
			{
				quote = c;
			}
			else if (c == '(')
			{
				depth++;
			}
			else if (c == ')' && --depth == 0)
			{
				return i;
			}
			else if (c is '{' or '}' or ';')
			{
				return -1;
			}
		}
		return -1;
	}

	[DoesNotReturn]
	private static void Fail(int line, string message) => throw CheatEngineToolException.InvalidArgument("path", $"dump_cs_v1 line {line + 1}: {message}");

	private sealed record ImageRange(int Number, string Name, int TypeDefIndexStart);
}
