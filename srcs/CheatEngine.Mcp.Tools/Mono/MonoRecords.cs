using System.ComponentModel;

using CheatEngine.Mcp.Core.Jobs;

namespace CheatEngine.Mcp.Tools.Mono;

/// <summary>The state of Cheat Engine's injected Mono data collector.</summary>
public sealed record MonoStatus(
	[property: Description(
		"Whether Cheat Engine's Mono extension reports its collector attached to the selected process, whoever attached it.")]
	bool Attached,
	[property: Description("Whether Cheat Engine reports its limited IL2CPP mode.")]
	bool Il2Cpp = false,
	[property: Description("The collector version when Cheat Engine reports it; omitted in the same cases as domains.")]
	long? CollectorVersion = null,
	[property: Description(
		"The Mono application domain handles as opaque decimal strings, for mono_get_static_field_address; omitted when not attached, while the target is paused, and after a collector pipe timeout or error until another Mono tool reconnects the pipe.")]
	string[]? Domains = null);

/// <summary>The effects reported by a successful Mono collector attach.</summary>
public sealed record MonoAttachResult(
	[property: Description("Always true: the collector is attached to the selected process.")]
	bool Attached,
	[property: Description("Whether Cheat Engine reports its limited IL2CPP mode.")]
	bool Il2Cpp,
	[property: Description("The collector version when Cheat Engine reports it.")]
	long? CollectorVersion,
	[property: Description(
		"The effects left behind: collector_injected, mono_error_ok_patched (not on IL2CPP), cheat_engine_hooks_installed and uses_mono_table_option (when Cheat Engine set it).")]
	string[] HostEffects);

/// <summary>The result of closing the Mono collector connection.</summary>
public sealed record MonoDetachResult(
	[property: Description(
		"Always true: the collector attachment this activation made is closed and no longer tracked.")]
	bool Detached,
	[property: Description("The effects a detach cannot undo.")]
	string[] RemainingEffects,
	[property: Description(
		"Whether that attachment had already ended before this call (a detach, re-activation or target switch in Cheat Engine), so nothing was closed and any current collector attachment was left unchanged.")]
	bool AlreadyEnded = false);

/// <summary>One managed assembly and its Mono image handle.</summary>
public sealed record MonoAssembly(
	[property: Description("The opaque assembly handle as a decimal string.")]
	string AssemblyHandle,
	[property: Description("The opaque image handle as a decimal string, for mono_list_classes.")]
	string ImageHandle,
	[property: Description("The image name, such as Assembly-CSharp.")]
	string Name);

/// <summary>A page of managed Mono assemblies.</summary>
public sealed record MonoAssemblyPage(
	[property: Description("The assemblies of this page.")]
	MonoAssembly[] Assemblies,
	[property: Description("The number of assemblies Cheat Engine enumerated.")]
	int Total,
	[property: Description("The offset of the next page; omitted on the last page.")]
	int? NextOffset = null);

/// <summary>One Mono class. Handles are opaque and only valid while the collector stays attached.</summary>
public sealed record MonoClass(
	[property: Description("The opaque class handle as a decimal string.")]
	string Handle,
	[property: Description("The class name.")]
	string Name,
	[property: Description("The namespace; empty for the global namespace.")]
	string Namespace);

/// <summary>A page of classes in one Mono image.</summary>
public sealed record MonoClassPage(
	[property: Description("The image handle, as supplied.")]
	string ImageHandle,
	[property: Description("The classes of this page.")]
	MonoClass[] Classes,
	[property: Description(
		"The number of classes Cheat Engine enumerated in the image; with nameContains, the number that match.")]
	int Total,
	[property: Description("The offset of the next page; omitted on the last page.")]
	int? NextOffset = null);

/// <summary>One Mono field.</summary>
public sealed record MonoField(
	[property: Description("The opaque field handle as a decimal string.")]
	string Handle,
	[property: Description("The field name.")]
	string Name,
	[property: Description("The field's type name, when Cheat Engine reports it.")]
	string? TypeName,
	[property: Description(
		"The offset: from the object start for an instance field, from the class static data for a static field.")]
	long Offset,
	[property: Description("Whether the field is static (including literal constants).")]
	bool IsStatic,
	[property: Description("Whether the field is a literal constant, whose offset has no meaning.")]
	bool IsConst = false,
	[property: Description("The Mono FieldAttributes flags, when Cheat Engine reports them.")]
	long? Flags = null,
	[property: Description(
		"The static field's address as uppercase hexadecimal without 0x, when Cheat Engine reports the class static data.")]
	string? StaticAddress = null);

/// <summary>The fields of a Mono class.</summary>
public sealed record MonoFieldList(
	[property: Description("The class handle, as supplied.")]
	string ClassHandle,
	[property: Description("The fields copied, at most maximumFields, and with nameContains only the matching ones.")]
	MonoField[] Fields);

/// <summary>One Mono method.</summary>
public sealed record MonoMethod(
	[property: Description("The opaque method handle as a decimal string.")]
	string Handle,
	[property: Description("The method name.")]
	string Name,
	[property: Description("The comma-separated parameter types, when Cheat Engine reports them.")]
	string? Signature = null,
	[property: Description("The return type name, when Cheat Engine reports it.")]
	string? ReturnType = null,
	[property: Description("The parameter names in order, when Cheat Engine reports them.")]
	string[]? ParameterNames = null,
	[property: Description("The Mono MethodAttributes flags, when Cheat Engine reports them.")]
	long? Flags = null,
	[property: Description("Whether the method is static (flag 0x10), when Cheat Engine reports the flags.")]
	bool? IsStatic = null);

/// <summary>A page of methods in a Mono class.</summary>
public sealed record MonoMethodPage(
	[property: Description("The class handle, as supplied.")]
	string ClassHandle,
	[property: Description("The methods of this page.")]
	MonoMethod[] Methods,
	[property: Description(
		"The number of methods Cheat Engine enumerated in the class; with nameContains, the number that match.")]
	int Total,
	[property: Description("The offset of the next page; omitted on the last page.")]
	int? NextOffset = null);

/// <summary>The static field data address for a class and domain.</summary>
public sealed record MonoStaticFieldAddress(
	[property: Description("The class handle, as supplied.")]
	string ClassHandle,
	[property: Description("The domain handle actually used, as a decimal string.")]
	string DomainHandle,
	[property: Description("The class static data address as uppercase hexadecimal without 0x.")]
	string Address);

/// <summary>The native code address produced by Mono JIT compilation.</summary>
public sealed record MonoCompiledMethod(
	[property: Description("The method handle, as supplied.")]
	string MethodHandle,
	[property: Description("The native code address as uppercase hexadecimal without 0x.")]
	string NativeAddress);

/// <summary>One supported argument for <c>mono_invoke_method</c>.</summary>
public sealed record MonoInvokeArgument(
	[property: Description(
		"The Cheat Engine variable type code: 0 byte or bool, 1 int16, 2 int32, 3 int64, 4 float, 5 double, 6 string, 12 object or pointer.")]
	int Type,
	[property: Description(
		"The value as text: a decimal or 0x integer for 0 to 3 (true or false also for 0), a number for 4 and 5, the text for 6, or an object address or Cheat Engine address expression for 12 (0 for null).")]
	string Value);

/// <summary>The result of a single Mono method invocation.</summary>
public sealed record MonoMethodInvocation(
	[property: Description("Always true: the method ran in the target.")]
	bool Invoked,
	[property: Description(
		"The return value as text: numbers and addresses in decimal, strings as text, value types as name=value pairs.")]
	string? ReturnValue = null,
	[property: Description("The managed exception the method threw, when it threw one.")]
	string? Exception = null);

/// <summary>One field of a Mono object, with the value read from the target.</summary>
public sealed record MonoObjectField(
	[property: Description("The field name.")]
	string Name,
	[property: Description(
		"The offset: from the object start for an instance field, from the class static data for a static field.")]
	long Offset,
	[property: Description("Whether the field is static (including literal constants).")]
	bool IsStatic,
	[property: Description("Whether the field is a literal constant, which has no storage, address or value.")]
	bool IsConst = false,
	[property: Description("The field's type name, when Cheat Engine reports it.")]
	string? TypeName = null,
	[property: Description(
		"The field's type code, numbered like CorElementType, such as 8 for Int32, 12 for Single or 18 for a class reference, when Cheat Engine reports it.")]
	long? ElementType = null,
	[property: Description(
		"The field's address as uppercase hexadecimal without 0x: the object start plus the offset for an instance field, the static address for a static field when Cheat Engine reports the class static data.")]
	string? Address = null,
	[property: Description(
		"The value read from the target: decimal numbers, true or false, or an object, string or pointer address in hexadecimal; omitted for structs, enums, generic instances, constants, statics without an address and unreadable memory.")]
	string? Value = null);

/// <summary>The Mono object that contains an address, found without asking the collector about that address.</summary>
public sealed record MonoObject(
	[property: Description("The object's start address (its header) as uppercase hexadecimal without 0x.")]
	string Address,
	[property: Description(
		"How many bytes the supplied address lies past the object start, in decimal, to compare with the field offsets.")]
	long OffsetInObject,
	[property: Description(
		"The object's class handle as a decimal string, for mono_list_fields, mono_list_methods and mono_start_instance_search.")]
	string ClassHandle,
	[property: Description("The class name.")]
	string ClassName,
	[property: Description("The namespace; empty for the global namespace.")]
	string Namespace,
	[property: Description("The handle of the image whose class list confirmed the class, as a decimal string.")]
	string ImageHandle,
	[property: Description(
		"The number of fields Cheat Engine reported for the class, inherited and static ones included; more than the fields copied when maximumFields cut the list.")]
	int TotalFields,
	[property: Description("The fields copied, at most maximumFields, inherited ones first.")]
	MonoObjectField[] Fields);

/// <summary>The acknowledgement returned when a Mono instance search is scheduled.</summary>
public sealed record MonoInstanceSearch(
	[property: Description("The job id for mono_poll_instance_search and runtime_stop_job.")]
	string JobId,
	[property: Description("The class handle, as supplied.")]
	string ClassHandle,
	[property: Description("The number of addresses the job retains.")]
	int MaximumResults);

/// <summary>One potential instance found by Cheat Engine's bounded Mono lookup.</summary>
public sealed record MonoInstance(
	[property: Description("A candidate object address as uppercase hexadecimal without 0x.")]
	string Address);

/// <summary>A non-consuming page from a Mono instance search job.</summary>
public sealed record MonoInstanceSearchPage(
	[property: Description("The job's state; poll until it is no longer running.")]
	JobStatus Job,
	[property: Description("The candidate addresses after afterSequence.")]
	MonoInstance[] Instances,
	[property: Description("The oldest retained sequence number.")]
	long FirstSequence,
	[property: Description("The cursor to pass as afterSequence next.")]
	long NextAfterSequence,
	[property: Description("Whether retained addresses remain after this page.")]
	bool More,
	[property: Description("The number of addresses the job dropped.")]
	long Dropped);

/// <summary>The one bounded collector result copied by a Mono instance-search job.</summary>
public sealed record MonoInstanceBatch(
	[property: Description("The candidate addresses copied.")]
	MonoInstance[] Instances,
	[property: Description("The number of candidates Cheat Engine found.")]
	int Total);
