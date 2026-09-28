using Microsoft.Extensions.Options;

namespace CheatEngine.Mcp.Core.Composition;

/// <summary>
///     Fails the resolution of a manifest that declares its server instructions more than once, so a second primitive
///     project can never replace the first one's text silently.
/// </summary>
internal sealed class CheatEngineMcpManifestValidator : IValidateOptions<CheatEngineMcpPrimitiveOptions>
{
	public ValidateOptionsResult Validate(string? name, CheatEngineMcpPrimitiveOptions options)
	{
		ArgumentNullException.ThrowIfNull(options);
		return options.InstructionDeclarations > 1
			? ValidateOptionsResult.Fail(
				$"The composition declares server instructions {options.InstructionDeclarations} times; declare them once.")
			: ValidateOptionsResult.Success;
	}
}
