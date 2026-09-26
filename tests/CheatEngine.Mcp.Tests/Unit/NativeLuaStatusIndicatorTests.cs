using CheatEngine.Client;
using CheatEngine.Client.Dispatching;
using CheatEngine.Client.Results;
using CheatEngine.Mcp.Tools;

namespace CheatEngine.Mcp.Tests;

public sealed unsafe partial class NativeLuaToolRuntimeTests
{
	[Fact]
	public void McpStatus_EnableDisableReenable_UpdatesOneMenuAndItsDetails()
	{
		using RuntimeScope scope = CreateScope();
		InstallStatusMenu();
		ICheatEngineClient client = ClientTestDouble.Client();
		const string enabledDetails = "Instance: fox \"test\" \\ \u2603\nListening at: http://127.0.0.1:12345/";

		McpStatusIndicator.Show(client, "Enabled", enabledDetails);
		Assert.Equal("MCP: Enabled", ReadStatusCaption());
		InstallStubs("statusItem.OnClick()");
		Assert.Equal(enabledDetails, ReadGlobal("shownMessage"));

		// Lifecycle UI must work without Client Lua dispatch; this double exposes no Lua service.
		McpStatusIndicator.ShowDisabled(ClientTestDouble.Client(), "This instance is disabled.");
		Assert.Equal("MCP: Disabled", ReadStatusCaption());
		InstallStubs("statusItem.OnClick()");
		Assert.Equal("This instance is disabled.", ReadGlobal("shownMessage"));

		McpStatusIndicator.Show(client, "Enabled", "A new activation is ready.");
		Assert.Equal("MCP: Enabled", ReadStatusCaption());
		Assert.Equal(1L, ReadGlobal("createdMenus"));
		Assert.Equal(1L, ReadGlobal("addedMenus"));
		InstallStubs("statusItem.OnClick()");
		Assert.Equal("A new activation is ready.", ReadGlobal("shownMessage"));
	}

	[Fact]
	public void McpStatus_DisableWithoutIndicator_DoesNotCreateResources()
	{
		using RuntimeScope scope = CreateScope();
		InstallStatusMenu();

		McpStatusIndicator.ShowDisabled(ClientTestDouble.Client(), "Disabled");

		Assert.Equal(0L, ReadGlobal("createdMenus"));
		Assert.Equal(0L, ReadGlobal("addedMenus"));
		Assert.Null(ReadGlobal("statusItem"));
	}

	[Fact]
	public void McpStatus_ForeignMenuName_DoesNotOverwriteIt()
	{
		using RuntimeScope scope = CreateScope();
		InstallStatusMenu();
		InstallStubs("statusItem={Name='CheatEngineMcpStatus',Tag=77,Caption='Another plugin'}");

		Assert.Throws<CheatEngineOperationException>(() => McpStatusIndicator.Show(ClientTestDouble.Client(), "Enabled", "Ready"));
		Assert.Equal("Another plugin", ReadStatusCaption());
		Assert.Equal(0L, ReadGlobal("createdMenus"));
	}

	[Fact]
	public void McpStatus_ServerStartFails_KeepsFailureVisibleDuringRollback()
	{
		using RuntimeScope scope = CreateScope();
		InstallStatusMenu();
		using PluginLog log = new(Path.GetTempPath());
		McpOptions options = new()
		{
			Port = -1
		};
		using McpModule module = new(options, log);
		ICheatEngineClient client = ClientTestDouble.Client();

		InvalidOperationException failure = Assert.Throws<InvalidOperationException>(() => module.OnEnabled(client));
		Assert.Contains("Could not start the MCP server", failure.Message);
		Assert.Equal("MCP: Start failed", ReadStatusCaption());
		module.OnDisabling(client);
		Assert.Equal("MCP: Start failed", ReadStatusCaption());
		InstallStubs("statusItem.OnClick()");
		Assert.Contains(log.LogFilePath, Assert.IsType<string>(ReadGlobal("shownMessage")));
	}

	[Fact]
	public void McpStatus_OffMainThread_RefusesUiWork()
	{
		using RuntimeScope scope = CreateScope();
		InstallStatusMenu();
		ICheatEngineDispatcher dispatcher = ClientTestDouble.Create<ICheatEngineDispatcher>((_, _) => false);
		ICheatEngineClient client = ClientTestDouble.Create<ICheatEngineClient>((method, _) =>
			method.Name == "get_Dispatcher" ? dispatcher : throw new NotSupportedException());

		Assert.Throws<InvalidOperationException>(() => McpStatusIndicator.Show(client, "Enabled", "Ready"));
		Assert.Equal(0L, ReadGlobal("createdMenus"));
		Assert.Equal(0L, ReadGlobal("addedMenus"));
	}

	private static void InstallStatusMenu() => InstallStubs("""
		createdMenus=0; addedMenus=0; statusItem=nil; shownMessage=nil
		local menu={Items={}}
		menu.findComponentByName=function(name)
		  if statusItem and statusItem.Name==name then return statusItem end
		end
		menu.Items.add=function(item) addedMenus=addedMenus+1; statusItem=item end
		getMainForm=function() return {Menu=menu} end
		createMenuItem=function(owner) assert(owner==menu); createdMenus=createdMenus+1; return {} end
		showMessage=function(message) shownMessage=message end
		""");

	private static string ReadStatusCaption()
	{
		LuaToolRuntime.LuaToolOperation operation = new("status_caption", "return statusItem.Caption");
		Assert.True(operation.TryExecute(ActiveContext.Instance, out object? result, out CheatEngineFailure failure), failure.Message);
		return Assert.IsType<string>(result);
	}
}
