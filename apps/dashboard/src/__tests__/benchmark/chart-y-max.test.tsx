import { describe, expect, it } from 'vitest';

import { CHART_Y_AXIS_MAX_DEFAULT } from '@/lib/constants';
import { computeChartYMax, computeSharedChartYMax } from '@/lib/chart-y-max';
import type { ChartDataPoint } from '@/types/chart';

function makePoint(baselineTokens: number, preparedPromptTokens: number): ChartDataPoint {
  return {
    turnIndex: 1,
    model: 'fixture-model',
    systemTokens: 1000,
    virtualToolSchemaTokens: 0,
    clientToolSchemaTokens: 0,
    rulesTokens: 0,
    historyTokens: 2000,
    workingMemoryTokens: 500,
    preparedPromptTokens,
    baselineTokens,
    virtualToolsTokensSaved: null,
    isLegacyMixedAxis: false,
    workingMemoryVersion: 1,
    netTokensSaved: baselineTokens - preparedPromptTokens,
    savingsRatio: 0.1,
    softBudgetExceeded: false,
    hardBudgetExceeded: false,
  };
}

describe('computeChartYMax', () => {
  it('returns default when data is empty', () => {
    expect(computeChartYMax([])).toBe(CHART_Y_AXIS_MAX_DEFAULT);
  });

  it('ceilings max baseline or prepared prompt by 10%', () => {
    expect(computeChartYMax([makePoint(10_000, 5_000)])).toBe(11_000);
  });
});

describe('computeSharedChartYMax', () => {
  it('returns default when all datasets are empty', () => {
    expect(computeSharedChartYMax([], [])).toBe(CHART_Y_AXIS_MAX_DEFAULT);
  });

  it('uses the maximum across unequal-length series', () => {
    const shorter = [makePoint(10_000, 5_000)];
    const longer = [
      makePoint(20_000, 8_000),
      makePoint(5_000, 30_000),
    ];

    expect(computeSharedChartYMax(shorter, longer)).toBe(33_000);
  });

  it('ignores empty datasets when others have data', () => {
    const data = [makePoint(15_000, 12_000)];

    expect(computeSharedChartYMax([], data, [])).toBe(16_500);
  });
});
