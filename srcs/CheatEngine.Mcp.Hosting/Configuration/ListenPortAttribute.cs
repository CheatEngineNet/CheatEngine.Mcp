using System.ComponentModel.DataAnnotations;
using System.Net;

namespace CheatEngine.Mcp.Hosting.Configuration;

/// <summary>Accepts a TCP port, where 0 selects a free port.</summary>
/// <remarks>The options generator's replacement for <see cref="RangeAttribute" /> ignores the configured message.</remarks>
[AttributeUsage(AttributeTargets.Property)]
internal sealed class ListenPortAttribute : ValidationAttribute
{
	public override bool IsValid(object? value)
	{
		return value is int and >= IPEndPoint.MinPort and <= IPEndPoint.MaxPort;
	}
}
