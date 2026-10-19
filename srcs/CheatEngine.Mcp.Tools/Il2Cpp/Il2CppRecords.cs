using System.ComponentModel;

using CheatEngine.Mcp.Tools.Modules;

namespace CheatEngine.Mcp.Tools.Il2Cpp;

/// <summary>The provenance and limits of an immutable imported <c>dump.cs</c> dataset.</summary>
public sealed record Il2CppDumpImport(
	[property: Description("The opaque dataset id used by subsequent il2cpp tools.")]
	string DatasetId,
	[property: Description("The supported input contract, always dump_cs_v1.")]
	string Format,
	[property: Description("The format contract version, always 1.")]
	int FormatVersion,
	[property: Description("The source file name without its path.")]
	string FileName,
	[property: Description("The source file length in bytes.")]
	long Length,
	[property: Description("The uppercase SHA-256 hash of the imported source bytes.")]
	string Sha256,
	[property: Description("The number of imported types.")]
	int TypeCount,
	[property: Description("The number of imported fields and methods.")]
	int MemberCount,
	[property: Description("The original PE build bound for method resolution, or null when no modulePath was supplied.")]
	ModuleBuildIdentity? Module);

/// <summary>A stable page of types matched from an imported dataset.</summary>
public sealed record Il2CppTypePage(
	[property: Description("The dataset searched.")]
	string DatasetId,
	[property: Description("The number of matching types before paging.")]
	int Total,
	[property: Description("The matching types in stable image then TypeDefIndex order.")]
	Il2CppType[] Types,
	[property: Description("The next filtered offset, omitted after the final page.")]
	int? NextOffset);

/// <summary>A type emitted in the supported dump.cs subset.</summary>
public sealed record Il2CppType(
	[property: Description("The stable type id, derived from image and TypeDefIndex.")]
	string TypeId,
	[property: Description("The emitted assembly image name.")]
	string Image,
	[property: Description("The emitted namespace, or empty for the global namespace.")]
	string Namespace,
	[property: Description("The declared type name and generic signature.")]
	string Name,
	[property: Description("The declared kind: class, struct, interface or enum.")]
	string Kind,
	[property: Description("The dump TypeDefIndex.")]
	int TypeDefIndex);

/// <summary>A stable page of fields or methods matched from an imported dataset.</summary>
public sealed record Il2CppMemberPage(
	[property: Description("The dataset searched.")]
	string DatasetId,
	[property: Description("The requested member kind.")]
	string Kind,
	[property: Description("The number of matching members before paging.")]
	int Total,
	[property: Description("The matching members in stable type then source order.")]
	Il2CppMember[] Members,
	[property: Description("The next filtered offset, omitted after the final page.")]
	int? NextOffset);

/// <summary>A field or method declared by an imported type.</summary>
public sealed record Il2CppMember(
	[property: Description("The stable field or method id.")]
	string MemberId,
	[property: Description("The stable declaring type id.")]
	string TypeId,
	[property: Description("field or method.")]
	string Kind,
	[property: Description("The member name.")]
	string Name,
	[property: Description("The emitted declaration without comments or body.")]
	string Signature,
	[property: Description("The field offset as uppercase hexadecimal without 0x, or null for static or const fields.")]
	string? Offset,
	[property: Description("The method RVA as uppercase hexadecimal without 0x, or null when the dump reports -1.")]
	string? Rva);

/// <summary>The validated target address for an imported method RVA.</summary>
public sealed record Il2CppMethodResolution(
	[property: Description("The resolved method id.")]
	string MethodId,
	[property: Description("The imported module-relative binding used for validation.")]
	ModuleRvaBinding Binding,
	[property: Description("The currently validated module-relative address.")]
	ModuleRvaResolution Resolution);

/// <summary>Confirmation that an immutable dataset was released.</summary>
public sealed record Il2CppDeleteResult(
	[property: Description("The released dataset id.")]
	string DatasetId,
	[property: Description("Whether a dataset was removed. False when it was already absent.")]
	bool Deleted);
