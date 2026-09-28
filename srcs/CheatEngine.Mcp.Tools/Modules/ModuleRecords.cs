using System.ComponentModel;
using System.Text.Json.Serialization;

using CheatEngine.Mcp.Core.Contract;

namespace CheatEngine.Mcp.Tools.Modules;

/// <summary>One page of the target's modules.</summary>
/// <param name="ProcessId">The process whose modules are listed.</param>
/// <param name="Total">The number of modules that match the filter.</param>
/// <param name="NextOffset">The offset of the next page; omitted on the last page.</param>
/// <param name="Modules">The modules of this page.</param>
public sealed record ModuleList(
	[property: Description("The process whose modules are listed.")]
	int ProcessId,
	[property: Description("The number of modules that match nameContains.")]
	int Total,
	[property: Description("The modules of this page, in Cheat Engine's order.")]
	ModuleEntry[] Modules,
	[property: Description("The offset of the next page; omitted on the last page.")]
	int? NextOffset = null);

/// <summary>One module of <see cref="ModuleList" />.</summary>
/// <param name="Name">The module name.</param>
/// <param name="Base">The base address.</param>
/// <param name="Size">The mapped size in bytes, when Cheat Engine reports it.</param>
/// <param name="Is64Bit">Whether the module is 64-bit; only in the detailed format.</param>
/// <param name="Path">The module's file; only in the detailed format.</param>
public sealed record ModuleEntry(
	[property: Description("The module name, such as game.exe.")]
	string Name,
	[property: Description("The base address as uppercase hexadecimal without 0x.")]
	string Base,
	[property: Description("The mapped size in bytes, when Cheat Engine reports it.")]
	long? Size = null,
	[property: Description("Whether the module is 64-bit; only with format detailed.")]
	bool? Is64Bit = null,
	[property: Description("The module's file on disk; only with format detailed.")]
	string? Path = null);

/// <summary>One module with its sections and PE header fields.</summary>
/// <param name="Name">The module name.</param>
/// <param name="Base">The base address.</param>
/// <param name="Size">The mapped size, when known.</param>
/// <param name="Path">The module's file.</param>
/// <param name="Is64Bit">Whether the module is 64-bit.</param>
/// <param name="Sections">The sections Cheat Engine reports.</param>
/// <param name="Pe">The PE header fields, omitted when the mapped header cannot be read or parsed.</param>
public sealed record ModuleDetails(
	[property: Description("The module name.")]
	string Name,
	[property: Description("The base address as uppercase hexadecimal without 0x.")]
	string Base,
	[property: Description("The module's file on disk, as Cheat Engine reports it.")]
	string Path,
	[property: Description("Whether the module is 64-bit.")]
	bool Is64Bit,
	[property: Description("The module's sections, as Cheat Engine enumerates them.")]
	ModuleSection[] Sections,
	[property: Description("The mapped size in bytes, when Cheat Engine reports it.")]
	long? Size = null,
	[property: Description("The PE header fields; omitted when the mapped header cannot be read or parsed.")]
	PeImageInfo? Pe = null);

/// <summary>One section of <see cref="ModuleDetails" />.</summary>
/// <param name="Name">The section name.</param>
/// <param name="Address">The section's address.</param>
/// <param name="Size">The section's size.</param>
/// <param name="FileOffset">The section's offset in the module file.</param>
/// <param name="Executable">Whether the PE header maps the section executable.</param>
/// <param name="Writable">Whether the PE header maps the section writable.</param>
public sealed record ModuleSection(
	[property: Description("The section name, such as .text.")]
	string Name,
	[property: Description("The section address as uppercase hexadecimal without 0x.")]
	string Address,
	[property: Description("The section size in bytes.")]
	long Size,
	[property: Description("The section's offset in the module file, as uppercase hexadecimal.")]
	string FileOffset,
	[property: Description("Whether the PE header marks the section as code or executable; omitted when unknown.")]
	bool? Executable = null,
	[property: Description("Whether the PE header marks the section writable; omitted when unknown.")]
	bool? Writable = null);

/// <summary>The PE header fields of a mapped module.</summary>
/// <param name="Machine">The CPU the image targets.</param>
/// <param name="TimeDateStamp">The raw COFF time stamp.</param>
/// <param name="TimeDateStampUtc">The time stamp read as a UTC time.</param>
/// <param name="EntryPoint">The entry point, when there is one.</param>
/// <param name="HeaderImageBase">The ImageBase field of the mapped header.</param>
/// <param name="Subsystem">The subsystem.</param>
/// <param name="IsDll">Whether the image is a DLL.</param>
/// <param name="Managed">Whether the image is a .NET assembly.</param>
/// <param name="Pdb">The PDB the image names, when it names one.</param>
public sealed record PeImageInfo(
	[property: Description("The CPU the image targets: x86, x64, arm, arm64 or unknown.")]
	PeMachine Machine,
	[property: Description(
		"The raw COFF time stamp as uppercase hexadecimal; it identifies a build, and reproducible builds store a hash in it.")]
	string TimeDateStamp,
	[property: Description(
		"The ImageBase field of the mapped header; Windows usually rewrites it to the load address, so it is not the file's preferred base.")]
	string HeaderImageBase,
	[property: Description("The subsystem, such as windows_gui or windows_cui.")]
	PeSubsystem Subsystem,
	[property: Description("Whether the image is a DLL.")]
	bool IsDll,
	[property: Description("Whether the image has a CLR header, which marks a .NET assembly.")]
	bool Managed,
	[property: Description("The time stamp read as seconds since 1970 in UTC; omitted when it is zero.")]
	DateTimeOffset? TimeDateStampUtc = null,
	[property: Description("The entry point address as uppercase hexadecimal; omitted when the image has none.")]
	string? EntryPoint = null,
	[property: Description("The PDB the image names in its CodeView record; omitted when it names none.")]
	PdbReference? Pdb = null);

/// <summary>The PDB named by an image's CodeView record.</summary>
/// <param name="Path">The PDB path recorded at link time.</param>
/// <param name="Signature">The PDB signature GUID.</param>
/// <param name="Age">The PDB age.</param>
public sealed record PdbReference(
	[property: Description("The PDB path recorded at link time.")]
	string Path,
	[property: Description("The PDB signature GUID, uppercase with dashes.")]
	string Signature,
	[property: Description("The PDB age; a symbol server keys the PDB by GUID and age.")]
	long Age);

/// <summary>The CPU a PE image targets; the wire value is the <c>snake_case</c> member name.</summary>
[JsonConverter(typeof(ContractEnumConverter<PeMachine>))]
public enum PeMachine
{
	/// <summary>A machine type these tools do not name.</summary>
	Unknown,

	/// <summary>32-bit x86 (IMAGE_FILE_MACHINE_I386).</summary>
	X86,

	/// <summary>x64 (IMAGE_FILE_MACHINE_AMD64).</summary>
	X64,

	/// <summary>32-bit ARM Thumb-2 (IMAGE_FILE_MACHINE_ARMNT).</summary>
	Arm,

	/// <summary>ARM64 (IMAGE_FILE_MACHINE_ARM64).</summary>
	Arm64
}

/// <summary>The subsystem of a PE image; the wire value is the <c>snake_case</c> member name.</summary>
[JsonConverter(typeof(ContractEnumConverter<PeSubsystem>))]
public enum PeSubsystem
{
	/// <summary>A subsystem these tools do not name.</summary>
	Unknown,

	/// <summary>A driver or native system process.</summary>
	Native,

	/// <summary>A Windows GUI application.</summary>
	WindowsGui,

	/// <summary>A Windows console application.</summary>
	WindowsCui,

	/// <summary>An EFI application or driver.</summary>
	Efi,

	/// <summary>A Windows boot application.</summary>
	WindowsBootApplication
}

/// <summary>One page of a module's exports.</summary>
/// <param name="Module">The module name.</param>
/// <param name="Total">The number of exports that match the filter.</param>
/// <param name="NextOffset">The offset of the next page; omitted on the last page.</param>
/// <param name="Exports">The exports of this page.</param>
public sealed record ExportList(
	[property: Description("The module name.")]
	string Module,
	[property: Description("The number of exports that match nameContains.")]
	int Total,
	[property: Description("The exports of this page, by ordinal.")]
	ModuleExport[] Exports,
	[property: Description("The offset of the next page; omitted on the last page.")]
	int? NextOffset = null);

/// <summary>One export of <see cref="ExportList" />.</summary>
/// <param name="Name">The export name, when it has one.</param>
/// <param name="Ordinal">The export ordinal.</param>
/// <param name="Address">The function address; omitted for a forwarder.</param>
/// <param name="Forwarder">The forwarder, when the export forwards to another module.</param>
public sealed record ModuleExport(
	[property: Description("The export ordinal.")]
	long Ordinal,
	[property: Description("The export name; omitted for an ordinal-only export.")]
	string? Name = null,
	[property: Description("The function address as uppercase hexadecimal; omitted for a forwarder.")]
	string? Address = null,
	[property: Description("The forwarder, such as NTDLL.RtlAllocateHeap, when the export forwards to another module.")]
	string? Forwarder = null);

/// <summary>One page of a module's imported functions.</summary>
/// <param name="Module">The module name.</param>
/// <param name="Total">The number of imports that match the filters.</param>
/// <param name="Imports">The imports of this page.</param>
/// <param name="NextOffset">The offset of the next page; omitted on the last page.</param>
public sealed record ImportList(
	[property: Description("The module name.")]
	string Module,
	[property: Description("The number of imports that match dllContains and nameContains.")]
	int Total,
	[property: Description(
		"The imports of this page: the import directory in descriptor and slot order, then the delay-load directory.")]
	ModuleImport[] Imports,
	[property: Description("The offset of the next page; omitted on the last page.")]
	int? NextOffset = null);

/// <summary>One imported function of <see cref="ImportList" />.</summary>
/// <param name="Dll">The DLL the import descriptor names.</param>
/// <param name="SlotAddress">The address of the function's import address table slot.</param>
/// <param name="DelayLoaded">Whether the import comes from the delay-load directory.</param>
/// <param name="Value">The pointer the slot holds now.</param>
/// <param name="Name">The imported name, when the image records one.</param>
/// <param name="Ordinal">The imported ordinal, for an import by ordinal.</param>
/// <param name="Hint">The export-table hint of a named import.</param>
/// <param name="TargetModule">The loaded module that contains the slot's value.</param>
/// <param name="TargetSymbol">Cheat Engine's name for the slot's value.</param>
public sealed record ModuleImport(
	[property: Description(
		"The DLL the import descriptor names, such as KERNEL32.dll or api-ms-win-core-synch-l1-2-0.dll.")]
	string Dll,
	[property: Description(
		"The address of the function's import address table (IAT) slot, uppercase hexadecimal without 0x; the code calls through it.")]
	string SlotAddress,
	[property: Description("Whether the import comes from the delay-load directory.")]
	bool DelayLoaded,
	[property: Description(
		"The pointer the slot holds now, uppercase hexadecimal: the resolved function, or for a delay-loaded import this module's loader stub until the first call.")]
	string Value,
	[property: Description(
		"The imported name; omitted for an import by ordinal and when the image keeps no lookup table (a bound IAT).")]
	string? Name = null,
	[property: Description("The imported ordinal; only for an import by ordinal.")]
	long? Ordinal = null,
	[property: Description("The export-table hint the linker recorded with a named import.")]
	int? Hint = null,
	[property: Description("The loaded module that contains value; omitted when no module does.")]
	string? TargetModule = null,
	[property: Description(
		"Cheat Engine's name for value, such as KERNELBASE.Sleep or game.exe+1A2B; omitted when it has none.")]
	string? TargetSymbol = null);

/// <summary>The differences between a module's code in memory and its file on disk.</summary>
/// <param name="Module">The module name.</param>
/// <param name="FilePath">The file that was compared.</param>
/// <param name="ComparedBytes">How many bytes were compared.</param>
/// <param name="UnreadableBytes">How many bytes of memory could not be read.</param>
/// <param name="RelocationsApplied">How many base relocations were applied to the file bytes.</param>
/// <param name="Truncated">Whether the scan stopped at the patch limit or the byte cap.</param>
/// <param name="Patches">The differing ranges.</param>
public sealed record PatchScanResult(
	[property: Description("The module name.")]
	string Module,
	[property: Description("The module file that was compared.")]
	string FilePath,
	[property: Description("How many bytes were compared.")]
	long ComparedBytes,
	[property: Description("How many bytes of the module's memory could not be read and were skipped.")]
	long UnreadableBytes,
	[property: Description("How many base relocations were applied to the file bytes before comparing.")]
	int RelocationsApplied,
	[property: Description("Whether the scan stopped at limit patches or at the 64 MiB comparison cap.")]
	bool Truncated,
	[property: Description("The differing ranges, by address.")]
	ModulePatch[] Patches);

/// <summary>One differing range of <see cref="PatchScanResult" />.</summary>
/// <param name="Address">The first differing address.</param>
/// <param name="Symbol">Cheat Engine's name for the address.</param>
/// <param name="Section">The section that holds the range.</param>
/// <param name="Length">The length of the range.</param>
/// <param name="FileBytes">The file's bytes after relocation.</param>
/// <param name="MemoryBytes">The bytes in memory.</param>
public sealed record ModulePatch(
	[property: Description("The first differing address as uppercase hexadecimal without 0x.")]
	string Address,
	[property: Description("The section that holds the range, such as .text.")]
	string Section,
	[property: Description("The length of the range in bytes; runs closer than four bytes are merged.")]
	int Length,
	[property: Description("The file's bytes after relocation, at most the first 64.")]
	string FileBytes,
	[property: Description("The bytes in memory, at most the first 64.")]
	string MemoryBytes,
	[property: Description("Cheat Engine's name for the address, such as game.exe+1A2B; omitted when it has none.")]
	string? Symbol = null);
