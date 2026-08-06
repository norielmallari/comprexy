/**
 * Cost model presets and helpers for the benchmark console.
 *
 * Catalog models come from GET /v1/comprexy/cost-models; this module maps them
 * into BenchmarkCostRates for the existing presentation calculator.
 */

import type { BenchmarkCostRates, BenchmarkModelKind, CostModelDto } from '@/types/api';
import {
  COST_DISCLAIMER,
  DEFAULT_COST_MODEL_KEY,
  isZeroRateModel,
  LOCAL_COST_DISCLAIMER,
} from '@/components/cost/format-token-cost';

export {
  COST_DISCLAIMER,
  LOCAL_COST_DISCLAIMER,
  DEFAULT_COST_MODEL_KEY,
  isZeroRateModel,
};

/** Server-side defaults from BenchOrchestrationOptions (control-api appsettings). */
export const BENCHMARK_TIMEOUT_DEFAULTS = {
  completionTimeoutSeconds: 300,
  conversationTimeoutSeconds: 7200,
  smokeConversationTimeoutSeconds: 1200,
} as const;

/** Time-value defaults retained for bench presentation (not from catalog). */
export const DEFAULT_TIME_VALUE_RATES = {
  developerUsdPerHour: 75,
  machineUsdPerHour: 0.5,
} as const;

export const DEFAULT_COST_RATES: BenchmarkCostRates = {
  inputUsdPer1M: 0,
  outputUsdPer1M: 0,
  compressionInputUsdPer1M: 0,
  compressionOutputUsdPer1M: 0,
  developerUsdPerHour: DEFAULT_TIME_VALUE_RATES.developerUsdPerHour,
  machineUsdPerHour: DEFAULT_TIME_VALUE_RATES.machineUsdPerHour,
  modelKind: 'local',
};

/**
 * Map a catalog cost model into BenchmarkCostRates.
 * Local / zero rates → modelKind `local` (tokens only, no USD in server calc).
 */
export function catalogModelToBenchmarkRates(
  model: CostModelDto,
  timeValue: Pick<BenchmarkCostRates, 'developerUsdPerHour' | 'machineUsdPerHour'> = DEFAULT_TIME_VALUE_RATES,
): BenchmarkCostRates {
  const input = Number(model.inputUsdPer1M);
  const output = Number(model.outputUsdPer1M);
  const local = isZeroRateModel(model);
  const modelKind: BenchmarkModelKind = local ? 'local' : 'usd';

  return {
    inputUsdPer1M: input,
    outputUsdPer1M: output,
    compressionInputUsdPer1M: input,
    compressionOutputUsdPer1M: output,
    developerUsdPerHour: timeValue.developerUsdPerHour,
    machineUsdPerHour: timeValue.machineUsdPerHour,
    modelKind,
  };
}

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
