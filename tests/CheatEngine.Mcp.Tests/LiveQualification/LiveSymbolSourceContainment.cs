using System.Diagnostics;
using System.Runtime.Versioning;

using CheatEngine.Mcp.Tests.LiveQualification.Infrastructure;
using CheatEngine.Mcp.Tests.LiveQualification.Infrastructure.NetworkIsolation;

namespace CheatEngine.Mcp.Tests.LiveQualification;

/// <summary>Owns the hidden original PDB and the dynamic, per-private-CE WFP filters for the admitted source run.</summary>
[SupportedOSPlatform("windows")]
internal sealed class LiveSymbolSourceContainment : IDisposable
{
	private readonly string _acknowledgement;
	private readonly List<LiveSymbolSourceProcessLaunch> _processLaunches = [];
	private readonly List<WfpNetworkIsolationSession> _sessions = [];
	private bool _activated;
	private bool _disposed;

	private LiveSymbolSourceContainment(LiveSymbolSourceFixtureStage fixture, string acknowledgement)
	{
		Fixture = fixture;
		_acknowledgement = acknowledgement;
	}

	internal LiveSymbolSourceFixtureStage Fixture
	{
		get;
	}
	internal string SymbolPath => Fixture.Manifest.EnvironmentSymbolPathPrefix;
	internal object? ActivationReceipt
	{
		get; private set;
	}
	internal object? CleanupReceipt
	{
		get; private set;
	}
	internal IReadOnlyList<LiveSymbolSourceProcessJobReceipt> ProcessJobs => _processLaunches.Select(static launch => launch.Job).ToArray();

	internal static LiveSymbolSourceContainment Prepare(NativeSymbolFixture fixture, string ownedRunRoot,
		string? acknowledgement)
	{
		if (!string.Equals(acknowledgement, WfpNetworkIsolationSession.Acknowledgement, StringComparison.Ordinal))
		{
			throw new UnauthorizedAccessException("The exact dynamic WFP acknowledgement is required before staging a source fixture.");
		}
		ArgumentNullException.ThrowIfNull(acknowledgement);
		return new LiveSymbolSourceContainment(LiveSymbolSourceFixture.Stage(fixture, ownedRunRoot), acknowledgement);
	}

	internal void Activate(string hostA, string hostB, string expectedHostSha256, string? acknowledgement)
	{
		ObjectDisposedException.ThrowIf(_disposed, this);
		if (_activated || !string.Equals(acknowledgement, _acknowledgement, StringComparison.Ordinal) ||
			!string.Equals(_acknowledgement, WfpNetworkIsolationSession.Acknowledgement, StringComparison.Ordinal))
		{
			throw new UnauthorizedAccessException("The approved source isolation session cannot be activated.");
		}

		try
		{
			foreach (string host in new[] { hostA, hostB })
			{
				string hash = CheatEngineInstallation.Sha256(host);
				if (!string.Equals(hash, expectedHostSha256, StringComparison.OrdinalIgnoreCase))
				{
					throw new InvalidDataException("The private source host no longer matches the frozen CE profile hash.");
				}
			}
			foreach (string host in new[] { hostA, hostB })
			{
				WfpNetworkIsolationSession session = WfpNetworkIsolationSession.PrepareNativeForQualification(
					Fixture.Manifest.OwnedRunRoot, host, expectedHostSha256, _acknowledgement);
				session.Activate(new WfpNetworkIsolationAuthorization(_acknowledgement), new WindowsWfpElevationToken());
				_sessions.Add(session);
			}
			_activated = true;
			ActivationReceipt = _sessions.Select(session => new { session.Plan, session.Activation }).ToArray();
		}
		catch
		{
			Dispose();
			throw;
		}
	}

	/// <summary>Starts one private CE only after its admitted WFP containment is active.</summary>
	[SupportedOSPlatform("windows")]
	internal Process Launch(ProcessStartInfo start)
	{
		ObjectDisposedException.ThrowIf(_disposed, this);
		if (!_activated)
		{
			throw new InvalidOperationException("The private source host cannot start before WFP containment is active.");
		}

		LiveSymbolSourceProcessLauncher launcher = new();
		LiveSymbolSourceProcessLaunch launch = launcher.Launch(LiveSymbolSourceProcessLaunchRequest.From(start));
		_processLaunches.Add(launch);
		return launch.Process;
	}

	public void Dispose()
	{
		if (_disposed)
		{
			return;
		}
		List<Exception> failures = [];
		foreach (LiveSymbolSourceProcessLaunch launch in _processLaunches.AsEnumerable().Reverse())
		{
			try
			{
				launch.Dispose();
			}
			catch (Exception exception) { failures.Add(exception); }
		}
		if (failures.Count != 0)
		{
			throw new AggregateException("Private source hosts did not terminate before WFP cleanup.", failures);
		}
		foreach (WfpNetworkIsolationSession session in _sessions.AsEnumerable().Reverse())
		{
			try
			{
				session.Dispose();
			}
			catch (Exception exception) { failures.Add(exception); }
		}
		try
		{
			Fixture.Dispose();
		}
		catch (Exception exception) { failures.Add(exception); }
		if (failures.Count != 0)
		{
			throw new AggregateException("Private symbol-source containment cleanup failed.", failures);
		}
		CleanupReceipt = new
		{
			originalPdbSha256 = CheatEngineInstallation.Sha256(Fixture.OriginalPdbPath),
			processJobs = _processLaunches.Select(launch => new { launch.Job.ProcessId }).ToArray(),
			network = _sessions.Select(session => session.Cleanup).ToArray()
		};
		_disposed = true;
	}
}
