namespace Comprexy.Application.Models;

/// <summary>
/// Outcome of evaluating an outgoing request's estimated token count against the configured
/// <see cref="Configuration.ContextPolicyOptions"/> soft limit.
/// </summary>
public enum ContextBudgetDecision
{
    /// <summary>Estimated tokens are within the soft limit; forward now, no wrap-up eligibility.</summary>
    ForwardImmediate,

    /// <summary>Estimated tokens are above the soft limit; forward now; Inline wrap-up may run after the answer when eligible.</summary>
    ForwardWithHighPriorityCompression,

    /// <summary>Reserved.</summary>
    EmergencyCompressionRequired
}
