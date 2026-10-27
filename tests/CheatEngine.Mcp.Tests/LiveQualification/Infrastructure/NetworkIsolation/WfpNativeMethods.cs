using System.Runtime.InteropServices;

namespace CheatEngine.Mcp.Tests.LiveQualification.Infrastructure.NetworkIsolation;

/// <summary>Direct Windows x64 bindings. This type is inert unless a separately approved adapter invokes it.</summary>
internal static unsafe partial class WfpNativeMethods
{
	private const string Library = "fwpuclnt.dll";

	[LibraryImport(Library, EntryPoint = "FwpmEngineOpen0", StringMarshalling = StringMarshalling.Utf16)]
	internal static partial uint FwpmEngineOpen0(string? serverName, uint authnService, nint authIdentity,
		in FwpmSession0 session, out WfpEngineHandle engineHandle);

	[LibraryImport(Library, EntryPoint = "FwpmEngineClose0")]
	internal static partial uint FwpmEngineClose0(nint engineHandle);

	[LibraryImport(Library, EntryPoint = "FwpmTransactionBegin0")]
	internal static partial uint FwpmTransactionBegin0(WfpEngineHandle engineHandle, uint flags);

	[LibraryImport(Library, EntryPoint = "FwpmTransactionCommit0")]
	internal static partial uint FwpmTransactionCommit0(WfpEngineHandle engineHandle);

	[LibraryImport(Library, EntryPoint = "FwpmTransactionAbort0")]
	internal static partial uint FwpmTransactionAbort0(WfpEngineHandle engineHandle);

	[LibraryImport(Library, EntryPoint = "FwpmGetAppIdFromFileName0", StringMarshalling = StringMarshalling.Utf16)]
	internal static partial uint FwpmGetAppIdFromFileName0(string fileName, out nint appId);

	[LibraryImport(Library, EntryPoint = "FwpmFilterAdd0")]
	internal static partial uint FwpmFilterAdd0(WfpEngineHandle engineHandle, FwpmFilter0* filter, nint securityDescriptor,
		out ulong filterId);

	[LibraryImport(Library, EntryPoint = "FwpmFilterGetById0")]
	internal static partial uint FwpmFilterGetById0(WfpEngineHandle engineHandle, ulong filterId, out nint filter);

	[LibraryImport(Library, EntryPoint = "FwpmFilterDeleteById0")]
	internal static partial uint FwpmFilterDeleteById0(WfpEngineHandle engineHandle, ulong filterId);

	[LibraryImport(Library, EntryPoint = "FwpmFilterCreateEnumHandle0")]
	internal static partial uint FwpmFilterCreateEnumHandle0(WfpEngineHandle engineHandle, nint template, out nint enumHandle);

	[LibraryImport(Library, EntryPoint = "FwpmFilterEnum0")]
	internal static partial uint FwpmFilterEnum0(WfpEngineHandle engineHandle, nint enumHandle, uint requested,
		out nint entries, out uint returned);

	[LibraryImport(Library, EntryPoint = "FwpmFilterDestroyEnumHandle0")]
	internal static partial uint FwpmFilterDestroyEnumHandle0(WfpEngineHandle engineHandle, nint enumHandle);

	[LibraryImport(Library, EntryPoint = "FwpmFreeMemory0")]
	internal static partial void FwpmFreeMemory0(ref nint memory);
}
