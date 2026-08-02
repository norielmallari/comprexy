namespace Comprexy.Application.Abstractions;

/// <summary>
/// Tracks in-flight client-driven upstream LLM calls so the idle shape learner can wait and preempt.
/// </summary>
public interface IUpstreamActivityGate
{
    /// <summary>Increment busy and cancel the learner preempt token. Dispose to release.</summary>
    IDisposable BeginClientDrivenCall();

    bool IsBusy { get; }

    CancellationToken PreemptToken { get; }

    Task WaitForIdleAsync(TimeSpan debounce, CancellationToken cancellationToken);
}
