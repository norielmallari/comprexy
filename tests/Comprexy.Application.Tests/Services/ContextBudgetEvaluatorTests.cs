using Comprexy.Application.Configuration;
using Comprexy.Application.Models;
using Comprexy.Application.Services;
using Microsoft.Extensions.Options;

namespace Comprexy.Application.Tests.Services;

public class ContextBudgetEvaluatorTests
{
    private static ContextBudgetEvaluator CreateEvaluator(int soft = 100, int hard = 200)
    {
        var options = Options.Create(new ContextPolicyOptions
        {
            SoftLimitTokens = soft,
            HardLimitTokens = hard
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
        var evaluator = CreateEvaluator(soft: 100, hard: 200);

        var decision = evaluator.Evaluate(100);

        Assert.Equal(ContextBudgetDecision.ForwardImmediate, decision);
    }

    [Fact]
    public void Evaluate_BetweenSoftAndHardLimit_ReturnsHighPriorityCompression()
    {
        var evaluator = CreateEvaluator(soft: 100, hard: 200);

        var decision = evaluator.Evaluate(150);

        Assert.Equal(ContextBudgetDecision.ForwardWithHighPriorityCompression, decision);
    }

    [Fact]
    public void Evaluate_AtHardLimit_ReturnsEmergencyCompressionRequired()
    {
        var evaluator = CreateEvaluator(soft: 100, hard: 200);

        var decision = evaluator.Evaluate(200);

        Assert.Equal(ContextBudgetDecision.EmergencyCompressionRequired, decision);
    }

    [Fact]
    public void Evaluate_AboveHardLimit_ReturnsEmergencyCompressionRequired()
    {
        var evaluator = CreateEvaluator(soft: 100, hard: 200);

        var decision = evaluator.Evaluate(201);

        Assert.Equal(ContextBudgetDecision.EmergencyCompressionRequired, decision);
    }

    [Fact]
    public void Evaluate_JustBelowHardLimit_ReturnsHighPriorityCompression()
    {
        var evaluator = CreateEvaluator(soft: 100, hard: 200);

        var decision = evaluator.Evaluate(199);

        Assert.Equal(ContextBudgetDecision.ForwardWithHighPriorityCompression, decision);
    }
}
