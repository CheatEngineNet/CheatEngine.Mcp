using System.Buffers;
using System.Collections.Immutable;
using System.ComponentModel;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

using CheatEngine.Client;
using CheatEngine.Client.Processes;
using CheatEngine.Mcp.Core.Contract;
using CheatEngine.Mcp.Core.Files;
using CheatEngine.Mcp.Core.Values;
using CheatEngine.SDK.Engine.Inspection;

namespace CheatEngine.Mcp.Tools.Modules;

/// <summary>A module's stable PE build metadata, independent of its load address.</summary>
public sealed record ModuleBuildIdentity(
	[property: Description("The module image name, compared without case.")]
	string Name,
	[property: Description("The PE pointer width, 4 or 8 bytes.")]
	int PointerBytes,
	[property: Description("SHA-256 of normalized PE build metadata; excludes the ASLR image base and is not a code-integrity hash.")]
	string Fingerprint,
	[property: Description("The PE SizeOfImage in bytes.")]
	long ImageSize);

/// <summary>A historical module-relative observation that may be validated against a later attachment.</summary>
public sealed record ModuleRvaBinding(
	[property: Description("The observed module build identity.")]
	ModuleBuildIdentity Identity,
	[property: Description("The relative virtual address as uppercase hexadecimal, without 0x.")]
	string Rva);

/// <summary>The facts observed while resolving a build-bound relative address.</summary>
public sealed record ModuleRvaResolution(
	[property: Description("The matching currently loaded module build.")]
	ModuleBuildIdentity Module,
	[property: Description("The current process id at validation.")]
	int ProcessId,
	[property: Description("The target-selection epoch at validation.")]
	long SelectionEpoch,
	[property: Description("The current module base as uppercase hexadecimal.")]
	string BaseAddress,
	[property: Description("The validated relative virtual address as uppercase hexadecimal.")]
	string Rva,
	[property: Description("The resolved address as uppercase hexadecimal. This observation does not authorize execution.")]
	string Address,
	[property: Description("Whether the PE section declares code or execution; this is not an operating-system page-permission check.")]
	bool Executable,
	[property: Description("The containing PE section, or null for image headers or unmapped padding.")]
	string? Section = null);

/// <summary>Build identity and RVA validation shared by persisted notes and offline metadata.</summary>
internal static class ModuleBuildBindings
{
	private static readonly SearchValues<char> HexDigits = SearchValues.Create("0123456789ABCDEFabcdef");
	/// <summary>Captures a binding inside an existing Client dispatch, checking selection before and after reads.</summary>
	internal static ModuleRvaBinding Capture(ICheatEngineClient client, string module, string rva,
		CancellationToken token)
	{
		uint relative = ParseRva(rva);
		Observed observed = Observe(client, ModuleLocator.RequireModule(module), token);
		RequireRange(observed, relative);
		return new ModuleRvaBinding(observed.Identity, HexFormat.Address(relative));
	}

	/// <summary>Resolves only when the loaded module's name, width and build facts still match the stored binding.</summary>
	internal static ModuleRvaResolution Validate(ICheatEngineClient client, ModuleRvaBinding binding,
		CancellationToken token)
	{
		ValidateBinding(binding);
		uint relative = ParseRva(binding.Rva);
		Observed observed = Observe(client, binding.Identity.Name, token);
		if (!SameBuild(binding.Identity, observed.Identity))
		{
			throw CheatEngineToolException.InvalidState("The saved module binding does not match the loaded build.",
				"Attach the matching build or explicitly create a new binding; do not reuse the old address.");
		}

		RequireRange(observed, relative);
		PeSection? section = observed.Headers.FindSection(relative);
		return new ModuleRvaResolution(observed.Identity, observed.Process.Id.Value, observed.Process.SelectionEpoch,
			HexFormat.Address(observed.Base), HexFormat.Address(relative), HexFormat.Address(observed.Base + relative),
			section?.IsExecutable ?? false, section?.Name);
	}

	/// <summary>Reads the original module file's bounded PE headers without loading or executing that file.</summary>
	internal static ModuleBuildIdentity FromFile(HeldFile file, CancellationToken token)
	{
		byte[] bytes = new byte[(int) Math.Min(file.Length, PeImageReader.MaximumHeaderBytes)];
		int copied = 0;
		while (copied < bytes.Length)
		{
			token.ThrowIfCancellationRequested();
			int read = file.ReadAt(bytes.AsSpan(copied), copied);
			if (read == 0)
			{
				throw CheatEngineToolException.InvalidState("The pinned module file ended before its PE header was read.");
			}
			copied += read;
		}

		token.ThrowIfCancellationRequested();
		return FromHeaders(Path.GetFileName(file.FullPath), Parse(bytes));
	}

	/// <summary>Checks an untrusted persisted binding before any target access.</summary>
	internal static void ValidateBinding(ModuleRvaBinding binding)
	{
		if (binding?.Identity is not { } identity || string.IsNullOrWhiteSpace(identity.Name) ||
			identity.Name.Length > 256 || identity.Name.IndexOfAny(['/', '\\', ':']) >= 0 ||
			identity.Name.Any(char.IsControl) || identity.PointerBytes is not (4 or 8) ||
			identity.ImageSize is <= 0 or > uint.MaxValue || identity.Fingerprint is not { Length: 64 } ||
			!identity.Fingerprint.All(char.IsAsciiHexDigit))
		{
			throw CheatEngineToolException.InvalidArgument("binding", "must contain a valid PE module build identity.");
		}

		uint relative = ParseRva(binding.Rva);
		if (relative >= identity.ImageSize)
		{
			throw CheatEngineToolException.InvalidArgument("rva", "must lie inside the bound module image.");
		}
	}

	/// <summary>Normalizes only build facts that remain stable after Windows rebases the image.</summary>
	internal static ModuleBuildIdentity FromHeaders(string name, PeHeaders headers)
	{
		using MemoryStream bytes = new();
		using (BinaryWriter writer = new(bytes, Encoding.UTF8, true))
		{
			writer.Write("cemcp.pe-build.v1");
			writer.Write(headers.Machine);
			writer.Write(headers.IsPe32Plus);
			writer.Write(headers.TimeDateStamp);
			writer.Write(headers.Characteristics);
			writer.Write(headers.SizeOfImage);
			writer.Write(headers.SizeOfHeaders);
			writer.Write(headers.AddressOfEntryPoint);
			writer.Write(headers.Subsystem);
			writer.Write(headers.Sections.Length);
			foreach (PeSection section in headers.Sections)
			{
				writer.Write(section.Name);
				writer.Write(section.VirtualAddress);
				writer.Write(section.VirtualSize);
				writer.Write(section.PointerToRawData);
				writer.Write(section.SizeOfRawData);
				writer.Write(section.Characteristics);
			}
		}

		return new ModuleBuildIdentity(name, headers.IsPe32Plus ? 8 : 4,
			Convert.ToHexString(SHA256.HashData(bytes.GetBuffer().AsSpan(0, checked((int) bytes.Length)))),
			headers.SizeOfImage);
	}

	internal static bool SameBuild(ModuleBuildIdentity expected, ModuleBuildIdentity actual) =>
		string.Equals(expected.Name, actual.Name, StringComparison.OrdinalIgnoreCase) &&
		expected.PointerBytes == actual.PointerBytes && expected.ImageSize == actual.ImageSize &&
		string.Equals(expected.Fingerprint, actual.Fingerprint, StringComparison.OrdinalIgnoreCase);

	internal static uint ParseRva(string? text)
	{
		ReadOnlySpan<char> digits = text.AsSpan();
		if (digits.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
		{
			digits = digits[2..];
		}
		return digits.Length is 0 or > 8 || digits.ContainsAnyExcept(HexDigits) ||
			!uint.TryParse(digits, NumberStyles.AllowHexSpecifier,
				CultureInfo.InvariantCulture, out uint value)
			? throw CheatEngineToolException.InvalidArgument("rva", "must be 1 to 8 hexadecimal digits, optionally prefixed with 0x.")
			: value;
	}

	private static Observed Observe(ICheatEngineClient client, string selector, CancellationToken token)
	{
		token.ThrowIfCancellationRequested();
		ProcessSnapshot before = client.Processes.GetCurrentProcess(token);
		ModuleInfo module = ModuleLocator.Find(client, selector, out ImmutableArray<ModuleInfo> modules, token);
		if (modules.Where(candidate => string.Equals(candidate.Name, selector, StringComparison.OrdinalIgnoreCase))
			.Select(static candidate => candidate.BaseAddress.ToUInt64()).Distinct().Skip(1).Any())
		{
			throw CheatEngineToolException.InvalidState("More than one loaded module has the requested name.",
				"Choose the module by its base address when capturing a new binding.");
		}
		PeHeaders headers = Parse(ModuleLocator.ReadHeaderBytes(client, module, token));
		if ((module.ImageSize is { } size && size.Value != headers.SizeOfImage) ||
			module.Is64Bit != headers.IsPe32Plus)
		{
			throw CheatEngineToolException.InvalidState("The module enumeration and mapped PE header disagree.");
		}
		ProcessSnapshot after = client.Processes.GetCurrentProcess(token);
		token.ThrowIfCancellationRequested();
		return before.Id != after.Id || before.SelectionEpoch != after.SelectionEpoch
			? throw new CheatEngineToolException(new ToolError(ToolErrorKind.TargetChanged,
				"The selected target changed while its module binding was read.", null, ToolHostEffect.Completed, false))
			: new Observed(before, module.BaseAddress.ToUInt64(), FromHeaders(module.Name, headers), headers);
	}

	private static PeHeaders Parse(ReadOnlySpan<byte> bytes)
	{
		try
		{
			PeHeaders headers = PeImageReader.ParseHeaders(bytes);
			return headers.SizeOfImage == 0 ? throw new InvalidDataException("The image size is zero.") : headers;
		}
		catch (InvalidDataException exception)
		{
			throw CheatEngineToolException.InvalidState($"A complete supported PE header is required for a build binding: {exception.Message}");
		}
	}

	private static void RequireRange(Observed observed, uint relative)
	{
		ulong maximum = observed.Identity.PointerBytes == 4 ? uint.MaxValue : ulong.MaxValue;
		if (relative >= observed.Identity.ImageSize || observed.Base > maximum || relative > maximum - observed.Base)
		{
			throw CheatEngineToolException.InvalidArgument("rva", "lies outside the loaded image or target address width.");
		}
	}

	private sealed record Observed(ProcessSnapshot Process, ulong Base, ModuleBuildIdentity Identity, PeHeaders Headers);
}
