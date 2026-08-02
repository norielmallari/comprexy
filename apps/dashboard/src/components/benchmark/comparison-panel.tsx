/**
 * Comparison mode: baseline + compare IDs, dual charts with shared Y-scale.
 */

'use client';

import { BarChart } from '@/components/charts';
import { Select } from '@/components/ui/select';
import { useComparisonPresentation } from '@/lib/api/benchmarks';
import { computeSharedChartYMax } from '@/lib/chart-y-max';
import { useConversations } from '@/lib/queries/use-conversations';
import { useTurnMetrics } from '@/lib/queries/use-turns';
import { transformTurnsToChartData, truncateConversationId } from '@/lib/utils';
import type { BenchmarkCostRates, BenchmarkModelKind } from '@/types/api';

import { BenchmarkCaveats } from './benchmark-caveats';
import { ComparisonSummaryCards } from './comparison-summary-cards';
import { IoTotalsCards } from './io-totals-cards';

interface ComparisonPanelProps {
  baselineId: string | null;
  compareId: string | null;
  onBaselineChange: (id: string | null) => void;
  onCompareChange: (id: string | null) => void;
  rates: BenchmarkCostRates;
  modelKind: BenchmarkModelKind;
}

export function ComparisonPanel({
  baselineId,
  compareId,
  onBaselineChange,
  onCompareChange,
  rates,
  modelKind,
}: ComparisonPanelProps) {
  const { data: conversations, isLoading: conversationsLoading } = useConversations();
  const { data: baselineTurns, isLoading: baselineTurnsLoading } =
    useTurnMetrics(baselineId);
  const { data: compareTurns, isLoading: compareTurnsLoading } =
    useTurnMetrics(compareId);

  const { data: presentation, isLoading: presentationLoading } =
    useComparisonPresentation(baselineId, compareId, rates, modelKind);

  const baselineChart = baselineTurns ? transformTurnsToChartData(baselineTurns) : [];
  const compareChart = compareTurns ? transformTurnsToChartData(compareTurns) : [];
  const sharedMaxY = computeSharedChartYMax(baselineChart, compareChart);

  const options =
    conversations?.map((c) => ({
      label: truncateConversationId(c.conversationId),
      value: c.conversationId,
    })) ?? [];

  return (
    <div className="space-y-4" data-testid="comparison-panel">
      <div className="grid grid-cols-1 gap-4 lg:grid-cols-2">
        <div>
          <Select
            label="Baseline (top chart)"
            options={options}
            value={baselineId ?? 'none'}
            placeholder={conversationsLoading ? 'Loading…' : 'Baseline conversation'}
            onChange={(value) =>
              onBaselineChange(value === 'none' ? null : value)
            }
            className="w-full"
            disabled={conversationsLoading}
          />
        </div>
        <div>
          <Select
            label="Compare (bottom chart)"
            options={options}
            value={compareId ?? 'none'}
            placeholder={conversationsLoading ? 'Loading…' : 'Compare conversation'}
            onChange={(value) =>
              onCompareChange(value === 'none' ? null : value)
            }
            className="w-full"
            disabled={conversationsLoading}
          />
        </div>
      </div>

      {presentation && (
        <>
          <IoTotalsCards
            showComparison
            deltas={{
              input: presentation.totals.input,
              output: presentation.totals.output,
              overhead: presentation.totals.overhead,
              turnCount: presentation.totals.turnCount,
            }}
          />
          <ComparisonSummaryCards
            baseline={presentation.totals.baseline}
            compare={presentation.totals.compare}
            wallClockMs={presentation.totals.wallClockMs}
            proxyDurationMs={presentation.totals.proxyDurationMs}
            cost={presentation.cost}
          />
          <BenchmarkCaveats caveats={presentation.totals.caveats} />
        </>
      )}

      {(presentationLoading || baselineTurnsLoading || compareTurnsLoading) && (
        <p className="text-sm text-slate-500">Loading comparison…</p>
      )}

      {baselineId && baselineChart.length > 0 && (
        <BarChart
          data={baselineChart}
          sharedMaxY={sharedMaxY}
          title="Baseline — tokens by turn"
          compact
          testId="baseline-token-chart"
        />
      )}

      {compareId && compareChart.length > 0 && (
        <BarChart
          data={compareChart}
          sharedMaxY={sharedMaxY}
          title="Compare — tokens by turn"
          compact
          testId="compare-token-chart"
        />
      )}

      {(!baselineId || !compareId) && (
        <p className="text-sm text-slate-500">
          Pick two operator-DB conversation IDs to compare totals. Charts may differ in length; Y-axis
          is shared. No turn-index pairing across arms.
        </p>
      )}
    </div>
  );
}
