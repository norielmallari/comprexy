using Comprexy.Application.Configuration;
using Microsoft.Extensions.Options;

namespace Comprexy.Application.Tests.Configuration;

public class ProviderOptionsValidatorTests
{
    private readonly ProviderOptionsValidator _validator = new();

    [Theory]
    [InlineData("OpenAICompatible")]
    [InlineData("openaiCompatible")]
    [InlineData(" OPENAICOMPATIBLE ")]
    public void Validate_AcceptsOpenAiCompatible(string type)
    {
        var result = _validator.Validate(null, new ProviderOptions { Type = type });
        Assert.True(result.Succeeded);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("AzureOpenAI")]
    [InlineData("Anthropic")]
    public void Validate_RejectsUnsupportedOrMissingType(string? type)
    {
        var result = _validator.Validate(null, new ProviderOptions { Type = type! });
        Assert.True(result.Failed);
        Assert.Contains(ProviderOptions.OpenAiCompatibleType, result.FailureMessage, StringComparison.Ordinal);
    }
}
