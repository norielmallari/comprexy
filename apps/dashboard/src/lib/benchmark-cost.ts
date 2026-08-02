/**
 * Cost model presets and helpers for the benchmark console.
 */

import type { BenchmarkCostRates, BenchmarkModelKind } from '@/types/api';

export const COST_DISCLAIMER =
  'Rates are operator assumptions for comparison only — not billing or guaranteed savings.';

export const LOCAL_COST_DISCLAIMER =
  'Local mode shows token and timing totals without USD estimates. Select USD to apply rate presets.';

/** Server-side defaults from BenchOrchestrationOptions (control-api appsettings). */
export const BENCHMARK_TIMEOUT_DEFAULTS = {
  completionTimeoutSeconds: 300,
  conversationTimeoutSeconds: 7200,
} as const;

export const DEFAULT_COST_RATES: BenchmarkCostRates = {
  inputUsdPer1M: 3,
  outputUsdPer1M: 15,
  compressionInputUsdPer1M: 3,
  compressionOutputUsdPer1M: 15,
  developerUsdPerHour: 75,
  machineUsdPerHour: 0.5,
  modelKind: 'local',
};

export interface CostRatePreset {
  id: string;
  label: string;
  rates: Omit<BenchmarkCostRates, 'modelKind'>;
}

export const COST_RATE_PRESETS: CostRatePreset[] = [
  {
    id: 'frontier-default',
    label: 'Frontier default ($3 / $15 per 1M)',
    rates: {
      inputUsdPer1M: 3,
      outputUsdPer1M: 15,
      compressionInputUsdPer1M: 3,
      compressionOutputUsdPer1M: 15,
      developerUsdPerHour: 75,
      machineUsdPerHour: 0.5,
    },
  },
  {
    id: 'local-small',
    label: 'Local small ($0.50 / $2 per 1M)',
    rates: {
      inputUsdPer1M: 0.5,
      outputUsdPer1M: 2,
      compressionInputUsdPer1M: 0.5,
      compressionOutputUsdPer1M: 2,
      developerUsdPerHour: 50,
      machineUsdPerHour: 0.25,
    },
  },
];

export function buildCostRates(
  base: Omit<BenchmarkCostRates, 'modelKind'>,
  modelKind: BenchmarkModelKind,
): BenchmarkCostRates {
  return {
    ...base,
    compressionInputUsdPer1M:
      base.compressionInputUsdPer1M > 0
        ? base.compressionInputUsdPer1M
        : base.inputUsdPer1M,
    compressionOutputUsdPer1M:
      base.compressionOutputUsdPer1M > 0
        ? base.compressionOutputUsdPer1M
        : base.outputUsdPer1M,
    modelKind,
  };
}
