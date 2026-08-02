import { describe, expect, it } from 'vitest';

import { benchmarkKeys, benchmarkRatesKeyPart } from '@/lib/api/benchmarks';
import { DEFAULT_COST_RATES } from '@/lib/benchmark-cost';

const CONVERSATION_ID = '00000000-0000-4000-8000-000000000001';

describe('benchmarkRatesKeyPart', () => {
  it('returns stable key for identical rates', () => {
    const keyA = benchmarkRatesKeyPart(DEFAULT_COST_RATES);
    const keyB = benchmarkRatesKeyPart({ ...DEFAULT_COST_RATES });
    expect(keyA).toBe(keyB);
    expect(keyA).not.toBe('none');
  });

  it('returns different keys when rate inputs change', () => {
    const baseKey = benchmarkRatesKeyPart(DEFAULT_COST_RATES);
    const changedKey = benchmarkRatesKeyPart({
      ...DEFAULT_COST_RATES,
      inputUsdPer1M: 5,
    });
    expect(changedKey).not.toBe(baseKey);
  });

  it('returns none when rates are undefined', () => {
    expect(benchmarkRatesKeyPart(undefined)).toBe('none');
  });

  it('embeds rates in telemetry presentation query key', () => {
    const ratesKey = benchmarkRatesKeyPart(DEFAULT_COST_RATES);
    const key = benchmarkKeys.telemetryPresentation(
      CONVERSATION_ID,
      'local',
      ratesKey,
    );
    expect(key).toContain(ratesKey);
    expect(key).toContain('local');
    expect(key).toContain(CONVERSATION_ID);
  });

  it('changes comparison presentation key when model kind changes', () => {
    const ratesKey = benchmarkRatesKeyPart(DEFAULT_COST_RATES);
    const localKey = benchmarkKeys.comparisonPresentation(
      '00000000-0000-4000-8000-000000000001',
      '00000000-0000-4000-8000-000000000002',
      'local',
      ratesKey,
    );
    const usdKey = benchmarkKeys.comparisonPresentation(
      '00000000-0000-4000-8000-000000000001',
      '00000000-0000-4000-8000-000000000002',
      'usd',
      ratesKey,
    );
    expect(localKey).not.toEqual(usdKey);
  });

  it('changes telemetry presentation key when rates change', () => {
    const baseRatesKey = benchmarkRatesKeyPart(DEFAULT_COST_RATES);
    const changedRatesKey = benchmarkRatesKeyPart({
      ...DEFAULT_COST_RATES,
      inputUsdPer1M: 5,
    });

    const baseQueryKey = benchmarkKeys.telemetryPresentation(
      CONVERSATION_ID,
      'local',
      baseRatesKey,
    );
    const changedQueryKey = benchmarkKeys.telemetryPresentation(
      CONVERSATION_ID,
      'local',
      changedRatesKey,
    );

    expect(changedQueryKey).not.toEqual(baseQueryKey);
  });
});
