namespace Comprexy.Application.Abstractions;

/// <summary>
/// Process-local copy of the latest OperatorSettings JSON for options overlay.
/// </summary>
public interface IOperatorSettingsOverlay
{
    long Revision { get; }

    string SettingsJson { get; }

    /// <summary>
    /// Replaces overlay when revision advances. Returns true when callers should signal change tokens.
    /// </summary>
    bool TryUpdate(long revision, string settingsJson);
}
