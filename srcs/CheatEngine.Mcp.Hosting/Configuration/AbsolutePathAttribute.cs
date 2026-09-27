using System.ComponentModel.DataAnnotations;

namespace CheatEngine.Mcp.Hosting.Configuration;

/// <summary>Accepts only a fully qualified path, so every process resolves the same directory.</summary>
[AttributeUsage(AttributeTargets.Property)]
internal sealed class AbsolutePathAttribute : ValidationAttribute
{
	public override bool IsValid(object? value)
	{
		return value is string path && Path.IsPathFullyQualified(path);
	}
}
