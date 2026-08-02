using System.Threading.Channels;
using Comprexy.Application.Abstractions;
using Comprexy.Application.Configuration;
using Microsoft.Extensions.Options;

namespace Comprexy.Application.Services.ToolIr;

public sealed class ToolIrShapeLearnQueue : IToolIrShapeLearnQueue
{
    private readonly Channel<ToolIrShapeLearnJob> _channel;

    public ToolIrShapeLearnQueue(IOptions<ToolSchemaOptions> options)
    {
        var capacity = Math.Max(1, options.Value.ResultShape.LearnQueueCapacity);
        _channel = Channel.CreateBounded<ToolIrShapeLearnJob>(new BoundedChannelOptions(capacity)
        {
            FullMode = BoundedChannelFullMode.DropWrite,
            SingleReader = true,
            SingleWriter = false
        });
    }

    public bool TryEnqueue(ToolIrShapeLearnJob job) => _channel.Writer.TryWrite(job);

    public async IAsyncEnumerable<ToolIrShapeLearnJob> ReadAllAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var job in _channel.Reader.ReadAllAsync(cancellationToken))
        {
            yield return job;
        }
    }
}
