using System.Globalization;

using Microsoft.Extensions.Configuration;

namespace CheatEngine.Mcp.Hosting.Configuration;

/// <summary>Maps the documented <c>MCP_*</c> variables onto the <c>Mcp</c> section.</summary>
internal sealed class McpEnvironmentConfigurationSource(Func<string, string?> environment) : IConfigurationSource
{
	public IConfigurationProvider Build(IConfigurationBuilder builder)
	{
		return new McpEnvironmentConfigurationProvider(environment);
	}
}

/// <summary>Reads the <c>MCP_*</c> variables once; configuration never reloads within an activation.</summary>
internal sealed class McpEnvironmentConfigurationProvider(Func<string, string?> environment) : ConfigurationProvider
{
	public override void Load()
	{
		Dictionary<string, string?> data = new(StringComparer.OrdinalIgnoreCase);
		Map(data, "MCP_HOST", nameof(McpBackendOptions.Host));
		Map(data, "MCP_INSTANCE_NAME", nameof(McpDiscoveryOptions.InstanceName));
		Map(data, "MCP_INSTANCE_DIRECTORY", nameof(McpDiscoveryOptions.InstanceDirectory));
		string? port = environment("MCP_PORT");
		if (port is not null)
		{
			// Parse here so a malformed value names its source instead of silently binding another port.
			data[Key(nameof(McpBackendOptions.Port))] =
				int.TryParse(port, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedPort)
					? parsedPort.ToString(CultureInfo.InvariantCulture)
					: throw new FormatException("MCP_PORT must be an integer from 0 to 65535.");
		}

		Data = data;
	}

	private void Map(Dictionary<string, string?> data, string variable, string property)
	{
		string? value = environment(variable);
		if (value is not null)
		{
			data[Key(property)] = value;
		}
	}

	private static string Key(string property)
	{
		return ConfigurationPath.Combine(McpBackendOptions.SectionName, property);
	}
}
