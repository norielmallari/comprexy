using System.Threading.Channels;
using Comprexy.Application.Configuration;
using Comprexy.Application.Services.ToolIr;
using Microsoft.Extensions.Options;

namespace Comprexy.Application.Abstractions;

public sealed record ToolIrShapeLearnJob(
    Guid ConversationId,
    string ClientToolName,
    string VirtualToolName,
    IReadOnlyList<ToolIrShapeFeatures> Samples);

public interface IToolIrShapeLearnQueue
{
    bool TryEnqueue(ToolIrShapeLearnJob job);

    IAsyncEnumerable<ToolIrShapeLearnJob> ReadAllAsync(CancellationToken cancellationToken);
}
