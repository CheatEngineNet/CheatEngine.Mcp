using CheatEngine.Mcp.Gateway;
using CheatEngine.Mcp.Instances;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

HostApplicationBuilder builder = Host.CreateApplicationBuilder();
builder.Logging.ClearProviders();
builder.Logging.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);
string directory = ResolveInstanceDirectory(args);
InstanceRegistry registry = new(directory);
GatewayServer gateway = new(registry);
Microsoft.Extensions.DependencyInjection.IMcpServerBuilder mcpServer = builder.Services.AddMcpServer(server =>
{
	server.ServerInfo = new()
	{
		Name = "CheatEngine.Mcp.Gateway",
		Version = typeof(Program).Assembly.GetName().Version?.ToString() ?? "2.0.0"
	};
})
.WithStdioServerTransport();

gateway.Configure(mcpServer);
await builder.Build().RunAsync();

static string ResolveInstanceDirectory(string[] arguments)
{
	string? directory = Environment.GetEnvironmentVariable("MCP_INSTANCE_DIRECTORY");
	for (int index = 0; index < arguments.Length; index++)
	{
		if (string.Equals(arguments[index], "--instance-directory", StringComparison.Ordinal))
		{
			if (++index >= arguments.Length)
			{
				throw new ArgumentException("--instance-directory requires an absolute directory path.");
			}
			directory = arguments[index];
		}
		else
		{
			throw new ArgumentException($"Unknown gateway argument '{arguments[index]}'.");
		}
	}
	if (string.IsNullOrWhiteSpace(directory))
	{
		return InstanceRegistry.DefaultDirectory;
	}
	return Path.IsPathFullyQualified(directory)
		? Path.GetFullPath(directory)
		: throw new ArgumentException("The instance directory must be an absolute path.", nameof(arguments));
}
