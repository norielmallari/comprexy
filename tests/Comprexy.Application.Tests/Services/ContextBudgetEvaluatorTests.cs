using Comprexy.Application.Configuration;
using Comprexy.Application.Models;
using Comprexy.Application.Services;
using Microsoft.Extensions.Options;

namespace Comprexy.Application.Tests.Services;

public class ContextBudgetEvaluatorTests
{
    private static ContextBudgetEvaluator CreateEvaluator(int soft = 100)
    {
        var options = Options.Create(new ContextPolicyOptions
        {
            SoftLimitTokens = soft
        });

        return new ContextBudgetEvaluator(options);
    }

    [Fact]
    public void Evaluate_BelowSoftLimit_ReturnsForwardImmediate()
    {
        var evaluator = CreateEvaluator();

        var decision = evaluator.Evaluate(50);

        Assert.Equal(ContextBudgetDecision.ForwardImmediate, decision);
    }

    [Fact]
    public void Evaluate_AtSoftLimit_ReturnsForwardImmediate()
    {
        var evaluator = CreateEvaluator(soft: 100);

        var decision = evaluator.Evaluate(100);

        Assert.Equal(ContextBudgetDecision.ForwardImmediate, decision);
    }

    [Fact]
    public void Evaluate_AboveSoftLimit_ReturnsHighPriorityCompression()
    {
        var evaluator = CreateEvaluator(soft: 100);

        var decision = evaluator.Evaluate(150);

        Assert.Equal(ContextBudgetDecision.ForwardWithHighPriorityCompression, decision);
    }

    [Fact]
    public void Evaluate_FarAboveSoftLimit_ReturnsHighPriorityCompression()
    {
        var evaluator = CreateEvaluator(soft: 100);

        var decision = evaluator.Evaluate(10_000);

        Assert.Equal(ContextBudgetDecision.ForwardWithHighPriorityCompression, decision);
    }
}
