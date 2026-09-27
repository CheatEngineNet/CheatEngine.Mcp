using CheatEngine.Client;
using CheatEngine.Client.Inspection;
using CheatEngine.Client.Memory;
using CheatEngine.Client.Processes;
using CheatEngine.Client.Results;
using CheatEngine.Client.Scanning;
using CheatEngine.Mcp.Tests.Support;
using CheatEngine.Mcp.Tools;
using CheatEngine.SDK.Engine.Inspection;
using CheatEngine.SDK.Engine.Runtime;
using CheatEngine.SDK.Engine.Values;

using Xunit.Sdk;

namespace CheatEngine.Mcp.Tests.Tools;

[Collection(nameof(SerialTestGroup))]
public sealed class ToolBehaviorTests
{
	[Fact]
	public void ScanTool_MemoryScanThenReset_ReleasesOwnedSession()
	{
		int[] releases = [0];
		IValueScanSession session = ClientTestDouble.Create<IValueScanSession>((method, _) => method.Name switch
		{
			"get_State" => ValueScanSessionState.Created,
			"FirstScan" => null,
			"GetResultCount" => 2UL,
			"Release" => Release(releases),
			_ => throw new NotSupportedException(method.Name)
		});
		IValueScanner scanner = ClientTestDouble.Create<IValueScanner>((method, _) => method.Name == "CreateSession"
			? session
			: throw new NotSupportedException(method.Name));
		ScanTool tool = new(ClientTestDouble.Client((nameof(ICheatEngineClient.ValueScans), scanner)));

		ToolResultAssert.IsSuccess(tool.MemoryScan("health", "int32", "100"));
		ToolResultAssert.IsSuccess(tool.ResetMemoryScan("health"));

		Assert.True(releases[0] == 1, "Reset must release the Client-owned value-scan session.");
	}

	[Fact]
	public void ProcessTool_OpenProcess_SamePid_DoesNotReleaseOwnedScanOrReattach()
	{
		int[] releases = [0];
		IValueScanSession session = ClientTestDouble.Create<IValueScanSession>((method, _) => method.Name switch
		{
			"get_State" => ValueScanSessionState.Created,
			"FirstScan" => null,
			"GetResultCount" => 1UL,
			"get_IsReleased" => false,
			"Release" => Release(releases),
			_ => throw new NotSupportedException(method.Name)
		});
		IValueScanner scanner = ClientTestDouble.Create<IValueScanner>((method, _) => method.Name == "CreateSession"
			? session
			: throw new NotSupportedException(method.Name));
		ProcessSnapshot current = new(new TargetProcessId(42), null, null, TargetBackend.LocalProcess,
			CheatEngineArchitecture.X64, PointerSize.Bit64, 8, null, 0);
		IProcessClient processes = ClientTestDouble.Create<IProcessClient>((method, arguments) =>
		{
			if (method.Name == "TryGetCurrentProcess")
			{
				arguments![0] = current;
				arguments[1] = default(CheatEngineFailure);
				return true;
			}

			throw new XunitException($"Same-PID attach unexpectedly called {method.Name}.");
		});
		TargetResources resources = new();
		ICheatEngineClient client = ClientTestDouble.Client((nameof(ICheatEngineClient.ValueScans), scanner),
			(nameof(ICheatEngineClient.Processes), processes));
		ScanTool scanTool = new(client, resources);
		ProcessTool processTool = new(client, TestRuntime.Info, resources);

		ToolResultAssert.IsSuccess(scanTool.MemoryScan("health", "int32", "100"));
		ToolResultAssert.IsSuccess(processTool.OpenProcess("42"));
		object refused = processTool.OpenProcess("43");
		Assert.False(ToolResultAssert.GetProperty<bool>(refused, "success"));
		Assert.Contains("still hold state", ToolResultAssert.GetProperty<string>(refused, "error"),
			StringComparison.Ordinal);

		Assert.True(releases[0] == 0, "A same-PID request must retain the owned scan session.");
	}

	private static ICheatEngineClient CreateClient(IMemoryClient? memory = null, Address? address = null)
	{
		IInspectionClient inspection = ClientTestDouble.Create<IInspectionClient>((method, _) =>
			method.Name == "ResolveAddress"
				? address ?? new Address(0x1234)
				: throw new NotSupportedException(method.Name));
		return memory is null
			? ClientTestDouble.Client((nameof(ICheatEngineClient.Inspection), inspection))
			: ClientTestDouble.Client((nameof(ICheatEngineClient.Inspection), inspection),
				(nameof(ICheatEngineClient.Memory), memory));
	}

	private static LeaseReleaseOutcome Release(int[] releases)
	{
		releases[0]++;
		return new LeaseReleaseOutcome(LeaseReleaseKind.Released, CheatEngineHostEffect.Completed);
	}
}
