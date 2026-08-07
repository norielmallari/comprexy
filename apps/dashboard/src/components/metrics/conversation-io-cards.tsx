/**
 * Conversation I/O token strip — Raw input (NativeRaw), compressed Input, Output, and
 * optional Virtual Tools / native-wire channel secondary metric.
 * Shows presentation `$` beside tokens when a non-zero catalog model is selected.
 */

'use client';

import { formatTokenCostOverlay } from '@/components/cost/format-token-cost';
import { VIRTUAL_TOOLS_CHANNEL_LABEL } from '@/lib/constants';
import { useCostModels } from '@/lib/queries/use-cost-models';
import { useDashboardStore } from '@/lib/store/dashboard-store';
import { formatNumber } from '@/lib/utils';

import { MetricCard } from './metric-card';

interface ConversationIoCardsProps {
  totalCompressedPromptTokens: number | null;
  totalCompletionTokens: number | null;
  /** Omit to hide Raw; pass null to show Raw with em dash. Stays NativeRaw. */
  totalRawInputTokensEstimated?: number | null;
  /**
   * Optional VT / native-wire secondary total (NativeRaw − IrFull).
   * Omit to hide; pass null to show em dash. Do not confuse with Raw input tokens.
   */
  totalVirtualToolsTokensSaved?: number | null;
}

interface VirtualToolsChannelCardProps {
  totalVirtualToolsTokensSaved: number | null;
}

function formatTokenValue(value: number | null): string {
  return value !== null ? formatNumber(value) : '—';
}

function useSelectedCostModel() {
  const selectedCostModelKey = useDashboardStore((s) => s.selectedCostModelKey);
  const { data: models } = useCostModels();
  return models?.find((m) => m.modelKey === selectedCostModelKey) ?? null;
}

export function VirtualToolsChannelCard({
  totalVirtualToolsTokensSaved,
}: VirtualToolsChannelCardProps) {
  const model = useSelectedCostModel();

  return (
    <MetricCard
      title={VIRTUAL_TOOLS_CHANNEL_LABEL}
      value={formatTokenValue(totalVirtualToolsTokensSaved)}
      unit="tokens"
      costOverlay={formatTokenCostOverlay(totalVirtualToolsTokensSaved, model, 'input')}
    />
  );
}

export function ConversationIoCards({
  totalCompressedPromptTokens,
  totalCompletionTokens,
  totalRawInputTokensEstimated,
  totalVirtualToolsTokensSaved,
}: ConversationIoCardsProps) {
  const model = useSelectedCostModel();

  const showRaw = totalRawInputTokensEstimated !== undefined;
  const showVt = totalVirtualToolsTokensSaved !== undefined;
  const colClass = showVt
    ? 'grid w-full grid-cols-1 gap-2 sm:grid-cols-2 lg:grid-cols-4'
    : 'grid w-full grid-cols-1 gap-2 sm:grid-cols-2 lg:grid-cols-3';

  return (
    <div className={colClass} data-testid="conversation-io-cards">
      {showRaw && (
        <MetricCard
          title="Raw input tokens"
          value={formatTokenValue(totalRawInputTokensEstimated ?? null)}
          unit="tokens"
          costOverlay={formatTokenCostOverlay(totalRawInputTokensEstimated, model, 'input')}
        />
      )}
      <MetricCard
        title="Input tokens"
        value={formatTokenValue(totalCompressedPromptTokens)}
        unit="tokens"
        costOverlay={formatTokenCostOverlay(totalCompressedPromptTokens, model, 'input')}
      />
      <MetricCard
        title="Output tokens"
        value={formatTokenValue(totalCompletionTokens)}
        unit="tokens"
        costOverlay={formatTokenCostOverlay(totalCompletionTokens, model, 'output')}
      />
      {showVt && (
        <VirtualToolsChannelCard
          totalVirtualToolsTokensSaved={totalVirtualToolsTokensSaved ?? null}
        />
      )}
    </div>
  );
}
