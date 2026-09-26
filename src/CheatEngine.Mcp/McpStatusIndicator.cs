using CheatEngine.Client;
using CheatEngine.Mcp.Tools;

namespace CheatEngine.Mcp;

/// <summary>Shows this CE host's backend state in a CE-owned, session-long menu item.</summary>
internal static class McpStatusIndicator
{
	internal const string UpdateSource = """
		local menu = assert(getMainForm().Menu, 'The Cheat Engine main menu is unavailable.')
		local item = menu.findComponentByName('CheatEngineMcpStatus')
		if item == nil then
		  if a[3] then return false end
		  item = createMenuItem(menu)
		  item.Name = 'CheatEngineMcpStatus'
		  item.Tag = 0x4D4350
		  menu.Items.add(item)
		end
		assert(item.Tag == 0x4D4350, 'The MCP status menu name is already in use.')
		item.Caption = a[1]
		local details = a[2]
		item.OnClick = function() showMessage(details) end
		return item.Caption == a[1]
		""";

	internal static bool Show(ICheatEngineClient client, string state, string details) =>
		LuaToolRuntime.ShowPluginStatus(client, state, details, existingOnly: false);

	internal static bool ShowDisabled(ICheatEngineClient client, string details) =>
		LuaToolRuntime.ShowPluginStatus(client, "Disabled", details, existingOnly: true);

	internal static string Describe(string state, McpOptions options, PluginLog log, string? endpoint = null) =>
		$"CheatEngine.Mcp: {state}\n\nInstance: {options.InstanceName}\n"
		+ (endpoint is null ? string.Empty : $"Listening at: {endpoint}\n")
		+ $"\n{state switch
		{
			"Enabled" => "The CE backend is ready for MCP requests. Your AI client connects through CheatEngine.Mcp.Gateway.exe.",
			"Disabled" => "This CE instance is no longer accepting MCP requests. Enable the plugin in Edit > Settings > Plugins to start it again.",
			"Start failed" => "The MCP server could not start. Check the log for the startup error, then enable the plugin again.",
			_ => "The MCP server is starting."
		}}\n\nLog: {log.LogFilePath}";
}
