using Microsoft.Extensions.Options;

namespace Comprexy.Application.Configuration;

public sealed class ProviderOptionsValidator : IValidateOptions<ProviderOptions>
{
    public ValidateOptionsResult Validate(string? name, ProviderOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.Type))
        {
            return ValidateOptionsResult.Fail(
                $"Provider:Type is required. Only '{ProviderOptions.OpenAiCompatibleType}' is supported.");
        }

        if (!string.Equals(
                options.Type.Trim(),
                ProviderOptions.OpenAiCompatibleType,
                StringComparison.OrdinalIgnoreCase))
        {
            return ValidateOptionsResult.Fail(
                $"Provider:Type '{options.Type}' is not supported. Only '{ProviderOptions.OpenAiCompatibleType}' is supported.");
        }

        return ValidateOptionsResult.Success;
    }
}
