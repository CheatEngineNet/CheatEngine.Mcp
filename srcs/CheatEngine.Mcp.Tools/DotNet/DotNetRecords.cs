using System.ComponentModel;

using CheatEngine.Mcp.Core.Jobs;

namespace CheatEngine.Mcp.Tools.DotNet;

/// <summary>The state of Cheat Engine's out-of-process .NET data collector.</summary>
public sealed record DotNetStatus(
	[property: Description("Whether this Cheat Engine build exposes a .NET data collector.")]
	bool Available,
	[property: Description("Whether the collector answered for the selected target.")]
	bool Attached,
	[property: Description("The number of application domains copied for the status check.")]
	int DomainCount = 0);

/// <summary>One .NET application domain. Its handle is opaque and scoped to the collector session.</summary>
public sealed record DotNetDomain(
	[property: Description("The opaque domain handle as a decimal string, for dotnet_list_modules.")]
	string Handle,
	[property: Description("The domain name.")]
	string Name);

/// <summary>A page of .NET application domains.</summary>
public sealed record DotNetDomainPage(
	[property: Description("The domains of this page.")]
	DotNetDomain[] Domains,
	[property: Description("The number of domains the collector reported.")]
	int Total,
	[property: Description("The offset of the next page; omitted on the last page.")]
	int? NextOffset = null);

/// <summary>One managed module. Its handle is opaque and scoped to the collector session.</summary>
public sealed record DotNetModule(
	[property: Description("The opaque module handle as a decimal string, for dotnet_list_types.")]
	string Handle,
	[property: Description("The module name, typically its full path.")]
	string Name);

/// <summary>A page of modules in one .NET application domain.</summary>
public sealed record DotNetModulePage(
	[property: Description("The domain handle, as supplied.")]
	string DomainHandle,
	[property: Description("The modules of this page.")]
	DotNetModule[] Modules,
	[property: Description("The number of modules the collector reported in the domain.")]
	int Total,
	[property: Description("The offset of the next page; omitted on the last page.")]
	int? NextOffset = null);

/// <summary>One type definition in a managed module.</summary>
public sealed record DotNetType(
	[property: Description("The TypeDef token as a decimal string, for dotnet_get_type and dotnet_list_methods.")]
	string Token,
	[property: Description("The full type name, including its namespace.")]
	string Name,
	[property: Description("The TypeAttributes flags, when the collector reports them.")]
	long? Flags = null,
	[property: Description("The base type's token as a decimal string, when the collector reports it.")]
	string? Extends = null);

/// <summary>A page of type definitions in one .NET module.</summary>
public sealed record DotNetTypePage(
	[property: Description("The module handle, as supplied.")]
	string ModuleHandle,
	[property: Description("The types of this page.")]
	DotNetType[] Types,
	[property: Description(
		"The number of types the collector enumerated in the module; with nameContains, the number that match.")]
	int Total,
	[property: Description("The offset of the next page; omitted on the last page.")]
	int? NextOffset = null);

/// <summary>One field in a managed type layout.</summary>
public sealed record DotNetField(
	[property: Description("The field name.")]
	string Name,
	[property: Description("The field offset from the object start; meaningless for a static field.")]
	long Offset,
	[property: Description("Whether the collector reports the field as static.")]
	bool IsStatic = false,
	[property: Description("The field's type name, when the collector reports it.")]
	string? FieldType = null,
	[property: Description("The field's CorElementType code, such as 8 for Int32 or 18 for a class reference.")]
	long? ElementType = null,
	[property: Description("The FieldAttributes flags, when the collector reports them.")]
	long? Attributes = null,
	[property: Description(
		"The static field's target address as uppercase hexadecimal without 0x, when the collector reports it.")]
	string? StaticAddress = null);

/// <summary>The layout that Cheat Engine reported for a .NET type.</summary>
public sealed record DotNetTypeDetails(
	[property: Description("The module handle, as supplied.")]
	string ModuleHandle,
	[property: Description("The type token, as supplied.")]
	string TypeToken,
	[property: Description("The type name, when the collector reports it.")]
	string? Name,
	[property: Description("The base type's name, when the collector reports a base type.")]
	string? BaseType,
	[property: Description("The collector's object type code, when reported.")]
	long? ObjectType,
	[property: Description("The CorElementType code of array or string elements, when reported.")]
	long? ElementType,
	[property: Description("The offset of an array's element count, when reported.")]
	long? CountOffset,
	[property: Description("The size of one array element, when reported.")]
	long? ElementSize,
	[property: Description("The offset of an array's first element, when reported.")]
	long? FirstElementOffset,
	[property: Description("The fields copied, at most maximumFields.")]
	DotNetField[] Fields,
	[property: Description("The base type's module handle as a decimal string, for dotnet_get_type.")]
	string? BaseTypeModuleHandle = null,
	[property: Description("The base type's token as a decimal string, for dotnet_get_type.")]
	string? BaseTypeToken = null);

/// <summary>One managed method.</summary>
public sealed record DotNetMethod(
	[property: Description("The MethodDef token as a decimal string, for dotnet_get_method_parameters.")]
	string Token,
	[property: Description("The method name.")]
	string Name,
	[property: Description("The MethodAttributes flags; 0x10 is static.")]
	long? Attributes = null,
	[property: Description("The MethodImplAttributes flags.")]
	long? ImplementationFlags = null,
	[property: Description("The IL body address as uppercase hexadecimal without 0x; omitted when zero.")]
	string? IlCode = null,
	[property: Description(
		"The JIT-compiled entry point as uppercase hexadecimal without 0x; omitted until the method is compiled.")]
	string? NativeCode = null,
	[property: Description("The addresses of other native code versions, as uppercase hexadecimal without 0x.")]
	string[]? SecondaryNativeCode = null);

/// <summary>A page of methods in one managed type.</summary>
public sealed record DotNetMethodPage(
	[property: Description("The module handle, as supplied.")]
	string ModuleHandle,
	[property: Description("The type token, as supplied.")]
	string TypeToken,
	[property: Description("The methods of this page.")]
	DotNetMethod[] Methods,
	[property: Description(
		"The number of methods the collector reported for the type; with nameContains, the number that match.")]
	int Total,
	[property: Description("The offset of the next page; omitted on the last page.")]
	int? NextOffset = null);

/// <summary>A field value observed on a managed object.</summary>
public sealed record DotNetObjectField(
	[property: Description("The field name.")]
	string Name,
	[property: Description(
		"The value read from the target: decimal numbers, true or false, or an object or pointer address in hexadecimal; omitted for value types and unreadable memory.")]
	string? Value = null,
	[property: Description("The field's type name, when the collector reports it.")]
	string? FieldType = null,
	[property: Description("The field offset from the object start, when the collector reports it.")]
	long? Offset = null,
	[property: Description("The field's CorElementType code, such as 8 for Int32 or 18 for a class reference.")]
	long? ElementType = null);

/// <summary>The .NET object and fields reported for one target address.</summary>
public sealed record DotNetObject(
	[property: Description("The object's start address as uppercase hexadecimal without 0x.")]
	string Address,
	[property: Description("The object's type name, when the collector reports it.")]
	string? TypeName,
	[property: Description("The fields copied, at most maximumFields.")]
	DotNetObjectField[] Fields);

/// <summary>The acknowledgement returned when an instance search has been scheduled.</summary>
public sealed record DotNetInstanceSearch(
	[property: Description("The job id for dotnet_poll_instance_search and runtime_stop_job.")]
	string JobId,
	[property: Description("The module handle, as supplied.")]
	string ModuleHandle,
	[property: Description("The type token, as supplied.")]
	string TypeToken,
	[property: Description("The number of addresses the job retains.")]
	int MaximumResults);

/// <summary>One address returned by a .NET instance search.</summary>
public sealed record DotNetInstance(
	[property: Description("An object address as uppercase hexadecimal without 0x.")]
	string Address);

/// <summary>A non-consuming page from a .NET instance search job.</summary>
public sealed record DotNetInstanceSearchPage(
	[property: Description("The job's state; poll until it is no longer running.")]
	JobStatus Job,
	[property: Description("The addresses after afterSequence.")]
	DotNetInstance[] Instances,
	[property: Description("The oldest retained sequence number.")]
	long FirstSequence,
	[property: Description("The cursor to pass as afterSequence next.")]
	long NextAfterSequence,
	[property: Description("Whether retained addresses remain after this page.")]
	bool More,
	[property: Description("The number of addresses the job dropped.")]
	long Dropped);

/// <summary>The bounded snapshot used as the one CE dispatch inside an instance-search job.</summary>
public sealed record DotNetInstanceBatch(
	[property: Description("The addresses copied.")]
	DotNetInstance[] Instances,
	[property: Description("The number of objects the collector found.")]
	int Total);

/// <summary>The parameters of one managed method, as the .NET collector reports them.</summary>
public sealed record DotNetMethodParameters(
	[property: Description("The module handle, as supplied.")]
	string ModuleHandle,
	[property: Description("The method token, as supplied.")]
	string MethodToken,
	[property: Description("The metadata entries in collector order; the list can be incomplete and include return metadata or unavailable placeholders.")]
	DotNetParameter[] Parameters,
	[property: Description(
		"The signature text the collector reports, such as System.Int32 (System.Single, System.String), when available.")]
	string? Signature = null);

/// <summary>One parameter of a managed method.</summary>
public sealed record DotNetParameter(
	[property: Description("The zero-based position in the returned collector entries, not necessarily the declared parameter ordinal.")]
	int Index,
	[property: Description("The reported parameter name; empty when unnamed or unavailable.")]
	string Name,
	[property: Description("The collector's CType code for a metadata constant/default value, not the declared parameter type. 1 (Void) means no constant; 0 can mean unavailable metadata. Use signature for declared types.")]
	int ElementType,
	[property: Description("The CorElementType name for elementType when known; this does not name the declared parameter type.")]
	string? ElementTypeName = null);
