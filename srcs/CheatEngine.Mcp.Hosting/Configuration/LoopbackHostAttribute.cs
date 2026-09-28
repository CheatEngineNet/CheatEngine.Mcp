using System.ComponentModel.DataAnnotations;

namespace CheatEngine.Mcp.Hosting.Configuration;

/// <summary>Accepts only the literal loopback address <see cref="McpBackendOptions.LoopbackHost" />.</summary>
[AttributeUsage(AttributeTargets.Property)]
internal sealed class LoopbackHostAttribute : ValidationAttribute
{
	public override bool IsValid(object? value)
	{
		return value is string host && string.Equals(host, McpBackendOptions.LoopbackHost, StringComparison.Ordinal);
	}
}
