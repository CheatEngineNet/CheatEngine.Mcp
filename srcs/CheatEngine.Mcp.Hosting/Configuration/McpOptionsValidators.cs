using Microsoft.Extensions.Options;

namespace CheatEngine.Mcp.Hosting.Configuration;

/// <summary>Validates <see cref="McpBackendOptions" /> with generated, reflection-free code.</summary>
[OptionsValidator]
internal sealed partial class McpBackendOptionsValidator : IValidateOptions<McpBackendOptions>;

/// <summary>Validates <see cref="McpDiscoveryOptions" /> with generated, reflection-free code.</summary>
[OptionsValidator]
internal sealed partial class McpDiscoveryOptionsValidator : IValidateOptions<McpDiscoveryOptions>;

/// <summary>Applies a validator outside the options pipeline, with the pipeline's exception.</summary>
internal static class McpOptionsValidation
{
	internal static TOptions ThrowIfInvalid<TOptions>(IValidateOptions<TOptions> validator, TOptions options)
		where TOptions : class
	{
		ValidateOptionsResult result = validator.Validate(Options.DefaultName, options);
		return result.Failed
			? throw new OptionsValidationException(Options.DefaultName, typeof(TOptions), result.Failures)
			: options;
	}
}
