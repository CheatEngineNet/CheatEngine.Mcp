using System.ComponentModel;
using System.Runtime.InteropServices;

using Microsoft.Win32.SafeHandles;

namespace CheatEngine.Mcp.Tests.LiveQualification.Infrastructure.NetworkIsolation;

/// <summary>
/// Pins the private executable namespace while WFP derives and uses its application identifier.
/// Every ancestor is held without delete sharing, and the image is held without write or delete sharing.
/// </summary>
internal sealed partial class WfpOwnedPathLease : IDisposable
{
	private const uint FileReadData = 0x0001;
	private const uint FileReadAttributes = 0x0080;
	private const uint FileShareRead = 0x00000001;
	private const uint FileShareWrite = 0x00000002;
	private const uint OpenExisting = 3;
	private const uint FileFlagBackupSemantics = 0x02000000;
	private const uint VolumeNameNt = 0x00000002;
	private readonly List<SafeFileHandle> _ancestorHandles;
	private bool _disposed;

	private WfpOwnedPathLease(List<SafeFileHandle> ancestorHandles, FileStream image, string imageFinalNtPath)
	{
		_ancestorHandles = ancestorHandles;
		Image = image;
		ImageFinalNtPath = imageFinalNtPath;
	}

	internal FileStream Image
	{
		get;
	}
	internal string ImageFinalNtPath
	{
		get;
	}

	internal static WfpOwnedPathLease Acquire(WfpNetworkIsolationPlan plan)
	{
		ArgumentNullException.ThrowIfNull(plan);
		List<SafeFileHandle> ancestors = [];
		FileStream? image = null;
		try
		{
			string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(plan.OwnedRunRoot));
			string application = Path.GetFullPath(plan.ApplicationPath);
			string volumeRoot = Path.GetPathRoot(root) ?? throw new InvalidOperationException("The owned run root has no volume root.");
			SafeFileHandle volume = OpenDirectory(volumeRoot);
			ancestors.Add(volume);
			string expectedNtPath = GetFinalNtPath(volume);
			string currentPath = volumeRoot;
			string rootRelative = root[volumeRoot.Length..];
			foreach (string segment in rootRelative.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
			{
				currentPath = Path.Combine(currentPath, segment);
				SafeFileHandle directory = OpenDirectory(currentPath);
				ancestors.Add(directory);
				expectedNtPath = AppendNtPath(expectedNtPath, segment);
				if (!string.Equals(GetFinalNtPath(directory), expectedNtPath, StringComparison.OrdinalIgnoreCase))
				{
					throw new InvalidOperationException("The owned run root must be a canonical path without reparse-point traversal.");
				}
			}

			string imageDirectory = Path.GetDirectoryName(application) ?? throw new InvalidOperationException("The private executable has no parent directory.");
			string relativeDirectory = Path.GetRelativePath(root, imageDirectory);
			if (!string.Equals(relativeDirectory, ".", StringComparison.Ordinal))
			{
				foreach (string segment in relativeDirectory.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
				{
					currentPath = Path.Combine(currentPath, segment);
					SafeFileHandle directory = OpenDirectory(currentPath);
					ancestors.Add(directory);
					expectedNtPath = AppendNtPath(expectedNtPath, segment);
					if (!string.Equals(GetFinalNtPath(directory), expectedNtPath, StringComparison.OrdinalIgnoreCase))
					{
						throw new InvalidOperationException("The private executable path must be canonical and must not traverse reparse points.");
					}
				}
			}

			SafeFileHandle imageHandle = OpenImage(application);
			try
			{
				image = new FileStream(imageHandle, FileAccess.Read, bufferSize: 4096, isAsync: false);
			}
			catch
			{
				imageHandle.Dispose();
				throw;
			}
			string imageNtPath = GetFinalNtPath(imageHandle);
			string expectedImageNtPath = AppendNtPath(expectedNtPath, Path.GetFileName(application));
			if (!string.Equals(imageNtPath, expectedImageNtPath, StringComparison.OrdinalIgnoreCase))
			{
				throw new InvalidOperationException("The private executable must be a canonical non-reparse file beneath the pinned owned run root.");
			}

			return new WfpOwnedPathLease(ancestors, image ?? throw new InvalidOperationException("The pinned private executable handle was not created."), imageNtPath);
		}
		catch
		{
			image?.Dispose();
			foreach (SafeFileHandle ancestor in ancestors)
			{
				ancestor.Dispose();
			}
			throw;
		}
	}

	internal void VerifySha256(string expectedSha256)
	{
		ObjectDisposedException.ThrowIf(_disposed, this);
		Image.Position = 0;
		string actual = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Image));
		Image.Position = 0;
		if (!string.Equals(actual, expectedSha256, StringComparison.OrdinalIgnoreCase))
		{
			throw new InvalidOperationException("The pinned private executable hash does not match the frozen expected hash.");
		}
	}

	internal unsafe void VerifyAppId(FwpByteBlob* appId)
	{
		if (appId == null || appId->Data == null || appId->Size == 0 || appId->Size % sizeof(char) != 0)
		{
			throw new InvalidOperationException("WFP returned an invalid application-ID blob.");
		}
		string actual = MemoryMarshal.Cast<byte, char>(new ReadOnlySpan<byte>(appId->Data, checked((int) appId->Size))).TrimEnd('\0').ToString();
		if (!string.Equals(actual, ImageFinalNtPath, StringComparison.OrdinalIgnoreCase))
		{
			throw new InvalidOperationException("The WFP application-ID blob does not identify the pinned executable handle.");
		}
	}

	public void Dispose()
	{
		if (_disposed)
		{
			return;
		}
		Image.Dispose();
		foreach (SafeFileHandle ancestor in _ancestorHandles)
		{
			ancestor.Dispose();
		}
		_disposed = true;
	}

	private static SafeFileHandle OpenDirectory(string path)
	{
		return Open(path, FileReadAttributes, FileShareRead | FileShareWrite, FileFlagBackupSemantics);
	}

	private static SafeFileHandle OpenImage(string path)
	{
		return Open(path, FileReadData | FileReadAttributes, FileShareRead, 0);
	}

	private static SafeFileHandle Open(string path, uint desiredAccess, uint shareMode, uint flags)
	{
		SafeFileHandle handle = CreateFileW(path, desiredAccess, shareMode, 0, OpenExisting, flags, 0);
		if (handle.IsInvalid)
		{
			handle.Dispose();
			throw new Win32Exception(Marshal.GetLastWin32Error(), "CreateFileW failed while pinning the private executable namespace.");
		}
		return handle;
	}

	private static string GetFinalNtPath(SafeFileHandle handle)
	{
		uint length = GetFinalPathNameByHandleW(handle, null, 0, VolumeNameNt);
		if (length == 0)
		{
			throw new Win32Exception(Marshal.GetLastWin32Error(), "GetFinalPathNameByHandleW failed while validating the private executable namespace.");
		}
		char[] buffer = new char[checked((int) length + 1)];
		uint written = GetFinalPathNameByHandleW(handle, buffer, (uint) buffer.Length, VolumeNameNt);
		if (written == 0 || written >= buffer.Length)
		{
			throw new Win32Exception(Marshal.GetLastWin32Error(), "GetFinalPathNameByHandleW returned an incomplete private executable path.");
		}
		return new string(buffer, 0, checked((int) written));
	}

	private static string AppendNtPath(string parent, string child) => parent.EndsWith('\\') ? parent + child : parent + "\\" + child;

	[LibraryImport("kernel32.dll", EntryPoint = "CreateFileW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
	private static partial SafeFileHandle CreateFileW(string fileName, uint desiredAccess, uint shareMode,
		nint securityAttributes, uint creationDisposition, uint flagsAndAttributes, nint templateFile);

	[LibraryImport("kernel32.dll", EntryPoint = "GetFinalPathNameByHandleW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
	private static partial uint GetFinalPathNameByHandleW(SafeFileHandle file, char[]? path, uint pathLength, uint flags);
}
