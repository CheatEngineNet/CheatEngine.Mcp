using System.Diagnostics;
using System.Runtime.Versioning;
using System.Text.Json;
using System.Text.Json.Nodes;

using CheatEngine.Mcp.Core.Contract;

namespace CheatEngine.Mcp.Tests.LiveQualification;

/// <summary>Checks only the read-only DBK/DBVM status query on private CE copies.</summary>
[SupportedOSPlatform("windows")]
internal static class LiveKernelStatusQualification
{
	private static readonly TimeSpan InventoryDeadline = TimeSpan.FromSeconds(20);

	internal static async Task RunAsync(LiveQualificationInputs inputs)
	{
		Assert.Equal(LiveQualificationScenario.KernelStatus, inputs.Scenario);
		DriverEntry[] driversBefore = await ReadKernelDriverInventoryAsync();
		Assert.DoesNotContain(driversBefore, driver => string.Equals(driver.State, "Running", StringComparison.OrdinalIgnoreCase));

		LiveSandboxSession? sandbox = null;
		LiveLifecycleSession? connection = null;
		Exception? failure = null;
		List<Exception> cleanup = [];
		try
		{
			sandbox = await LiveSandboxSession.StartAsync(inputs, LiveSandboxOptions.Smoke with
			{
				KernelStatusQualification = true
			});
			connection = await LiveLifecycleSession.ConnectAsync(sandbox);
			await connection.RequireInstancesAsync(sandbox.HostA.InstanceId, sandbox.HostB.InstanceId);

			LiveMcpInstanceClient instance = connection.Gateway.Bind(sandbox.HostA.InstanceId);
			JsonNode runtime = await CallAsync(instance, CheatEngineToolNames.RuntimeGetInfo);
			JsonNode gates = runtime["gates"] ?? throw new InvalidDataException("The private host omitted its capability gates.");
			Assert.True(gates["kernelAccess"]!.GetValue<bool>());
			Assert.False(gates["unsafeLua"]!.GetValue<bool>());
			Assert.False(gates["autoAssembler"]!.GetValue<bool>());
			Assert.False(gates["targetCodeExecution"]!.GetValue<bool>());
			sandbox.Record("kernel_status_qualification_gates", gates.DeepClone());

			JsonNode first = await CallAsync(instance, CheatEngineToolNames.KernelGetStatus);
			bool? dbk = OptionalBoolean(first["dbkInitialized"]);
			bool? dbvm = OptionalBoolean(first["dbvmInitialized"]);
			Assert.True(dbk.HasValue || dbvm.HasValue,
				"Cheat Engine returned no usable DBK or DBVM initialization state.");
			Assert.NotEqual(true, dbk);
			Assert.NotEqual(true, dbvm);
			JsonNode resources = await CallAsync(instance, CheatEngineToolNames.RuntimeListResources);
			JsonNode jobs = await CallAsync(instance, CheatEngineToolNames.RuntimeListJobs);
			Assert.Empty(resources["resources"]!.AsArray());
			Assert.Empty(jobs["jobs"]!.AsArray());
			sandbox.Record("all_tools_kernel_get_status", new
			{
				status = first.DeepClone(),
				availableStates = new
				{
					dbkInitialized = dbk.HasValue,
					dbvmInitialized = dbvm.HasValue
				},
				noActiveDbkOrDbvm = true,
				noOwnedResources = true,
				noOwnedJobs = true
			});

			JsonNode repeated = await CallAsync(instance, CheatEngineToolNames.KernelGetStatus);
			Assert.True(JsonNode.DeepEquals(first, repeated), "The read-only kernel status changed between identical queries.");
			await connection.RestartGatewayAsync();
			JsonNode afterGatewayRestart = await CallAsync(connection.Gateway.Bind(sandbox.HostA.InstanceId),
				CheatEngineToolNames.KernelGetStatus);
			Assert.True(JsonNode.DeepEquals(first, afterGatewayRestart), "Restarting the gateway changed the native kernel status.");
			JsonNode resourcesAfter = await CallAsync(connection.Gateway.Bind(sandbox.HostA.InstanceId), CheatEngineToolNames.RuntimeListResources);
			JsonNode jobsAfter = await CallAsync(connection.Gateway.Bind(sandbox.HostA.InstanceId), CheatEngineToolNames.RuntimeListJobs);
			Assert.Empty(resourcesAfter["resources"]!.AsArray());
			Assert.Empty(jobsAfter["jobs"]!.AsArray());

			DriverEntry[] driversAfter = await ReadKernelDriverInventoryAsync();
			Assert.Equal(SerializeDrivers(driversBefore), SerializeDrivers(driversAfter));
			sandbox.Record("live_kernel_status_cleanup", new
			{
				statusUnchangedAfterRepeatedQueryAndGatewayRestart = true,
				ownedResourcesAbsent = true,
				ownedJobsAbsent = true,
				kernelDriverInventoryUnchanged = true,
				matchingDriverCount = driversAfter.Length
			});
		}
		catch (Exception exception)
		{
			failure = exception;
			throw;
		}
		finally
		{
			await LiveLifecycleQualification.DisposeOrderedAsync(failure,
				connection is null ? null : connection.DisposeAsync,
				sandbox is null ? null : exceptions =>
				{
					sandbox.Record("kernel_status_connection_cleanup_failure",
						exceptions.Select(exception => exception.ToString()).ToArray());
					return ValueTask.CompletedTask;
				},
				sandbox is null ? null : () => { sandbox.MarkPassed(); return ValueTask.CompletedTask; },
				sandbox is null ? null : sandbox.DisposeAsync,
				cleanup);
			LiveLifecycleQualification.ThrowWithCleanup(failure, cleanup);
		}
	}

	private static async Task<DriverEntry[]> ReadKernelDriverInventoryAsync()
	{
		const string command = "$drivers = @(Get-CimInstance -ClassName Win32_SystemDriver -ErrorAction Stop | " +
			"Where-Object { $_.Name -match '(?i)dbk|dbvm' -or $_.PathName -match '(?i)dbk|dbvm' } | " +
			"ForEach-Object { [ordered]@{name=$_.Name; state=$_.State; path=$_.PathName} }); " +
			"ConvertTo-Json -InputObject $drivers -Compress -Depth 3";
		ProcessStartInfo startInfo = new("powershell.exe")
		{
			UseShellExecute = false,
			CreateNoWindow = true,
			RedirectStandardOutput = true,
			RedirectStandardError = true
		};
		foreach (string argument in new[] { "-NoProfile", "-NonInteractive", "-Command", command })
		{
			startInfo.ArgumentList.Add(argument);
		}

		using Process process = Process.Start(startInfo)
			?? throw new InvalidOperationException("Could not start the read-only kernel driver inventory query.");
		Task<string> outputTask = process.StandardOutput.ReadToEndAsync();
		Task<string> errorTask = process.StandardError.ReadToEndAsync();
		try
		{
			await process.WaitForExitAsync().WaitAsync(InventoryDeadline);
		}
		catch
		{
			if (!process.HasExited)
			{
				process.Kill(entireProcessTree: true);
				await process.WaitForExitAsync();
			}
			throw;
		}

		string output = await outputTask;
		string error = await errorTask;
		if (process.ExitCode != 0)
		{
			throw new InvalidOperationException($"The read-only kernel driver inventory failed: {error}");
		}

		using JsonDocument document = JsonDocument.Parse(output);
		return document.RootElement.EnumerateArray()
			.Select(driver => new DriverEntry(
				driver.GetProperty("name").GetString() ?? string.Empty,
				driver.GetProperty("state").GetString() ?? string.Empty,
				driver.GetProperty("path").GetString() ?? string.Empty))
			.OrderBy(driver => driver.Name, StringComparer.OrdinalIgnoreCase)
			.ThenBy(driver => driver.Path, StringComparer.OrdinalIgnoreCase)
			.ToArray();
	}

	private static string[] SerializeDrivers(IEnumerable<DriverEntry> drivers) =>
		drivers.Select(driver => $"{driver.Name}\0{driver.State}\0{driver.Path}").ToArray();

	private static bool? OptionalBoolean(JsonNode? node) => node is null ? null : node.GetValue<bool>();

	private static async Task<JsonNode> CallAsync(LiveMcpInstanceClient instance, string tool) =>
		await instance.CallToolAsync(tool) ?? throw new InvalidDataException($"{tool} returned no payload.");

	private readonly record struct DriverEntry(string Name, string State, string Path);
}
