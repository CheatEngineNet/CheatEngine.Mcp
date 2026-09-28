using System.ComponentModel.DataAnnotations;

namespace CheatEngine.Mcp.Hosting.Configuration;

/// <summary>The loopback listener of one activation's backend, bound from the <c>Mcp</c> section.</summary>
public sealed class McpBackendOptions
{
	/// <summary>The configuration section that holds the MCP settings.</summary>
	public const string SectionName = "Mcp";

	/// <summary>The only address a backend may bind: the gateway connects to private local backends.</summary>
	public const string LoopbackHost = "127.0.0.1";

	/// <summary>The listening address; always <see cref="LoopbackHost" />.</summary>
	[LoopbackHost(ErrorMessage = "Mcp:Host must be 127.0.0.1; the gateway connects to private local backends.")]
	public string Host
	{
		get;
		set;
	} = LoopbackHost;

	/// <summary>The listening port; 0 selects a free port.</summary>
	[ListenPort(ErrorMessage = "Mcp:Port must be between 0 and 65535 (0 selects a free port).")]
	public int Port
	{
		get;
		set;
	}

	/// <summary>The name the backend advertises as <c>serverInfo.name</c>.</summary>
	[Required(ErrorMessage = "Mcp:ServerName is required.")]
	public string ServerName
	{
		get;
		set;
	} = "CheatEngine.Mcp";

	/// <summary>The listener address as an HTTP base URL.</summary>
	public string BaseUrl => new UriBuilder(Uri.UriSchemeHttp, Host, Port).Uri.GetLeftPart(UriPartial.Authority);
}
