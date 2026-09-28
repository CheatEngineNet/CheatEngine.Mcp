using System.ComponentModel.DataAnnotations;

using Microsoft.Extensions.Options;

namespace CheatEngine.Mcp.Core.Execution;

/// <summary>Validates <see cref="McpExecutionOptions" /> with generated, reflection-free code.</summary>
[OptionsValidator]
internal sealed partial class McpExecutionOptionsValidator : IValidateOptions<McpExecutionOptions>;

/// <summary>Accepts an integer within inclusive bounds.</summary>
/// <remarks>The options generator's replacement for <see cref="RangeAttribute" /> ignores the configured message.</remarks>
/// <param name="minimum">The smallest accepted value.</param>
/// <param name="maximum">The largest accepted value.</param>
[AttributeUsage(AttributeTargets.Property)]
internal sealed class BoundedIntegerAttribute(int minimum, int maximum) : ValidationAttribute
{
	/// <summary>The smallest accepted value.</summary>
	public int Minimum
	{
		get;
	} = minimum;

	/// <summary>The largest accepted value.</summary>
	public int Maximum
	{
		get;
	} = maximum;

	public override bool IsValid(object? value)
	{
		return value is int number && number >= Minimum && number <= Maximum;
	}
}
