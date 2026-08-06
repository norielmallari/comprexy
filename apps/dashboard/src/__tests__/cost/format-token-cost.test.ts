import { describe, expect, it } from 'vitest';

import {
  formatTokenCostOverlay,
  formatUsd,
  isZeroRateModel,
} from '@/components/cost/format-token-cost';
import { LOCAL_MODEL, SONNET_MODEL } from '@/__tests__/helpers/cost-model-mocks';

describe('format-token-cost', () => {
  it('treats Local zero rates as zero-rate model', () => {
    expect(isZeroRateModel(LOCAL_MODEL)).toBe(true);
    expect(isZeroRateModel(SONNET_MODEL)).toBe(false);
  });

  it('returns null overlay for Local / zero-rate models', () => {
    expect(formatTokenCostOverlay(8000, LOCAL_MODEL, 'input')).toBeNull();
    expect(formatTokenCostOverlay(8000, null, 'input')).toBeNull();
  });

  it('formats `$` overlay for non-zero Sonnet rates', () => {
    // 8000 / 1e6 * $3 = $0.0240
    expect(formatTokenCostOverlay(8000, SONNET_MODEL, 'input')).toBe('$0.0240');
    // 600 / 1e6 * $15 = $0.0090
    expect(formatTokenCostOverlay(600, SONNET_MODEL, 'output')).toBe('$0.0090');
  });

  it('returns null overlay for zero token counts', () => {
    expect(formatTokenCostOverlay(0, SONNET_MODEL, 'input')).toBeNull();
  });

  it('formats compact USD amounts', () => {
    expect(formatUsd(0.024)).toBe('$0.0240');
    expect(formatUsd(12.5)).toBe('$12.50');
  });
});
