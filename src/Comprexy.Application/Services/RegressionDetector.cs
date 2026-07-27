using Comprexy.Application.Abstractions;
using Comprexy.Application.Models.Telemetry;

namespace Comprexy.Application.Services;

public sealed class RegressionDetector : IRegressionDetector
{
    public const double RelativeDropThreshold = 0.10d;

    public IReadOnlyList<SavingsRegressionDto> DetectSavingsRegressions(
        IReadOnlyList<ConversationTurnProjection> turnsOrderedByIndex)
    {
        if (turnsOrderedByIndex.Count < 2)
        {
            return [];
        }

        var regressions = new List<SavingsRegressionDto>();
        for (var i = 1; i < turnsOrderedByIndex.Count; i++)
        {
            var previous = turnsOrderedByIndex[i - 1];
            var current = turnsOrderedByIndex[i];
            if (previous.NetTokenSavingsRatio <= 0)
            {
                continue;
            }

            var relativeDrop =
                (previous.NetTokenSavingsRatio - current.NetTokenSavingsRatio) / previous.NetTokenSavingsRatio;
            if (relativeDrop > RelativeDropThreshold)
            {
                regressions.Add(new SavingsRegressionDto
                {
                    FromTurnIndex = previous.TurnIndex,
                    ToTurnIndex = current.TurnIndex,
                    FromSavingsRatio = previous.NetTokenSavingsRatio,
                    ToSavingsRatio = current.NetTokenSavingsRatio,
                    RelativeDrop = Math.Round(relativeDrop, 6)
                });
            }
        }

        return regressions;
    }
}
