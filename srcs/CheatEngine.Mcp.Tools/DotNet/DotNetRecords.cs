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
public sealed record DotNetDomain(string Handle, string Name);

/// <summary>A page of .NET application domains.</summary>
public sealed record DotNetDomainPage(DotNetDomain[] Domains, int Total, int? NextOffset = null);

/// <summary>One managed module. Its handle is opaque and scoped to the collector session.</summary>
public sealed record DotNetModule(string Handle, string Name);

/// <summary>A page of modules in one .NET application domain.</summary>
public sealed record DotNetModulePage(string DomainHandle, DotNetModule[] Modules, int Total, int? NextOffset = null);

/// <summary>One type definition in a managed module.</summary>
public sealed record DotNetType(string Token, string Name, long? Flags = null, string? Extends = null);

/// <summary>A page of type definitions in one .NET module.</summary>
public sealed record DotNetTypePage(string ModuleHandle, DotNetType[] Types, int Total, int? NextOffset = null);

/// <summary>One field in a managed type layout.</summary>
public sealed record DotNetField(string Name, long Offset, string? FieldType = null, string? StaticAddress = null);

/// <summary>The layout that Cheat Engine reported for a .NET type.</summary>
public sealed record DotNetTypeDetails(
	string ModuleHandle,
	string TypeToken,
	string? Name,
	string? BaseType,
	long? ObjectType,
	long? ElementType,
	long? CountOffset,
	long? ElementSize,
	long? FirstElementOffset,
	DotNetField[] Fields);

/// <summary>One managed method.</summary>
public sealed record DotNetMethod(
	string Token,
	string Name,
	long? Attributes = null,
	long? ImplementationFlags = null,
	string? IlCode = null,
	string? NativeCode = null,
	string[]? SecondaryNativeCode = null);

/// <summary>A page of methods in one managed type.</summary>
public sealed record DotNetMethodPage(
	string ModuleHandle,
	string TypeToken,
	DotNetMethod[] Methods,
	int Total,
	int? NextOffset = null);

/// <summary>A field value observed on a managed object.</summary>
public sealed record DotNetObjectField(
	string Name,
	string? Value = null,
	string? FieldType = null,
	long? Offset = null);

/// <summary>The .NET object and fields reported for one target address.</summary>
public sealed record DotNetObject(string Address, string? TypeName, DotNetObjectField[] Fields);

/// <summary>The acknowledgement returned when an instance search has been scheduled.</summary>
public sealed record DotNetInstanceSearch(string JobId, string ModuleHandle, string TypeToken, int MaximumResults);

/// <summary>One address returned by a .NET instance search.</summary>
public sealed record DotNetInstance(string Address);

/// <summary>A non-consuming page from a .NET instance search job.</summary>
public sealed record DotNetInstanceSearchPage(
	JobStatus Job,
	DotNetInstance[] Instances,
	long FirstSequence,
	long NextAfterSequence,
	bool More,
	long Dropped);

/// <summary>The bounded snapshot used as the one CE dispatch inside an instance-search job.</summary>
public sealed record DotNetInstanceBatch(DotNetInstance[] Instances, int Total);
