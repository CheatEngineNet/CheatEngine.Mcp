using System.ComponentModel.DataAnnotations;
using System.Globalization;

using Microsoft.Extensions.Options;

namespace CheatEngine.Mcp.Core.Files;

/// <summary>
///     Validates <see cref="McpFileOptions" /> with generated, reflection-free code. The composition root runs it when it
///     reads the section, and <see cref="McpFilePaths" /> runs it again on construction, so an invalid root fails the
///     enable either way.
/// </summary>
[OptionsValidator]
public sealed partial class McpFileOptionsValidator : IValidateOptions<McpFileOptions>
{
	/// <summary>Throws the options pipeline's exception when <paramref name="options" /> is invalid.</summary>
	/// <param name="options">The options to check.</param>
	/// <returns>The same options.</returns>
	/// <exception cref="OptionsValidationException">A root is blank, relative, a UNC or device path, or duplicated.</exception>
	public McpFileOptions ThrowIfInvalid(McpFileOptions options)
	{
		ArgumentNullException.ThrowIfNull(options);
		ValidateOptionsResult result = Validate(Options.DefaultName, options);
		return result.Failed
			? throw new OptionsValidationException(Options.DefaultName, typeof(McpFileOptions), result.Failures)
			: options;
	}
}

/// <summary>
///     Accepts a list of absolute local directories: each entry must pass the host path rules (drive letter, no UNC or
///     device path, no alternate data stream, no device name) and appear once.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
internal sealed class LocalDirectoriesAttribute : ValidationAttribute
{
	protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
	{
		if (value is null)
		{
			return ValidationResult.Success;
		}

		if (value is not string[] roots)
		{
			return new ValidationResult($"{McpFileOptions.SectionName}:AllowedRoots must be a list of directories.");
		}

		HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
		for (int index = 0; index < roots.Length; index++)
		{
			string key = string.Create(CultureInfo.InvariantCulture,
				$"{McpFileOptions.SectionName}:AllowedRoots:{index}");
			string? problem = McpPathRules.FindFormProblem(roots[index]);
			if (problem is not null)
			{
				return new ValidationResult(
					$"{key} {problem} Each root must be an absolute local directory such as C:\\CheatEngine\\Files.");
			}

			if (!seen.Add(McpPathRules.WithSeparator(Path.GetFullPath(roots[index]))))
			{
				return new ValidationResult($"{key} repeats an earlier root.");
			}
		}

		return ValidationResult.Success;
	}
}
