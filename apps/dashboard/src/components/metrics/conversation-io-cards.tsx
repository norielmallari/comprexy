/**
 * Conversation I/O token strip — Raw input, compressed Input, and Output totals.
 */

import { formatNumber } from "@/lib/utils";

import { MetricCard } from "./metric-card";

interface ConversationIoCardsProps {
  totalCompressedPromptTokens: number | null;
  totalCompletionTokens: number | null;
  /** Omit to hide Raw; pass null to show Raw with em dash. */
  totalRawInputTokensEstimated?: number | null;
}

function formatTokenValue(value: number | null): string {
  return value !== null ? formatNumber(value) : "—";
}

export function ConversationIoCards({
  totalCompressedPromptTokens,
  totalCompletionTokens,
  totalRawInputTokensEstimated,
}: ConversationIoCardsProps) {
  const showRaw = totalRawInputTokensEstimated !== undefined;

  return (
    <div
      className="grid w-full grid-cols-1 gap-2 sm:grid-cols-2 lg:grid-cols-3"
      data-testid="conversation-io-cards"
    >
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
    </div>
  );
}
