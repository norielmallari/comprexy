using Comprexy.Application.Abstractions;

namespace Comprexy.Application.Services;

public sealed class UpstreamActivityGate : IUpstreamActivityGate
{
    private readonly TimeProvider _timeProvider;
    private readonly object _sync = new();
    private int _busyCount;
    private CancellationTokenSource _preempt = new();
    private TaskCompletionSource _idle;

    public UpstreamActivityGate(TimeProvider timeProvider)
    {
        _timeProvider = timeProvider;
        _idle = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _idle.TrySetResult();
    }

    public bool IsBusy
    {
        get
        {
            lock (_sync)
            {
                return _busyCount > 0;
            }
        }
    }

    public CancellationToken PreemptToken
    {
        get
        {
            lock (_sync)
            {
                return _preempt.Token;
            }
        }
    }

    public IDisposable BeginClientDrivenCall()
    {
        CancellationTokenSource? toCancel = null;
        lock (_sync)
        {
            _busyCount++;
            if (_busyCount == 1)
            {
                _idle = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                toCancel = _preempt;
            }
        }

        toCancel?.Cancel();
        return new Lease(this);
    }

    public async Task WaitForIdleAsync(TimeSpan debounce, CancellationToken cancellationToken)
    {
        while (true)
        {
            Task idleTask;
            lock (_sync)
            {
                idleTask = _idle.Task;
            }

            await idleTask.WaitAsync(cancellationToken);

            await Task.Delay(debounce, _timeProvider, cancellationToken);

            lock (_sync)
            {
                if (_busyCount == 0)
                {
                    return;
                }
            }
        }
    }

    private void Release()
    {
        TaskCompletionSource? toComplete = null;
        lock (_sync)
        {
            if (_busyCount <= 0)
            {
                return;
            }

            _busyCount--;
            if (_busyCount == 0)
            {
                _preempt = new CancellationTokenSource();
                toComplete = _idle;
            }
        }

        toComplete?.TrySetResult();
    }

    private sealed class Lease : IDisposable
    {
        private UpstreamActivityGate? _gate;

        public Lease(UpstreamActivityGate gate) => _gate = gate;

        public void Dispose()
        {
            var gate = Interlocked.Exchange(ref _gate, null);
            gate?.Release();
        }
    }
}
