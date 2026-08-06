/**
 * Shared Vitest helpers for cost-catalog + store mocks used by token card tests.
 */

import type { CostModelDto } from '@/types/api';

export const LOCAL_MODEL: CostModelDto = {
  modelKey: 'local',
  displayLabel: 'Local',
  currencyCode: 'USD',
  inputUsdPer1M: 0,
  outputUsdPer1M: 0,
  sortOrder: 0,
};

export const SONNET_MODEL: CostModelDto = {
  modelKey: 'claude-sonnet-5',
  displayLabel: 'Claude Sonnet 5',
  currencyCode: 'USD',
  inputUsdPer1M: 3,
  outputUsdPer1M: 15,
  sortOrder: 2,
};

export const CATALOG_MODELS: CostModelDto[] = [LOCAL_MODEL, SONNET_MODEL];

export function storeSelectorStub(selectedCostModelKey: string) {
  return (selector: (s: { selectedCostModelKey: string }) => unknown) =>
    selector({ selectedCostModelKey });
}
