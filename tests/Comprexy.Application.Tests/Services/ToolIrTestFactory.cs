using Comprexy.Application.Abstractions;
using Comprexy.Application.Configuration;
using Comprexy.Application.Services.ToolIr;
using Microsoft.Extensions.Options;

namespace Comprexy.Application.Tests.Services;

internal static class ToolIrTestFactory
{
    public static ToolIrResultShapeStore CreateShapeStore(ToolSchemaOptions? options = null) =>
        new(Options.Create(options ?? new ToolSchemaOptions()));

    public static IToolIrShapeLearnQueue CreateLearnQueue(ToolSchemaOptions? options = null) =>
        new ToolIrShapeLearnQueue(Options.Create(options ?? new ToolSchemaOptions()));

    public static ToolIrResultDistiller CreateDistiller(
        IOptions<ToolSchemaOptions> options,
        ToolIrFileBodyCache cache,
        ToolIrResultShapeStore? store = null,
        IToolIrShapeLearnQueue? queue = null) =>
        new(
            options,
            cache,
            store ?? CreateShapeStore(options.Value),
            queue ?? CreateLearnQueue(options.Value));
}
