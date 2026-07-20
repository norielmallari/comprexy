namespace Comprexy.Application.Models;

/// <summary>
/// Outcome of evaluating an outgoing request's estimated token count against the configured
/// <see cref="Configuration.ContextPolicyOptions"/> soft/hard limits.
/// </summary>
public enum ContextBudgetDecision
{
    /// <summary>Estimated tokens are within the soft limit; forward now, no post-response compression.</summary>
    ForwardImmediate,

    /// <summary>Estimated tokens are between the soft and hard limit; forward now, compact at high priority.</summary>
    ForwardWithHighPriorityCompression,

    /// <summary>Estimated tokens exceed the hard limit; synchronous emergency compression is required first.</summary>
    EmergencyCompressionRequired
}
