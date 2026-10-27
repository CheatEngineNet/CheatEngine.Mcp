namespace CheatEngine.Mcp.Tests.LiveQualification;

public sealed class LiveSymbolSourceProcessLauncherTests
{
	[Fact]
	public void LaunchForAdapterTest_ValidRequest_CreatesAssignsThenResumesBeforeJobCleanup()
	{
		RecordingNativeAdapter native = new();
		LiveSymbolSourceProcessLauncher launcher = new(native);

		using LiveSymbolSourceProcessJobReceipt receipt = launcher.LaunchForAdapterTest(Request());

		Assert.Equal(42, receipt.ProcessId);
		Assert.Equal(["create", "job", "configure", "assign", "resume", "thread-dispose"], native.Events);
		receipt.Dispose();
		Assert.Equal(["create", "job", "configure", "assign", "resume", "thread-dispose", "job-dispose", "wait",
			"process-dispose"], native.Events);
	}

	[Fact]
	public void LaunchForAdapterTest_AssignFails_TerminatesOnlyNewChildAndClosesJob()
	{
		RecordingNativeAdapter native = new()
		{
			FailAt = "assign"
		};
		LiveSymbolSourceProcessLauncher launcher = new(native);

		Assert.Throws<InvalidOperationException>(() => launcher.LaunchForAdapterTest(Request()));

		Assert.Equal(["create", "job", "configure", "assign", "terminate", "job-dispose", "process-dispose"], native.Events);
	}

	[Fact]
	public void LaunchForAdapterTest_ResumeFails_TerminatesOnlyNewChildAndClosesJob()
	{
		RecordingNativeAdapter native = new()
		{
			FailAt = "resume"
		};
		LiveSymbolSourceProcessLauncher launcher = new(native);

		Assert.Throws<InvalidOperationException>(() => launcher.LaunchForAdapterTest(Request()));

		Assert.Equal(["create", "job", "configure", "assign", "resume", "terminate", "job-dispose", "process-dispose"], native.Events);
	}

	private static LiveSymbolSourceProcessLaunchRequest Request() => new("C:\\private CE\\cheatengine-x86_64.exe",
		"C:\\private CE", ["--named", "Tails says \"hello\""], new Dictionary<string, string>
		{
			["MCP_INSTANCE_NAME"] = "Live Qualification A"
		});

	private sealed class RecordingNativeAdapter : ILiveSymbolSourceProcessNativeAdapter
	{
		internal List<string> Events { get; } = [];
		internal string? FailAt
		{
			get; init;
		}

		public LiveSymbolSourceCreatedProcess CreateSuspended(LiveSymbolSourceProcessLaunchRequest request)
		{
			Events.Add("create");
			ThrowIfRequested("create");
			return new RecordingProcess(42, Events);
		}

		public ILiveSymbolSourceProcessJob CreateJob()
		{
			Events.Add("job");
			ThrowIfRequested("job");
			return new RecordingJob(Events);
		}

		public void ConfigureKillOnClose(ILiveSymbolSourceProcessJob job)
		{
			Events.Add("configure");
			ThrowIfRequested("configure");
		}

		public void AssignProcessToJob(ILiveSymbolSourceProcessJob job, LiveSymbolSourceCreatedProcess process)
		{
			Events.Add("assign");
			ThrowIfRequested("assign");
		}

		public void Resume(LiveSymbolSourceCreatedProcess process)
		{
			Events.Add("resume");
			ThrowIfRequested("resume");
		}

		public void Terminate(LiveSymbolSourceCreatedProcess process) => Events.Add("terminate");

		public void WaitForExit(LiveSymbolSourceCreatedProcess process) => Events.Add("wait");

		private void ThrowIfRequested(string operation)
		{
			if (FailAt == operation)
			{
				throw new InvalidOperationException(operation);
			}
		}
	}

	private sealed class RecordingProcess(int processId, List<string> events) : LiveSymbolSourceCreatedProcess(processId)
	{
		public override void DisposeThread() => events.Add("thread-dispose");
		public override void Dispose() => events.Add("process-dispose");
	}

	private sealed class RecordingJob(List<string> events) : ILiveSymbolSourceProcessJob
	{
		public void Dispose() => events.Add("job-dispose");
	}
}
