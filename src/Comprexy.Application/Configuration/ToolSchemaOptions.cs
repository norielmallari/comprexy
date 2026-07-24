using Comprexy.Domain.Enums;

namespace Comprexy.Application.Configuration;

public class ToolSchemaOptions
{
    public const string SectionName = "ToolSchema";

    public ToolSchemaMode Mode { get; set; } = ToolSchemaMode.CompactIndex;

    public int MinToolCountToActivate { get; set; } = 1;

    public int MaxHydrateRoundsPerRequest { get; set; } = 8;

    public bool SkipRefetchIfHydrated { get; set; } = true;

    public string InstructionFile { get; set; } = "Prompts/tool-schema.md";
}
