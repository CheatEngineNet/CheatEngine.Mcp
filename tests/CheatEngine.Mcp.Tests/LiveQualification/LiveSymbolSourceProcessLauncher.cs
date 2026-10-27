using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

using Microsoft.Win32.SafeHandles;

namespace CheatEngine.Mcp.Tests.LiveQualification;

/// <summary>Starts an admitted private CE source host in a job that dies with this harness.</summary>
internal sealed class LiveSymbolSourceProcessLauncher
{
	private readonly ILiveSymbolSourceProcessNativeAdapter _native;

	[SupportedOSPlatform("windows")]
	internal LiveSymbolSourceProcessLauncher() : this(new WindowsLiveSymbolSourceProcessNativeAdapter())
	{
	}

	internal LiveSymbolSourceProcessLauncher(ILiveSymbolSourceProcessNativeAdapter native)
	{
		_native = native;
	}

	internal LiveSymbolSourceProcessLaunch Launch(LiveSymbolSourceProcessLaunchRequest request)
	{
		LiveSymbolSourceProcessJobReceipt receipt = LaunchCore(request);
		try
		{
			return new LiveSymbolSourceProcessLaunch(Process.GetProcessById(receipt.ProcessId), receipt);
		}
		catch
		{
			receipt.Dispose();
			throw;
		}
	}

	internal LiveSymbolSourceProcessJobReceipt LaunchForAdapterTest(LiveSymbolSourceProcessLaunchRequest request) =>
		LaunchCore(request);

	private LiveSymbolSourceProcessJobReceipt LaunchCore(LiveSymbolSourceProcessLaunchRequest request)
	{
		ArgumentNullException.ThrowIfNull(request);
		LiveSymbolSourceCreatedProcess? process = null;
		ILiveSymbolSourceProcessJob? job = null;
		try
		{
			process = _native.CreateSuspended(request);
			job = _native.CreateJob();
			_native.ConfigureKillOnClose(job);
			_native.AssignProcessToJob(job, process);
			_native.Resume(process);
			process.DisposeThread();
			LiveSymbolSourceProcessJobReceipt receipt = new(process, job, _native);
			process = null;
			job = null;
			return receipt;
		}
		catch
		{
			if (process is not null)
			{
				try
				{
					_native.Terminate(process);
				}
				catch { }
			}
			job?.Dispose();
			throw;
		}
		finally
		{
			process?.Dispose();
		}
	}
}

internal sealed record LiveSymbolSourceProcessLaunchRequest(string FileName, string WorkingDirectory,
	IReadOnlyList<string> Arguments, IReadOnlyDictionary<string, string> Environment)
{
	internal static LiveSymbolSourceProcessLaunchRequest From(ProcessStartInfo start)
	{
		ArgumentNullException.ThrowIfNull(start);
		if (start.UseShellExecute || !string.IsNullOrEmpty(start.UserName))
		{
			throw new InvalidOperationException("The private source host must use a direct, current-user process launch.");
		}
		if (!string.IsNullOrEmpty(start.Arguments))
		{
			throw new InvalidOperationException("The private source host must supply structured arguments, not an unparsed command line.");
		}

		return new LiveSymbolSourceProcessLaunchRequest(start.FileName, start.WorkingDirectory,
			start.ArgumentList.ToArray(), start.Environment.ToDictionary(static pair => pair.Key,
				static pair => pair.Value ?? throw new ArgumentException("The private source host environment contains a null value."),
				StringComparer.OrdinalIgnoreCase));
	}
}

internal sealed class LiveSymbolSourceProcessLaunch : IDisposable
{
	private bool _disposed;

	internal LiveSymbolSourceProcessLaunch(Process process, LiveSymbolSourceProcessJobReceipt job)
	{
		Process = process;
		Job = job;
	}

	internal Process Process
	{
		get;
	}
	internal LiveSymbolSourceProcessJobReceipt Job
	{
		get;
	}

	public void Dispose()
	{
		if (_disposed)
		{
			return;
		}
		Job.Dispose();
		_disposed = true;
	}
}

/// <summary>Owns exactly one process job; closing it terminates that process tree.</summary>
internal sealed class LiveSymbolSourceProcessJobReceipt : IDisposable
{
	private readonly ILiveSymbolSourceProcessNativeAdapter _native;
	private ILiveSymbolSourceProcessJob? _job;
	private LiveSymbolSourceCreatedProcess? _process;

	internal LiveSymbolSourceProcessJobReceipt(LiveSymbolSourceCreatedProcess process,
		ILiveSymbolSourceProcessJob job, ILiveSymbolSourceProcessNativeAdapter native)
	{
		ProcessId = process.ProcessId;
		_process = process;
		_job = job;
		_native = native;
	}

	internal int ProcessId
	{
		get;
	}

	public void Dispose()
	{
		if (_job is not null)
		{
			_job.Dispose();
			_job = null;
		}
		if (_process is not null)
		{
			_native.WaitForExit(_process);
			_process.Dispose();
			_process = null;
		}
	}
}

internal interface ILiveSymbolSourceProcessNativeAdapter
{
	public LiveSymbolSourceCreatedProcess CreateSuspended(LiveSymbolSourceProcessLaunchRequest request);
	public ILiveSymbolSourceProcessJob CreateJob();
	public void ConfigureKillOnClose(ILiveSymbolSourceProcessJob job);
	public void AssignProcessToJob(ILiveSymbolSourceProcessJob job, LiveSymbolSourceCreatedProcess process);
	public void Resume(LiveSymbolSourceCreatedProcess process);
	public void Terminate(LiveSymbolSourceCreatedProcess process);
	public void WaitForExit(LiveSymbolSourceCreatedProcess process);
}

internal abstract class LiveSymbolSourceCreatedProcess(int processId) : IDisposable
{
	internal int ProcessId { get; } = processId;
	public abstract void DisposeThread();
	public abstract void Dispose();
}

internal interface ILiveSymbolSourceProcessJob : IDisposable
{
}

[SupportedOSPlatform("windows")]
internal sealed partial class WindowsLiveSymbolSourceProcessNativeAdapter : ILiveSymbolSourceProcessNativeAdapter
{
	private const uint CreateSuspendedFlag = 0x00000004;
	private const uint CreateUnicodeEnvironmentFlag = 0x00000400;
	private const uint JobObjectExtendedLimitInformationClass = 9;
	private const uint JobObjectLimitKillOnJobClose = 0x00002000;
	private const uint StillActive = 259;
	private const uint WaitObject0 = 0;
	private const uint WaitTimeout = 258;
	private const uint SourceHostExitTimeoutMilliseconds = 5000;

	public unsafe LiveSymbolSourceCreatedProcess CreateSuspended(LiveSymbolSourceProcessLaunchRequest request)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(request.FileName);
		ArgumentException.ThrowIfNullOrWhiteSpace(request.WorkingDirectory);
		string commandLine = BuildCommandLine(request.FileName, request.Arguments);
		char[] commandLineBuffer = (commandLine + '\0').ToCharArray();
		char[] environment = BuildEnvironmentBlock(request.Environment);
		StartupInfo startup = new()
		{
			Cb = (uint) Marshal.SizeOf<StartupInfo>()
		};
		fixed (char* commandLinePointer = commandLineBuffer)
		fixed (char* environmentPointer = environment)
		{
			if (CreateProcessW(request.FileName, commandLinePointer, 0, 0, 0,
				CreateSuspendedFlag | CreateUnicodeEnvironmentFlag, environmentPointer, request.WorkingDirectory, ref startup,
				out ProcessInformation information) == 0)
			{
				throw new Win32Exception(Marshal.GetLastPInvokeError(), "CreateProcessW failed for the private source host.");
			}
			return new WindowsLiveSymbolSourceCreatedProcess(checked((int) information.ProcessId),
				SafeKernelHandle.FromNative(information.Process), SafeKernelHandle.FromNative(information.Thread));
		}
	}

	public ILiveSymbolSourceProcessJob CreateJob()
	{
		SafeKernelHandle handle = CreateJobObjectW(0, null);
		if (handle.IsInvalid)
		{
			handle.Dispose();
			throw new Win32Exception(Marshal.GetLastPInvokeError(), "CreateJobObjectW failed for the private source host.");
		}
		return new WindowsLiveSymbolSourceProcessJob(handle);
	}

	public void ConfigureKillOnClose(ILiveSymbolSourceProcessJob job)
	{
		if (job is not WindowsLiveSymbolSourceProcessJob windowsJob)
		{
			throw new ArgumentException("The native job adapter received a foreign ownership handle.");
		}
		JobObjectExtendedLimitInformation limits = new();
		limits.BasicLimitInformation.LimitFlags = JobObjectLimitKillOnJobClose;
		if (SetInformationJobObject(windowsJob.Handle, JobObjectExtendedLimitInformationClass, ref limits,
			(uint) Marshal.SizeOf<JobObjectExtendedLimitInformation>()) == 0)
		{
			throw new Win32Exception(Marshal.GetLastPInvokeError(), "SetInformationJobObject failed for the private source host.");
		}
	}

	public void AssignProcessToJob(ILiveSymbolSourceProcessJob job, LiveSymbolSourceCreatedProcess process)
	{
		if (job is not WindowsLiveSymbolSourceProcessJob windowsJob || process is not WindowsLiveSymbolSourceCreatedProcess windowsProcess)
		{
			throw new ArgumentException("The native job adapter received a foreign ownership handle.");
		}
		if (AssignProcessToJobObject(windowsJob.Handle, windowsProcess.ProcessHandle) == 0)
		{
			throw new Win32Exception(Marshal.GetLastPInvokeError(), "AssignProcessToJobObject failed for the private source host.");
		}
	}

	public void Resume(LiveSymbolSourceCreatedProcess process)
	{
		if (process is not WindowsLiveSymbolSourceCreatedProcess windowsProcess)
		{
			throw new ArgumentException("The native job adapter received a foreign process handle.");
		}
		if (ResumeThread(windowsProcess.ThreadHandle) == uint.MaxValue)
		{
			throw new Win32Exception(Marshal.GetLastPInvokeError(), "ResumeThread failed for the private source host.");
		}
	}

	public void Terminate(LiveSymbolSourceCreatedProcess process)
	{
		if (process is WindowsLiveSymbolSourceCreatedProcess windowsProcess &&
			TerminateProcess(windowsProcess.ProcessHandle, 1) == 0 && Marshal.GetLastPInvokeError() != StillActive)
		{
			throw new Win32Exception(Marshal.GetLastPInvokeError(), "TerminateProcess failed while rolling back the private source host.");
		}
	}

	public void WaitForExit(LiveSymbolSourceCreatedProcess process)
	{
		if (process is not WindowsLiveSymbolSourceCreatedProcess windowsProcess)
		{
			throw new ArgumentException("The native job adapter received a foreign process handle.");
		}
		uint result = WaitForSingleObject(windowsProcess.ProcessHandle, SourceHostExitTimeoutMilliseconds);
		if (result == WaitObject0)
		{
			return;
		}
		if (result == WaitTimeout)
		{
			throw new TimeoutException("The private source host remained active after its job was closed.");
		}
		throw new Win32Exception(Marshal.GetLastPInvokeError(), "WaitForSingleObject failed for the private source host.");
	}

	private static string BuildCommandLine(string fileName, IReadOnlyList<string> arguments) =>
		string.Join(' ', new[] { Quote(fileName) }.Concat(arguments.Select(Quote)));

	private static string Quote(string value)
	{
		if (value.IndexOfAny([' ', '\t', '"']) < 0)
		{
			return value;
		}
		System.Text.StringBuilder builder = new("\"");
		int slashes = 0;
		foreach (char character in value)
		{
			if (character == '\\')
			{
				slashes++;
				continue;
			}
			if (character == '"')
			{
				builder.Append('\\', (slashes * 2) + 1).Append(character);
				slashes = 0;
				continue;
			}
			builder.Append('\\', slashes).Append(character);
			slashes = 0;
		}
		return builder.Append('\\', slashes * 2).Append('"').ToString();
	}

	private static char[] BuildEnvironmentBlock(IReadOnlyDictionary<string, string> environment)
	{
		if (environment.Any(static pair => pair.Key.Contains('=') || pair.Key.Contains('\0') || pair.Value.Contains('\0')))
		{
			throw new ArgumentException("The private source host environment contains an invalid entry.");
		}
		return (string.Join('\0', environment.OrderBy(static pair => pair.Key, StringComparer.OrdinalIgnoreCase)
			.Select(static pair => pair.Key + "=" + pair.Value)) + "\0\0").ToCharArray();
	}

	[LibraryImport("kernel32.dll", EntryPoint = "CreateProcessW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
	private static unsafe partial int CreateProcessW(string applicationName, char* commandLine, nint processAttributes,
		nint threadAttributes, int inheritHandles, uint creationFlags, void* environment, string currentDirectory,
		ref StartupInfo startupInfo, out ProcessInformation processInformation);

	[LibraryImport("kernel32.dll", EntryPoint = "CreateJobObjectW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
	private static partial SafeKernelHandle CreateJobObjectW(nint jobAttributes, string? name);

	[LibraryImport("kernel32.dll", SetLastError = true)]
	private static partial int SetInformationJobObject(SafeKernelHandle job, uint informationClass,
		ref JobObjectExtendedLimitInformation information, uint informationLength);

	[LibraryImport("kernel32.dll", SetLastError = true)]
	private static partial int AssignProcessToJobObject(SafeKernelHandle job, SafeKernelHandle process);

	[LibraryImport("kernel32.dll", SetLastError = true)]
	private static partial uint ResumeThread(SafeKernelHandle thread);

	[LibraryImport("kernel32.dll", SetLastError = true)]
	private static partial int TerminateProcess(SafeKernelHandle process, uint exitCode);

	[LibraryImport("kernel32.dll", SetLastError = true)]
	private static partial uint WaitForSingleObject(SafeKernelHandle handle, uint milliseconds);

	[LibraryImport("kernel32.dll", SetLastError = true)]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static partial bool CloseHandle(nint handle);

	[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
	private struct StartupInfo
	{
		internal uint Cb;
		internal nint Reserved;
		internal nint Desktop;
		internal nint Title;
		internal uint X;
		internal uint Y;
		internal uint XSize;
		internal uint YSize;
		internal uint XCountChars;
		internal uint YCountChars;
		internal uint FillAttribute;
		internal uint Flags;
		internal ushort ShowWindow;
		internal ushort Reserved2Count;
		internal nint Reserved2;
		internal nint StdInput;
		internal nint StdOutput;
		internal nint StdError;
	}

	[StructLayout(LayoutKind.Sequential)]
	private struct ProcessInformation
	{
		internal nint Process;
		internal nint Thread;
		internal uint ProcessId;
		internal uint ThreadId;
	}

	[StructLayout(LayoutKind.Sequential)]
	private struct JobObjectBasicLimitInformation
	{
		internal long PerProcessUserTimeLimit;
		internal long PerJobUserTimeLimit;
		internal uint LimitFlags;
		internal nuint MinimumWorkingSetSize;
		internal nuint MaximumWorkingSetSize;
		internal uint ActiveProcessLimit;
		internal nuint Affinity;
		internal uint PriorityClass;
		internal uint SchedulingClass;
	}

	[StructLayout(LayoutKind.Sequential)]
	private struct IoCounters
	{
		internal ulong ReadOperationCount;
		internal ulong WriteOperationCount;
		internal ulong OtherOperationCount;
		internal ulong ReadTransferCount;
		internal ulong WriteTransferCount;
		internal ulong OtherTransferCount;
	}

	[StructLayout(LayoutKind.Sequential)]
	private struct JobObjectExtendedLimitInformation
	{
		internal JobObjectBasicLimitInformation BasicLimitInformation;
		internal IoCounters IoInfo;
		internal nuint ProcessMemoryLimit;
		internal nuint JobMemoryLimit;
		internal nuint PeakProcessMemoryUsed;
		internal nuint PeakJobMemoryUsed;
	}

	private sealed class SafeKernelHandle : SafeHandleZeroOrMinusOneIsInvalid
	{
		public SafeKernelHandle() : base(true) { }

		internal static SafeKernelHandle FromNative(nint value)
		{
			SafeKernelHandle result = new();
			result.SetHandle(value);
			return result;
		}

		protected override bool ReleaseHandle() => CloseHandle(handle);
	}

	private sealed class WindowsLiveSymbolSourceCreatedProcess(int processId, SafeKernelHandle process,
		SafeKernelHandle thread) : LiveSymbolSourceCreatedProcess(processId)
	{
		internal SafeKernelHandle ProcessHandle { get; } = process;
		internal SafeKernelHandle ThreadHandle { get; } = thread;

		public override void Dispose()
		{
			DisposeThread();
			ProcessHandle.Dispose();
		}

		public override void DisposeThread() => ThreadHandle.Dispose();
	}

	private sealed class WindowsLiveSymbolSourceProcessJob(SafeKernelHandle handle) : ILiveSymbolSourceProcessJob
	{
		private readonly SafeKernelHandle _handle = handle;
		internal SafeKernelHandle Handle => _handle;
		public void Dispose() => _handle.Dispose();
	}
}
