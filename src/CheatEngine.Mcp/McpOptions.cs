using System.Globalization;

using CheatEngine.Mcp.Instances;

using Microsoft.Extensions.Configuration;

namespace CheatEngine.Mcp;

public sealed class McpOptions
{
	public string Host { get; set; } = "127.0.0.1";
	public int Port
	{
		get; set;
	}
	public string ServerName { get; set; } = "CheatEngine.Mcp";
	public string InstanceName { get; set; } = $"Cheat Engine {Environment.ProcessId}";
	public string InstanceDirectory { get; set; } = InstanceRegistry.DefaultDirectory;
	public bool EnableUnsafeLua { get; set; } = true;
	public bool EnableAutoAssembler { get; set; } = true;

	public string BaseUrl => new UriBuilder("http", Host, Port).Uri.GetLeftPart(UriPartial.Authority);

	public static string ConfigurationDirectory => ResolveConfigurationDirectory(Environment.GetEnvironmentVariable("MCP_DATA_DIRECTORY"));

	internal static string ResolveConfigurationDirectory(string? directory)
	{
		if (directory is null)
		{
			return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CheatEngine.Mcp");
		}
		if (!Path.IsPathFullyQualified(directory))
		{
			throw new ArgumentException("MCP_DATA_DIRECTORY must be an absolute directory.", nameof(directory));
		}
		return Path.GetFullPath(directory);
	}

	internal static McpOptions Load(ConfigurationManager configuration, string pluginDirectory,
		string configurationDirectory, Func<string, string?> environment)
	{
		configuration.SetBasePath(pluginDirectory).AddJsonFile("appsettings.json", optional: true, reloadOnChange: false);
		string settingsPath = Path.Combine(configurationDirectory, "appsettings.json");
		configuration.AddJsonFile(settingsPath, optional: true, reloadOnChange: false);
		McpOptions options = new McpOptions();
		configuration.GetSection("Mcp").Bind(options);
		options.Host = environment("MCP_HOST") ?? options.Host;
		options.InstanceName = environment("MCP_INSTANCE_NAME") ?? options.InstanceName;
		options.InstanceDirectory = environment("MCP_INSTANCE_DIRECTORY") ?? options.InstanceDirectory;
		string? port = environment("MCP_PORT");
		if (port is not null)
		{
			options.Port = int.Parse(port, CultureInfo.InvariantCulture);
		}
		options.Validate();
		return options;
	}

	internal void Validate()
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(Host);
		ArgumentException.ThrowIfNullOrWhiteSpace(ServerName);
		ArgumentException.ThrowIfNullOrWhiteSpace(InstanceName);
		if (Host != "127.0.0.1")
		{
			throw new ArgumentException("Mcp:Host must be 127.0.0.1; the gateway connects to private local backends.");
		}
		if (Port is < 0 or > 65535)
		{
			throw new InvalidOperationException("Mcp:Port must be between 0 and 65535 (0 selects a free port).");
		}
		if (InstanceName.Length > 128 || !Path.IsPathFullyQualified(InstanceDirectory))
		{
			throw new ArgumentException("Mcp:InstanceName must be at most 128 characters and InstanceDirectory must be absolute.");
		}
	}
}
