using Comprexy.Application.Configuration;
using Comprexy.Application.Models;
using Microsoft.Extensions.Options;

namespace Comprexy.Application.Services;

/// <summary>
/// Decides soft-pressure eligibility from estimated token count relative to the soft limit.
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
        if (estimatedTokens > _policy.SoftLimitTokens)
        {
            return ContextBudgetDecision.ForwardWithHighPriorityCompression;
        }

        return ContextBudgetDecision.ForwardImmediate;
    }
}
