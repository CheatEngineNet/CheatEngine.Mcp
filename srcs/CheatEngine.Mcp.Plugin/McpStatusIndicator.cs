using CheatEngine.Client;
using CheatEngine.Mcp.Hosting.Configuration;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CheatEngine.Mcp.Plugin;

/// <summary>Shows this CE host's backend state in a CE-owned, session-long menu item.</summary>
internal sealed partial class McpStatusIndicator(
	IOptions<McpDiscoveryOptions> discovery,
	PluginLog log,
	ILogger<McpStatusIndicator> logger)
{
	/// <summary>Shows a state; a failure is logged, never thrown, so it cannot change the lifecycle outcome.</summary>
	internal void Report(ICheatEngineClient client, string state, string? endpoint = null)
	{
		try
		{
			string details = Describe(state, discovery.Value.InstanceName, log, endpoint);
			bool updated = state == "Disabled" ? ShowDisabled(client, details) : Show(client, state, details);
			if (updated)
			{
				LogStatusShown(logger, state);
			}
		}
		catch (Exception exception)
		{
			LogStatusFailed(logger, exception, state);
		}
	}

	internal static bool Show(ICheatEngineClient client, string state, string details)
	{
		return LuaToolRuntime.ShowPluginStatus(client, state, details, false);
	}

	internal static bool ShowDisabled(ICheatEngineClient client, string details)
	{
		return LuaToolRuntime.ShowPluginStatus(client, "Disabled", details, true);
	}

	internal static string Describe(string state, string instanceName, PluginLog log, string? endpoint = null)
	{
		return $"CheatEngine.Mcp: {state}\n\nInstance: {instanceName}\n"
			   + (endpoint is null ? string.Empty : $"Listening at: {endpoint}\n")
			   + $"\n{state switch
			   {
				   "Enabled" => "The CE backend is ready for MCP requests. Your AI client connects through CheatEngine.Mcp.Gateway.exe.",
				   "Disabled" => "This CE instance is no longer accepting MCP requests. Enable the plugin in Edit > Settings > Plugins to start it again.",
				   "Start failed" => "The MCP server could not start. Check the log for the startup error, then enable the plugin again.",
				   _ => "The MCP server is starting."
			   }}\n\nLog: {log.LogFilePath}";
	}

	[LoggerMessage(Level = LogLevel.Information, Message = "MCP status indicator: {State}.")]
	private static partial void LogStatusShown(ILogger logger, string state);

	[LoggerMessage(Level = LogLevel.Warning, Message = "Could not update the MCP status indicator to {State}.")]
	private static partial void LogStatusFailed(ILogger logger, Exception exception, string state);
}
