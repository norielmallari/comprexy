using Comprexy.Application.Configuration;
using Comprexy.Application.Models;
using Microsoft.Extensions.Options;

namespace Comprexy.Application.Services;

/// <summary>
/// Decides how to handle an outgoing request based on its estimated token count relative to the
/// configured soft and hard context limits.
/// </summary>
public class ContextBudgetEvaluator
{
    private readonly ContextPolicyOptions _policy;

    public ContextBudgetEvaluator(IOptions<ContextPolicyOptions> policy)
    {
        _policy = policy.Value;
    }

    public ContextBudgetDecision Evaluate(int estimatedTokens)
    {
        if (estimatedTokens >= _policy.HardLimitTokens)
        {
            return ContextBudgetDecision.EmergencyCompressionRequired;
        }

        if (estimatedTokens > _policy.SoftLimitTokens)
        {
            return ContextBudgetDecision.ForwardWithHighPriorityCompression;
        }

        return ContextBudgetDecision.ForwardImmediate;
    }
}
