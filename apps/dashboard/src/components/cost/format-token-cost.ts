/**
 * Presentation cost helpers — USD `$` beside token counts (not billing).
 */

import type { CostModelDto } from '@/types/api';

export const COST_DISCLAIMER =
  'Rates are operator assumptions for comparison only — not billing or guaranteed savings.';

export const LOCAL_COST_DISCLAIMER =
  'Local mode shows token totals without USD estimates. Select a priced catalog model to show $ beside tokens.';

export const COST_MODEL_STORAGE_KEY = 'comprexy.selectedCostModelKey';

export const DEFAULT_COST_MODEL_KEY = 'local';

/** True when catalog rates are both zero (Local presentation — tokens only). */
export function isZeroRateModel(model: Pick<CostModelDto, 'inputUsdPer1M' | 'outputUsdPer1M'> | null | undefined): boolean {
  if (!model) {
    return true;
  }
  return Number(model.inputUsdPer1M) === 0 && Number(model.outputUsdPer1M) === 0;
}

/**
 * Estimate USD for a token count at a per-1M rate.
 * Returns null when tokens are null/non-finite, rate is zero, or tokens are zero.
 */
export function estimateUsdForTokens(
  tokens: number | null | undefined,
  usdPer1M: number,
): number | null {
  if (tokens === null || tokens === undefined || !Number.isFinite(tokens) || tokens === 0) {
    return null;
  }
  if (!Number.isFinite(usdPer1M) || usdPer1M === 0) {
    return null;
  }
  return (tokens / 1_000_000) * usdPer1M;
}

/** Format a USD amount for compact overlay beside tokens (e.g. `$0.0045`). */
export function formatUsd(amount: number): string {
  if (!Number.isFinite(amount)) {
    return '';
  }
  const abs = Math.abs(amount);
  if (abs !== 0 && abs < 0.0001) {
    return `$${amount.toExponential(2)}`;
  }
  if (abs < 1) {
    return `$${amount.toFixed(4)}`;
  }
  return `$${amount.toFixed(2)}`;
}

export type TokenCostChannel = 'input' | 'output';

/**
 * Build a `$…` overlay string for a token count, or null when Local / zero / unset.
 */
export function formatTokenCostOverlay(
  tokens: number | null | undefined,
  model: Pick<CostModelDto, 'inputUsdPer1M' | 'outputUsdPer1M'> | null | undefined,
  channel: TokenCostChannel,
): string | null {
  if (!model || isZeroRateModel(model)) {
    return null;
  }
  const rate = channel === 'input' ? Number(model.inputUsdPer1M) : Number(model.outputUsdPer1M);
  const usd = estimateUsdForTokens(tokens, rate);
  if (usd === null) {
    return null;
  }
  return formatUsd(usd);
}
