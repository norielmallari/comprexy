/**
 * Conversation I/O token strip — Raw input (NativeRaw), compressed Input, Output, and
 * optional Virtual Tools / native-wire channel secondary metric.
 */

import { VIRTUAL_TOOLS_CHANNEL_LABEL } from "@/lib/constants";
import { formatNumber } from "@/lib/utils";

import { MetricCard } from "./metric-card";

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

function formatTokenValue(value: number | null): string {
  return value !== null ? formatNumber(value) : "—";
}

export function ConversationIoCards({
  totalCompressedPromptTokens,
  totalCompletionTokens,
  totalRawInputTokensEstimated,
  totalVirtualToolsTokensSaved,
}: ConversationIoCardsProps) {
  const showRaw = totalRawInputTokensEstimated !== undefined;
  const showVt = totalVirtualToolsTokensSaved !== undefined;
  const colClass = showVt
    ? "grid w-full grid-cols-1 gap-2 sm:grid-cols-2 lg:grid-cols-4"
    : "grid w-full grid-cols-1 gap-2 sm:grid-cols-2 lg:grid-cols-3";

  return (
    <div className={colClass} data-testid="conversation-io-cards">
      {showRaw && (
        <MetricCard
          title="Raw input tokens"
          value={formatTokenValue(totalRawInputTokensEstimated)}
          unit="tokens"
        />
      )}
      <MetricCard
        title="Input tokens"
        value={formatTokenValue(totalCompressedPromptTokens)}
        unit="tokens"
      />
      <MetricCard
        title="Output tokens"
        value={formatTokenValue(totalCompletionTokens)}
        unit="tokens"
      />
      {showVt && (
        <MetricCard
          title={VIRTUAL_TOOLS_CHANNEL_LABEL}
          value={formatTokenValue(totalVirtualToolsTokensSaved)}
          unit="tokens"
          description="NativeRaw − IR full; not tools-only; may be negative"
        />
      )}
    </div>
  );
}
