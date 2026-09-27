using System.Globalization;

using CheatEngine.Mcp.Core.Features;
using CheatEngine.Mcp.Core.Jobs;
using CheatEngine.Mcp.Tests.Support;

namespace CheatEngine.Mcp.Tests.Core;

/// <summary>The fixed state and job kernel: its limits match the managed side and it never loads code or gains a gate.</summary>
public sealed class LuaJobKernelScriptsTests
{
	private static readonly string[] All =
	[
		LuaJobKernelScripts.Kernel,
		LuaJobKernelScripts.Strategies,
		LuaJobKernelScripts.Poll,
		LuaJobKernelScripts.Statuses,
		LuaJobKernelScripts.Stop,
		LuaJobKernelScripts.Sweep,
		LuaJobKernelScripts.StateSnapshot,
		LuaJobKernelScripts.StateReleaseUnmanaged,
		LuaJobKernelScripts.StateAcknowledge,
		LuaJobKernelScripts.StateReleaseResource
	];

	private static readonly string[] BlockingCalls =
		["waitfor", "waitTillDone", "sleep(", "showMessage", "messageDialog"];

	public static TheoryData<int> Scripts
	{
		get
		{
			TheoryData<int> data = [];
			for (int index = 0; index < All.Length; index++)
			{
				data.Add(index);
			}

			return data;
		}
	}

	[Theory]
	[InlineData("stateMaximumNamespaces", LuaJobKernelScripts.MaximumNamespaces)]
	[InlineData("stateMaximumResources", LuaJobKernelScripts.MaximumResourcesPerNamespace)]
	[InlineData("stateMaximumText", LuaJobKernelScripts.MaximumTextBytes)]
	[InlineData("stateMaximumSnapshot", LuaJobKernelScripts.MaximumSnapshotEntries)]
	[InlineData("stateMaximumAcknowledged", LuaJobKernelScripts.MaximumAcknowledgedIds)]
	[InlineData("jobMaximumBufferItems", LuaJobKernelScripts.MaximumBufferItems)]
	[InlineData("jobMaximumPollItems", LuaJobKernelScripts.MaximumPollItems)]
	[InlineData("jobMaximumTimeToLive", LuaJobKernelScripts.MaximumTimeToLiveMilliseconds)]
	[InlineData("jobMaximumPerNamespace", LuaJobKernelScripts.MaximumJobsPerNamespace)]
	[InlineData("jobSweepInterval", LuaJobKernelScripts.SweepIntervalMilliseconds)]
	[InlineData("jobMinimumSliceInterval", LuaJobKernelScripts.MinimumSliceIntervalMilliseconds)]
	[InlineData("jobMaximumSliceInterval", LuaJobKernelScripts.MaximumSliceIntervalMilliseconds)]
	[InlineData("jobMaximumSliceWork", LuaJobKernelScripts.MaximumSliceWorkMilliseconds)]
	public void Kernel_LuaLimit_MatchesTheManagedConstant(string name, int value)
	{
		Assert.Contains("local " + name + " = " + value.ToString(CultureInfo.InvariantCulture) + "\r\n",
			LuaJobKernelScripts.Strategies.ReplaceLineEndings("\r\n"), StringComparison.Ordinal);
	}

	[Fact]
	public void Limits_ManagedSide_AgreeWithTheKernel()
	{
		Assert.Equal(LuaJobKernelScripts.MaximumPollItems, JobRegistry.MaximumPollItems);
		Assert.Equal(LuaJobKernelScripts.MaximumTimeToLiveMilliseconds, JobRegistry.MaximumTimeToLiveSeconds * 1000);
		Assert.Equal(LuaJobKernelScripts.MaximumAcknowledgedIds, McpStateLedger.MaximumAcknowledgedIds);
		Assert.Equal(300, new McpExecutionOptions().JobMaxTtlSeconds);
	}

	[Fact]
	public void EntryScripts_ExtendTheKernel()
	{
		foreach (string script in All.Skip(1))
		{
			Assert.StartsWith(LuaJobKernelScripts.Kernel + "\n", script, StringComparison.Ordinal);
		}
	}

	[Theory]
	[MemberData(nameof(Scripts))]
	public void Script_Fixed_NeverLoadsCodeWaitsOrGainsAFeatureGate(int index)
	{
		string script = All[index];
		LuaFixedScriptAssert.NeverLoadsCode(script);
		Assert.DoesNotContain("_G.", script, StringComparison.Ordinal);
		foreach (string blocking in BlockingCalls)
		{
			Assert.DoesNotContain(blocking, script, StringComparison.Ordinal);
		}

		// The scan is lexical over comments too: an ungated kernel must never name a sensitive API.
		Assert.Empty(LuaFeatureScan.Scan(script));
	}
}
