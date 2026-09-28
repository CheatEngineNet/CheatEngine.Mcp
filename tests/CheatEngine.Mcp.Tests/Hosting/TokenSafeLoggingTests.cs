using CheatEngine.Mcp.Tests.Support;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CheatEngine.Mcp.Tests.Hosting;

public sealed class TokenSafeLoggingTests
{
	[Fact]
	public void Floor_TraceDefaultAndLoweredCategories_HoldsGuardedCategoriesAtInformation()
	{
		using ServiceProvider services = Create(static logging => logging
			.SetMinimumLevel(LogLevel.Trace)
			.AddFilter("ModelContextProtocol.Server", LogLevel.Trace)
			.AddFilter<LogCapture>(null, LogLevel.Trace)
			.AddFilter<LogCapture>("System.Net.Http.HttpClient", LogLevel.Debug));
		ILoggerFactory loggers = services.GetRequiredService<ILoggerFactory>();

		foreach (string category in new[]
				 {
					 "ModelContextProtocol.Server.McpServer", "ModelContextProtocol.Client.HttpClientTransport",
					 "Microsoft.AspNetCore.Server.Kestrel", "System.Net.Http.HttpClient.Default"
				 })
		{
			ILogger logger = loggers.CreateLogger(category);
			Assert.False(logger.IsEnabled(LogLevel.Debug), category);
			Assert.True(logger.IsEnabled(LogLevel.Information), category);
		}

		Assert.True(loggers.CreateLogger("CheatEngine.Mcp.Hosting.Gateway.GatewayRouter").IsEnabled(LogLevel.Trace));
	}

	[Fact]
	public void Floor_WarningDefault_KeepsTheQuieterLevel()
	{
		using ServiceProvider services = Create(static logging => logging.AddFilter(null, LogLevel.Warning));

		ILogger logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("ModelContextProtocol.Server");

		Assert.False(logger.IsEnabled(LogLevel.Information));
		Assert.True(logger.IsEnabled(LogLevel.Warning));
	}

	private static ServiceProvider Create(Action<ILoggingBuilder> configure)
	{
		ServiceCollection services = new();
		services.AddLogging(logging =>
		{
			logging.AddProvider(new LogCapture());
			configure(logging);
			logging.AddTokenSafeFloor();
		});
		return services.BuildServiceProvider();
	}
}
