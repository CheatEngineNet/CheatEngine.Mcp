using System.ComponentModel;
using System.Runtime.InteropServices;

using Microsoft.Win32.SafeHandles;

namespace CheatEngine.Mcp.Core.Files;

/// <summary>
///     The small Windows-native surface used to keep a file operation below a directory handle. Every relative open
///     rejects reparse points while the parser resolves the name, and every directory handle refuses delete sharing.
/// </summary>
internal static partial class WindowsAnchoredFiles
{
	internal const int StatusObjectNameNotFound = unchecked((int) 0xC0000034);
	internal const int StatusObjectPathNotFound = unchecked((int) 0xC000003A);
	internal const int StatusReparsePointEncountered = unchecked((int) 0xC000050B);
	internal const int StatusNameTooLong = unchecked((int) 0xC0000106);

	private const uint FileReadAttributes = 0x00000080;
	private const uint FileTraverse = 0x00000020;
	private const uint FileWriteData = 0x00000002;
	private const uint Delete = 0x00010000;
	private const uint Synchronize = 0x00100000;
	private const uint FileShareRead = 0x00000001;
	private const uint FileShareWrite = 0x00000002;
	private const uint FileAttributeNormal = 0x00000080;
	private const uint Win32OpenExisting = 3;
	private const uint FileOpen = 1;
	private const uint FileCreate = 2;
	private const uint FileDirectoryFile = 0x00000001;
	private const uint FileNonDirectoryFile = 0x00000040;
	private const uint FileSynchronousIoNonAlert = 0x00000020;
	private const uint FileFlagBackupSemantics = 0x02000000;
	private const uint ObjectCaseInsensitive = 0x00000040;
	private const uint ObjectDontReparse = 0x00001000;
	private const int FileRenameInformation = 10;
	private const int FileDispositionInformation = 13;

	/// <summary>Opens a volume root and denies its later rename or deletion while the returned handle is alive.</summary>
	internal static SafeFileHandle OpenVolumeRoot(string root)
	{
		SafeFileHandle handle = CreateFile(root, FileReadAttributes | FileTraverse | Synchronize,
			FileShareRead | FileShareWrite, 0, Win32OpenExisting, FileFlagBackupSemantics, 0);
		if (!handle.IsInvalid)
		{
			return handle;
		}

		int error = Marshal.GetLastPInvokeError();
		handle.Dispose();
		throw new IOException("The volume root could not be opened.", new Win32Exception(error));
	}

	/// <summary>Opens one non-link directory component relative to a directory handle.</summary>
	internal static SafeFileHandle OpenDirectory(SafeFileHandle directory, string name)
	{
		return OpenRelative(directory, name, FileReadAttributes | FileTraverse | Synchronize, FileOpen,
			FileDirectoryFile | FileSynchronousIoNonAlert);
	}

	/// <summary>Determines whether a direct child exists without following a reparse point.</summary>
	internal static bool EntryExists(SafeFileHandle directory, string name)
	{
		try
		{
			using SafeFileHandle handle = OpenRelative(directory, name, FileReadAttributes | Synchronize, FileOpen,
				FileSynchronousIoNonAlert);
			return true;
		}
		catch (WindowsFileException exception) when (exception.Status == StatusObjectNameNotFound)
		{
			return false;
		}
	}

	/// <summary>Creates a direct child file, rejecting an existing name and any reparse point.</summary>
	internal static SafeFileHandle CreateNewFile(SafeFileHandle directory, string name)
	{
		return OpenRelative(directory, name, FileWriteData | Delete | Synchronize, FileCreate,
			FileNonDirectoryFile | FileSynchronousIoNonAlert, shareAccess: 0);
	}

	/// <summary>Renames a file handle into a direct child of <paramref name="directory" />.</summary>
	internal static void Rename(SafeFileHandle file, SafeFileHandle directory, string name, bool overwrite)
	{
		int nameBytes = checked(name.Length * sizeof(char));
		int rootOffset = IntPtr.Size;
		int nameLengthOffset = checked(rootOffset + IntPtr.Size);
		int nameOffset = checked(nameLengthOffset + sizeof(uint));
		int informationLength = checked(nameOffset + nameBytes + IntPtr.Size - sizeof(uint));
		nint information = Marshal.AllocHGlobal(informationLength);
		try
		{
			for (int index = 0; index < informationLength; index++)
			{
				Marshal.WriteByte(information, index, 0);
			}

			Marshal.WriteByte(information, 0, overwrite ? (byte) 1 : (byte) 0);
			Marshal.WriteIntPtr(information, rootOffset, directory.DangerousGetHandle());
			Marshal.WriteInt32(information, nameLengthOffset, nameBytes);
			Marshal.Copy(name.ToCharArray(), 0, IntPtr.Add(information, nameOffset), name.Length);
			int status = NtSetInformationFile(file, out _, information, checked((uint) informationLength),
				FileRenameInformation);
			ThrowIfFailed(status, "The dump file could not be renamed.");
		}
		finally
		{
			Marshal.FreeHGlobal(information);
		}
	}

	/// <summary>Marks a temporary file for deletion as its last operation before its handle is closed.</summary>
	internal static void MarkForDelete(SafeFileHandle file)
	{
		nint information = Marshal.AllocHGlobal(sizeof(byte));
		try
		{
			Marshal.WriteByte(information, 0, 1);
			int status = NtSetInformationFile(file, out _, information, sizeof(byte), FileDispositionInformation);
			ThrowIfFailed(status, "The temporary dump file could not be removed.");
		}
		finally
		{
			Marshal.FreeHGlobal(information);
		}
	}

	private static SafeFileHandle OpenRelative(SafeFileHandle directory, string name, uint desiredAccess,
		uint createDisposition, uint createOptions, uint shareAccess = FileShareRead | FileShareWrite)
	{
		if (name.Length > (ushort.MaxValue - sizeof(char)) / sizeof(char))
		{
			throw new WindowsFileException("A file-name component is too long for Windows.", StatusNameTooLong);
		}

		int nameBytes = checked(name.Length * sizeof(char));
		nint characters = Marshal.StringToHGlobalUni(name);
		nint unicode = Marshal.AllocHGlobal(checked(IntPtr.Size * 2));
		try
		{
			Marshal.WriteInt16(unicode, 0, unchecked((short) nameBytes));
			Marshal.WriteInt16(unicode, sizeof(short), unchecked((short) (nameBytes + sizeof(char))));
			Marshal.WriteIntPtr(unicode, IntPtr.Size, characters);
			NativeObjectAttributes attributes = new()
			{
				Length = checked((uint) (IntPtr.Size * 6)),
				RootDirectory = directory.DangerousGetHandle(),
				ObjectName = unicode,
				Attributes = ObjectCaseInsensitive | ObjectDontReparse
			};
			int status = NtCreateFile(out nint rawHandle, desiredAccess, ref attributes, out _, 0, FileAttributeNormal,
				shareAccess, createDisposition, createOptions, 0, 0);
			if (status < 0)
			{
				DisposeFailureHandle(rawHandle);
				throw new WindowsFileException("A secure file operation was refused.", status);
			}

			SafeFileHandle handle = new(rawHandle, ownsHandle: true);
			if (handle.IsInvalid)
			{
				handle.Dispose();
				throw new WindowsFileException("A secure file operation returned an invalid handle.", status);
			}

			return handle;
		}
		finally
		{
			Marshal.FreeHGlobal(unicode);
			Marshal.FreeHGlobal(characters);
		}
	}

	private static void ThrowIfFailed(int status, string message)
	{
		if (status < 0)
		{
			throw new WindowsFileException(message, status);
		}
	}

	private static void DisposeFailureHandle(nint rawHandle)
	{
		if (rawHandle is 0 or -1)
		{
			return;
		}

		using SafeFileHandle handle = new(rawHandle, ownsHandle: true);
	}

	[LibraryImport("kernel32.dll", EntryPoint = "CreateFileW", SetLastError = true,
		StringMarshalling = StringMarshalling.Utf16)]
	private static partial SafeFileHandle CreateFile(string fileName, uint desiredAccess, uint shareMode,
		nint securityAttributes, uint creationDisposition, uint flagsAndAttributes, nint templateFile);

	[LibraryImport("ntdll.dll", EntryPoint = "NtCreateFile")]
	private static partial int NtCreateFile(out nint fileHandle, uint desiredAccess,
		ref NativeObjectAttributes objectAttributes, out NativeIoStatusBlock ioStatusBlock, nint allocationSize,
		uint fileAttributes, uint shareAccess, uint createDisposition, uint createOptions, nint eaBuffer,
		uint eaLength);

	[LibraryImport("ntdll.dll", EntryPoint = "NtSetInformationFile")]
	private static partial int NtSetInformationFile(SafeFileHandle fileHandle, out NativeIoStatusBlock ioStatusBlock,
		nint fileInformation, uint length, int fileInformationClass);

	[StructLayout(LayoutKind.Sequential)]
	private struct NativeObjectAttributes
	{
		internal uint Length;
		internal nint RootDirectory;
		internal nint ObjectName;
		internal uint Attributes;
		internal nint SecurityDescriptor;
		internal nint SecurityQualityOfService;
	}

	[StructLayout(LayoutKind.Sequential)]
	private struct NativeIoStatusBlock
	{
		internal nint Status;
		internal nuint Information;
	}
}

/// <summary>Describes a failed native operation without translating its status into a host-visible error prematurely.</summary>
internal sealed class WindowsFileException : IOException
{
	internal WindowsFileException(string message, int status)
		: base(message)
	{
		Status = status;
	}

	internal int Status
	{
		get;
	}
}
