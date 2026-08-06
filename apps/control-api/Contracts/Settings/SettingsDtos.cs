using Comprexy.Application.Models;

namespace Comprexy.ControlApi.Contracts.Settings;

public sealed class OperatorSettingsResponseDto
{
    public long Revision { get; init; }

    public OperatorMutableSettingsDto Settings { get; init; } = new();

    public DateTimeOffset UpdatedAt { get; init; }
}

public sealed class OperatorSettingsPutRequestDto
{
    public long Revision { get; init; }

    public OperatorMutableSettingsDto Settings { get; init; } = new();
}
