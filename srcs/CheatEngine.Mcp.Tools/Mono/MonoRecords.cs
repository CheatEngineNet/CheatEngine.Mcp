using System.ComponentModel;

using CheatEngine.Mcp.Core.Jobs;

namespace CheatEngine.Mcp.Tools.Mono;

/// <summary>The state of Cheat Engine's injected Mono data collector.</summary>
public sealed record MonoStatus(
	[property: Description("Whether the collector pipe is attached to the selected target.")]
	bool Attached,
	[property: Description("Whether Cheat Engine reports its limited IL2CPP mode.")]
	bool Il2Cpp = false,
	[property: Description("The collector version when Cheat Engine reports it.")]
	long? CollectorVersion = null);

/// <summary>The effects reported by a successful Mono collector attach.</summary>
public sealed record MonoAttachResult(bool Attached, bool Il2Cpp, long? CollectorVersion, string[] HostEffects);

/// <summary>The result of closing the Mono collector connection.</summary>
public sealed record MonoDetachResult(bool Detached, string[] RemainingEffects);

/// <summary>One managed assembly and its Mono image handle.</summary>
public sealed record MonoAssembly(string AssemblyHandle, string ImageHandle, string Name);

/// <summary>A page of managed Mono assemblies.</summary>
public sealed record MonoAssemblyPage(MonoAssembly[] Assemblies, int Total, int? NextOffset = null);

/// <summary>One Mono class. Handles are opaque and only valid while the collector stays attached.</summary>
public sealed record MonoClass(string Handle, string Name, string Namespace);

/// <summary>A page of classes in one Mono image.</summary>
public sealed record MonoClassPage(string ImageHandle, MonoClass[] Classes, int Total, int? NextOffset = null);

/// <summary>One Mono field.</summary>
public sealed record MonoField(
	string Handle,
	string Name,
	string? TypeName,
	long Offset,
	bool IsStatic,
	long? Flags = null);

/// <summary>The fields of a Mono class.</summary>
public sealed record MonoFieldList(string ClassHandle, MonoField[] Fields);

/// <summary>One Mono method.</summary>
public sealed record MonoMethod(string Handle, string Name, string? Signature = null, string? ReturnType = null);

/// <summary>A page of methods in a Mono class.</summary>
public sealed record MonoMethodPage(string ClassHandle, MonoMethod[] Methods, int Total, int? NextOffset = null);

/// <summary>The static field data address for a class and domain.</summary>
public sealed record MonoStaticFieldAddress(string ClassHandle, string DomainHandle, string Address);

/// <summary>The native code address produced by Mono JIT compilation.</summary>
public sealed record MonoCompiledMethod(string MethodHandle, string NativeAddress);

/// <summary>One supported argument for <c>mono_invoke_method</c>.</summary>
public sealed record MonoInvokeArgument(
	[property:
		Description("The Cheat Engine variable type code, for example 8 for a 32-bit integer or 14 for a string.")]
	int Type,
	[property: Description("The argument value as text; Cheat Engine converts it according to type.")]
	string Value);

/// <summary>The result of a single Mono method invocation.</summary>
public sealed record MonoMethodInvocation(bool Invoked, string? ReturnValue = null, string? Exception = null);

/// <summary>The acknowledgement returned when a Mono instance search is scheduled.</summary>
public sealed record MonoInstanceSearch(string JobId, string ClassHandle, int MaximumResults);

/// <summary>One potential instance found by Cheat Engine's bounded Mono lookup.</summary>
public sealed record MonoInstance(string Address);

/// <summary>A non-consuming page from a Mono instance search job.</summary>
public sealed record MonoInstanceSearchPage(
	JobStatus Job,
	MonoInstance[] Instances,
	long FirstSequence,
	long NextAfterSequence,
	bool More,
	long Dropped);

/// <summary>The one bounded collector result copied by a Mono instance-search job.</summary>
public sealed record MonoInstanceBatch(MonoInstance[] Instances, int Total);
