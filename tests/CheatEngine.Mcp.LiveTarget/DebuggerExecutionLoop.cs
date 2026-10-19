using System.ComponentModel;
using System.Runtime.InteropServices;

namespace CheatEngine.Mcp.LiveTarget;

internal sealed partial class DebuggerExecutionLoop : IDisposable
{
	private const uint CommitReserve = 0x3000;
	private const uint Release = 0x8000;
	private const uint ExecuteReadWrite = 0x40;
	private const uint Signaled = 0;
	private const uint TimedOut = 0x102;
	private const int AllocationSize = 4096;
	private const int StopOffset = 0x100;
	private const int HitOffset = 0x104;
	private const int ProgressOffset = 0x108;
	private IntPtr _memory;
	private IntPtr _thread;
	private bool _joined;

	private DebuggerExecutionLoop(IntPtr memory, IntPtr thread, int threadId) =>
		(_memory, _thread, ThreadId) = (memory, thread, threadId);

	internal int ThreadId
	{
		get;
	}
	internal IntPtr CodeStart => _memory;
	internal IntPtr EntryAddress => IntPtr.Add(_memory, 9);
	internal IntPtr CaptureAddress => EntryAddress;
	internal IntPtr RunToAddress => IntPtr.Add(_memory, 23);
	internal IntPtr CodeEnd => IntPtr.Add(_memory, DebuggerExecutionLoopEncoding.Length);
	internal IntPtr ProgressAddress => IntPtr.Add(_memory, ProgressOffset);
	internal IntPtr HitCounterAddress => IntPtr.Add(_memory, HitOffset);

	internal static DebuggerExecutionLoop Start()
	{
		if (!OperatingSystem.IsWindows() || IntPtr.Size != 8)
		{
			throw new PlatformNotSupportedException("The debugger execution fixture requires Windows x64.");
		}

		IntPtr memory = VirtualAlloc(IntPtr.Zero, AllocationSize, CommitReserve, ExecuteReadWrite);
		if (memory == IntPtr.Zero)
		{
			throw new Win32Exception(Marshal.GetLastWin32Error(), "VirtualAlloc failed for debugger execution fixture.");
		}

		IntPtr thread = IntPtr.Zero;
		try
		{
			Marshal.Copy(DebuggerExecutionLoopEncoding.Encode((nint) memory, (nint) ResolveSleep()), 0, memory,
				DebuggerExecutionLoopEncoding.Length);
			if (!FlushInstructionCache(GetCurrentProcess(), memory, DebuggerExecutionLoopEncoding.Length))
			{
				throw new Win32Exception(Marshal.GetLastWin32Error(), "FlushInstructionCache failed for debugger execution fixture.");
			}

			thread = CreateThread(IntPtr.Zero, 0, memory, IntPtr.Zero, 0, out int threadId);
			if (thread == IntPtr.Zero)
			{
				throw new Win32Exception(Marshal.GetLastWin32Error(), "CreateThread failed for debugger execution fixture.");
			}
			if (threadId <= 0)
			{
				Environment.FailFast("CreateThread returned a running debugger fixture without a thread identifier.");
				throw new InvalidOperationException("CreateThread returned no thread identifier.");
			}

			return new DebuggerExecutionLoop(memory, thread, threadId);
		}
		catch
		{
			// A successfully created thread may already be executing from this allocation.
			// Program owns its shutdown after publication; never free executable memory here.
			if (thread != IntPtr.Zero)
			{
				CloseHandle(thread);
				throw;
			}

			if (!VirtualFree(memory, 0, Release))
			{
				throw new Win32Exception(Marshal.GetLastWin32Error(), "VirtualFree failed after debugger fixture setup failure.");
			}

			throw;
		}
	}

	internal bool TryStop(TimeSpan timeout)
	{
		if (_joined)
		{
			return true;
		}

		Marshal.WriteInt32(IntPtr.Add(_memory, StopOffset), 1);
		uint wait = WaitForSingleObject(_thread, checked((uint) Math.Ceiling(timeout.TotalMilliseconds)));
		if (wait == TimedOut)
		{
			return false;
		}

		if (wait != Signaled)
		{
			throw new Win32Exception(Marshal.GetLastWin32Error(), "WaitForSingleObject failed for debugger execution fixture.");
		}

		_joined = true;
		if (!CloseHandle(_thread))
		{
			throw new Win32Exception(Marshal.GetLastWin32Error(), "CloseHandle failed for debugger execution fixture.");
		}

		_thread = IntPtr.Zero;
		if (!VirtualFree(_memory, 0, Release))
		{
			throw new Win32Exception(Marshal.GetLastWin32Error(), "VirtualFree failed for debugger execution fixture.");
		}

		_memory = IntPtr.Zero;
		return true;
	}

	public void Dispose()
	{
		if (_memory != IntPtr.Zero && !TryStop(TimeSpan.FromSeconds(5)))
		{
			throw new TimeoutException("The debugger fixture thread did not stop; do not free its executable memory.");
		}
	}

	private static IntPtr ResolveSleep()
	{
		IntPtr kernel32 = GetModuleHandle("kernel32.dll");
		IntPtr sleep = kernel32 == IntPtr.Zero ? IntPtr.Zero : GetProcAddress(kernel32, "Sleep");
		return sleep != IntPtr.Zero
			? sleep
			: throw new Win32Exception(Marshal.GetLastWin32Error(), "Sleep export was unavailable.");
	}

	[LibraryImport("kernel32.dll", SetLastError = true)]
	private static partial IntPtr VirtualAlloc(IntPtr address, nuint size, uint type, uint protection);
	[LibraryImport("kernel32.dll", SetLastError = true)]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static partial bool VirtualFree(IntPtr address, nuint size, uint type);
	[LibraryImport("kernel32.dll", SetLastError = true)]
	private static partial IntPtr CreateThread(IntPtr attributes, nuint stack, IntPtr start, IntPtr parameter, uint flags,
		out int id);
	[LibraryImport("kernel32.dll", SetLastError = true)]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static partial bool FlushInstructionCache(IntPtr process, IntPtr address, nuint size);
	[LibraryImport("kernel32.dll", SetLastError = true)]
	private static partial IntPtr GetCurrentProcess();
	[LibraryImport("kernel32.dll", EntryPoint = "GetModuleHandleW", SetLastError = true,
		StringMarshalling = StringMarshalling.Utf16)]
	private static partial IntPtr GetModuleHandle(string name);
	[LibraryImport("kernel32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf8)]
	private static partial IntPtr GetProcAddress(IntPtr module, string name);
	[LibraryImport("kernel32.dll", SetLastError = true)]
	private static partial uint WaitForSingleObject(IntPtr handle, uint milliseconds);
	[LibraryImport("kernel32.dll", SetLastError = true)]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static partial bool CloseHandle(IntPtr handle);
}
