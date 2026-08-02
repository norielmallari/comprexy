/**
 * Telemetry mode: one conversation, one chart, separated I/O cards.
 */

'use client';

import { BarChart } from '@/components/charts';
import { Select } from '@/components/ui/select';
import { useTelemetryPresentation } from '@/lib/api/benchmarks';
import { useConversations } from '@/lib/queries/use-conversations';
import { useTurnMetrics } from '@/lib/queries/use-turns';
import { transformTurnsToChartData, truncateConversationId } from '@/lib/utils';
import type { BenchmarkCostRates, BenchmarkModelKind } from '@/types/api';

import { BenchmarkCaveats } from './benchmark-caveats';
import { TelemetryTimingCard } from './comparison-summary-cards';
import { IoTotalsCards } from './io-totals-cards';

interface TelemetryPanelProps {
  conversationId: string | null;
  onConversationChange: (id: string | null) => void;
  rates: BenchmarkCostRates;
  modelKind: BenchmarkModelKind;
}

export function TelemetryPanel({
  conversationId,
  onConversationChange,
  rates,
  modelKind,
}: TelemetryPanelProps) {
  const { data: conversations, isLoading: conversationsLoading } = useConversations();
  const { data: turns, isLoading: turnsLoading } = useTurnMetrics(conversationId);
  const { data: presentation, isLoading: presentationLoading } = useTelemetryPresentation(
    conversationId,
    rates,
    modelKind,
  );

  const chartData = turns ? transformTurnsToChartData(turns) : [];

  return (
    <div className="space-y-4" data-testid="telemetry-panel">
      <Select
        label="Operator DB conversation"
        options={
          conversations?.map((c) => ({
            label: truncateConversationId(c.conversationId),
            value: c.conversationId,
          })) ?? []
        }
        value={conversationId ?? 'none'}
        placeholder={conversationsLoading ? 'Loading…' : 'Select conversation'}
        onChange={(value) =>
          onConversationChange(value === 'none' ? null : value)
        }
        className="w-56"
        disabled={conversationsLoading}
      />

      {presentation && (
        <IoTotalsCards totals={presentation.totals} />
      )}

      {presentation && (
        <TelemetryTimingCard totals={presentation.totals} cost={presentation.cost} />
      )}

      {(turnsLoading || presentationLoading) && (
        <p className="text-sm text-slate-500">Loading metrics…</p>
      )}

      {chartData.length > 0 && (
        <BarChart data={chartData} isLoading={turnsLoading} />
      )}

      {!conversationId && (
        <p className="text-sm text-slate-500">
          Select a conversation to view telemetry totals and chart.
        </p>
      )}

      <BenchmarkCaveats caveats={[]} />
    </div>
  );
}
