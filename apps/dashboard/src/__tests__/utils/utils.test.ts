import {
  cn,
  formatNumber,
  formatCompactNumber,
  formatPercentage,
  formatDateTime,
  formatRelativeTime,
  truncateConversationId,
  encodeConversationId,
  decodeConversationId,
  getMaxWorkingMemoryVersion,
  getAverageCompressionRatio,
  getBestCompressionRatio,
  getWmColor,
  getContrastingForeground,
  softBudgetBaselineTokens,
  transformTurnsToChartData,
} from '@/lib/utils';
import type { ConversationTurnMetricDto } from '@/types/api';
import type { ChartDataPoint } from '@/types/chart';

// ---------------------------------------------------------------------------
// cn()
// ---------------------------------------------------------------------------

describe('cn()', () => {
  it('joins multiple class names', () => {
    expect(cn('p-2', 'm-4')).toBe('p-2 m-4');
  });

  it('resolves conflicts via twMerge (later wins)', () => {
    expect(cn('p-2', 'p-4')).toBe('p-4');
    expect(cn('text-red-500', 'text-blue-600')).toBe('text-blue-600');
    expect(cn('bg-gray-100', 'bg-gray-900')).toBe('bg-gray-900');
  });

  it('handles empty inputs', () => {
    expect(cn()).toBe('');
    expect(cn('')).toBe('');
    expect(cn([], '')).toBe('');
  });

  it('handles falsy values (null, undefined, false)', () => {
    expect(cn('p-2', null, 'm-4')).toBe('p-2 m-4');
    expect(cn('p-2', undefined, 'm-4')).toBe('p-2 m-4');
    expect(cn('p-2', false, 'm-4')).toBe('p-2 m-4');
  });

  it('handles arrays of class names', () => {
    expect(cn(['p-2', 'm-4'], 'text-red-500')).toBe('p-2 m-4 text-red-500');
    expect(cn(['p-2', 'p-4'])).toBe('p-4');
  });

  it('handles conditional class names', () => {
    const isActive = true;
    expect(cn(isActive && 'bg-blue-500', 'text-white')).toBe(
      'bg-blue-500 text-white',
    );

    const isDisabled = false;
    expect(cn(isDisabled && 'opacity-50', 'text-black')).toBe('text-black');
  });
});

// ---------------------------------------------------------------------------
// formatNumber()
// ---------------------------------------------------------------------------

describe('formatNumber()', () => {
  it('formats integers with commas', () => {
    expect(formatNumber(1000)).toBe('1,000');
  });

  it('formats large numbers', () => {
    expect(formatNumber(1000000)).toBe('1,000,000');
  });

  it('handles zero', () => {
    expect(formatNumber(0)).toBe('0');
  });

  it('handles negative numbers', () => {
    expect(formatNumber(-1000)).toBe('-1,000');
  });

  it('handles larger numbers', () => {
    expect(formatNumber(1234567890)).toBe('1,234,567,890');
  });
});

// ---------------------------------------------------------------------------
// formatCompactNumber()
// ---------------------------------------------------------------------------

describe('formatCompactNumber()', () => {
  it('formats small numbers with decimals', () => {
    expect(formatCompactNumber(123)).toBe('123.0');
    expect(formatCompactNumber(123, undefined, 0)).toBe('123');
  });

  it('formats thousands', () => {
    expect(formatCompactNumber(1500)).toBe('1.5K');
  });

  it('formats millions', () => {
    expect(formatCompactNumber(1500000)).toBe('1.5M');
  });

  it('appends custom suffix', () => {
    expect(formatCompactNumber(1500, ' tokens')).toBe('1.5K tokens');
  });

  it('respects custom decimals', () => {
    expect(formatCompactNumber(1500, undefined, 0)).toBe('2K');
    expect(formatCompactNumber(1500, undefined, 2)).toBe('1.50K');
  });

  it('handles zero', () => {
    expect(formatCompactNumber(0)).toBe('0.0');
    expect(formatCompactNumber(0, undefined, 0)).toBe('0');
  });

  it('handles negative numbers', () => {
    expect(formatCompactNumber(-1500)).toBe('-1.5K');
    expect(formatCompactNumber(-1500000)).toBe('-1.5M');
  });

  it('handles negative small numbers', () => {
    expect(formatCompactNumber(-123)).toBe('-123.0');
  });

  it('handles boundary values at 1000', () => {
    expect(formatCompactNumber(999)).toBe('999.0');
    expect(formatCompactNumber(1000)).toBe('1.0K');
  });

  it('handles boundary values at 1000000', () => {
    expect(formatCompactNumber(999999)).toBe('1000.0K');
    expect(formatCompactNumber(1000000)).toBe('1.0M');
  });
});

// ---------------------------------------------------------------------------
// formatPercentage()
// ---------------------------------------------------------------------------

describe('formatPercentage()', () => {
  it('formats basic ratio', () => {
    expect(formatPercentage(0.25)).toBe('25.0%');
  });

  it('handles zero', () => {
    expect(formatPercentage(0)).toBe('0.0%');
  });

  it('handles full ratio', () => {
    expect(formatPercentage(1)).toBe('100.0%');
  });

  it('respects custom decimals', () => {
    expect(formatPercentage(0.255, 2)).toBe('25.50%');
  });

  it('handles negative ratios', () => {
    expect(formatPercentage(-0.5)).toBe('-50.0%');
  });

  it('handles ratios above 1', () => {
    expect(formatPercentage(1.5)).toBe('150.0%');
  });
});

// ---------------------------------------------------------------------------
// formatDateTime()
// ---------------------------------------------------------------------------

describe('formatDateTime()', () => {
  it('formats a date string', () => {
    const result = formatDateTime('2025-07-29T15:45:00Z');
    expect(result).toContain('Jul');
    expect(result).toContain('2025');
  });

  it('formats a Date object', () => {
    const date = new Date('2025-07-29T15:45:00Z');
    const result = formatDateTime(date);
    expect(result).toContain('Jul');
    expect(result).toContain('2025');
  });

  it('handles invalid input gracefully', () => {
    expect(() => formatDateTime('invalid')).toThrow();
  });
});

// ---------------------------------------------------------------------------
// formatRelativeTime()
// ---------------------------------------------------------------------------

describe('formatRelativeTime()', () => {
  it('returns "just now" for past date within 60 seconds', () => {
    const now = new Date();
    const result = formatRelativeTime(now);
    expect(result).toBe('just now');
  });

  it('returns "Xm ago" for minutes', () => {
    const fiveMinAgo = new Date(Date.now() - 5 * 60 * 1000);
    expect(formatRelativeTime(fiveMinAgo)).toBe('5m ago');
  });

  it('returns "Xh ago" for hours', () => {
    const threeHoursAgo = new Date(Date.now() - 3 * 60 * 60 * 1000);
    expect(formatRelativeTime(threeHoursAgo)).toBe('3h ago');
  });

  it('returns "Xd ago" for days', () => {
    const twoDaysAgo = new Date(Date.now() - 2 * 24 * 60 * 60 * 1000);
    expect(formatRelativeTime(twoDaysAgo)).toBe('2d ago');
  });

  it('handles date strings', () => {
    const fiveMinAgo = new Date(Date.now() - 5 * 60 * 1000);
    expect(formatRelativeTime(fiveMinAgo.toISOString())).toBe('5m ago');
  });

  it('returns "just now" for future dates', () => {
    const future = new Date(Date.now() + 1000);
    expect(formatRelativeTime(future)).toBe('just now');
  });
});

// ---------------------------------------------------------------------------
// truncateConversationId()
// ---------------------------------------------------------------------------

describe('truncateConversationId()', () => {
  it('truncates UUID to first 8 characters', () => {
    expect(truncateConversationId('abc12345-def6-7890-abcd-ef1234567890')).toBe(
      'abc12345',
    );
  });

  it('handles short strings', () => {
    expect(truncateConversationId('short')).toBe('short');
    expect(truncateConversationId('ab')).toBe('ab');
    expect(truncateConversationId('')).toBe('');
  });
});

// ---------------------------------------------------------------------------
// encodeConversationId() / decodeConversationId()
// ---------------------------------------------------------------------------

describe('encodeConversationId() / decodeConversationId()', () => {
  it('round-trip encoding/decoding', () => {
    const id = 'abc12345-def6-7890-abcd-ef1234567890';
    const encoded = encodeConversationId(id);
    const decoded = decodeConversationId(encoded);
    expect(decoded).toBe(id);
  });

  it('handles special characters', () => {
    const id = 'id with spaces & special=chars';
    const encoded = encodeConversationId(id);
    const decoded = decodeConversationId(encoded);
    expect(decoded).toBe(id);
  });

  it('does not double-encode', () => {
    const id = 'abc12345';
    const encoded = encodeConversationId(id);
    const decoded = decodeConversationId(encoded);
    expect(decoded).toBe('abc12345');
  });

  it('handles UUID with dashes', () => {
    const uuid = '550e8400-e29b-41d4-a716-446655440000';
    const encoded = encodeConversationId(uuid);
    const decoded = decodeConversationId(encoded);
    expect(decoded).toBe(uuid);
  });
});

// ---------------------------------------------------------------------------
// getWmColor()
// ---------------------------------------------------------------------------

describe('getWmColor()', () => {
  it('returns correct dark mode colors for versions 0-3', () => {
    expect(getWmColor(0, true)).toBe('#94a3b8');
    expect(getWmColor(1, true)).toBe('#60a5fa');
    expect(getWmColor(2, true)).toBe('#93c5fd');
    expect(getWmColor(3, true)).toBe('#bfdbfe');
  });

  it('returns correct light mode colors for versions 0-3', () => {
    expect(getWmColor(0, false)).toBe('#64748b');
    expect(getWmColor(1, false)).toBe('#2563eb');
    expect(getWmColor(2, false)).toBe('#1d4ed8');
    expect(getWmColor(3, false)).toBe('#1e3a8a');
  });

  it('returns fallback color for out-of-range versions', () => {
    expect(getWmColor(4, true)).toBe('#94a3b8');
    expect(getWmColor(10, true)).toBe('#94a3b8');
    expect(getWmColor(4, false)).toBe('#64748b');
  });

  it('returns fallback for negative versions', () => {
    expect(getWmColor(-1, true)).toBe('#94a3b8');
    expect(getWmColor(-1, false)).toBe('#64748b');
  });
});

describe('getContrastingForeground()', () => {
  it('returns dark text on light fills', () => {
    expect(getContrastingForeground('#bfdbfe')).toBe('#0f172a');
  });

  it('returns white text on dark fills', () => {
    expect(getContrastingForeground('#1e3a8a')).toBe('#ffffff');
  });
});

// ---------------------------------------------------------------------------
// transformTurnsToChartData() / getMaxWorkingMemoryVersion()
// ---------------------------------------------------------------------------

const makeTurn = (
  partial: Partial<ConversationTurnMetricDto> = {},
): ConversationTurnMetricDto => ({
  id: 'test-id',
  turnIndex: 1,
  requestStartedAt: '2025-07-29T10:00:00Z',
  model: 'gpt-4',
  rawInputTokensEstimated: 5000,
  irFullInputTokensEstimated: 4500,
  compressedInputTokensEstimated: 2000,
  systemPromptTokensEstimated: 300,
  workingMemoryTokensEstimated: 700,
  preparedVirtualToolSchemaTokensEstimated: 200,
  preparedClientToolSchemaTokensEstimated: 100,
  preparedRulesTokensEstimated: 0,
  historyTokensEstimated: 700,
  actualPromptTokens: 1000,
  actualCompletionTokens: 500,
  baselineTotalTokensEstimated: 5000,
  compressedTotalTokensEstimated: 3000,
  netTokensSaved: 2500,
  netTokenSavingsRatio: 0.5,
  virtualToolsTokensSaved: 500,
  isLegacyMixedAxis: false,
  softBudgetExceeded: false,
  hardBudgetExceeded: false,
  trimTriggered: false,
  workingMemoryVersionUsed: 1,
  rawMessageCount: 5,
  sentMessageCount: 4,
  durationMs: null,
  upstreamDurationMs: null,
  prepareDurationMs: null,
  createdAt: '2025-07-29T10:00:00Z',
  ...partial,
});

describe('transformTurnsToChartData()', () => {
  it('transforms a single turn correctly', () => {
    const turn = makeTurn();
    const result = transformTurnsToChartData([turn]);

    expect(result).toHaveLength(1);
    const point = result[0] as ChartDataPoint;

    expect(point.turnIndex).toBe(1);
    expect(point.model).toBe('gpt-4');
    expect(point.systemTokens).toBe(300);
    expect(point.virtualToolSchemaTokens).toBe(200);
    expect(point.clientToolSchemaTokens).toBe(100);
    expect(point.rulesTokens).toBe(0);
    expect(point.historyTokens).toBe(700);
    expect(point.workingMemoryTokens).toBe(700);
    expect(point.preparedPromptTokens).toBe(2000);
    expect(point.baselineTokens).toBe(4500);
    expect(point.virtualToolsTokensSaved).toBe(500);
    expect(point.isLegacyMixedAxis).toBe(false);
    expect(point.workingMemoryVersion).toBe(1);
    expect(point.netTokensSaved).toBe(2500);
    expect(point.savingsRatio).toBe(0.5);
    expect(point.softBudgetExceeded).toBe(false);
    expect(point.hardBudgetExceeded).toBe(false);
  });

  it('stacks segments that sum to the prepared prompt', () => {
    const point = transformTurnsToChartData([makeTurn()])[0];

    expect(
      point.systemTokens +
        point.virtualToolSchemaTokens +
        point.clientToolSchemaTokens +
        point.rulesTokens +
        point.historyTokens +
        point.workingMemoryTokens,
    ).toBe(point.preparedPromptTokens);
  });

  it('keeps SoftBudget ghost and VT channel separate from catalog segments', () => {
    const point = transformTurnsToChartData([
      makeTurn({
        preparedVirtualToolSchemaTokensEstimated: 200,
        virtualToolsTokensSaved: 500,
        irFullInputTokensEstimated: 4500,
      }),
    ])[0];

    expect(point.baselineTokens).toBe(4500);
    expect(point.virtualToolsTokensSaved).toBe(500);
    expect(point.virtualToolSchemaTokens).toBe(200);
    expect(point.virtualToolsTokensSaved).not.toBe(point.virtualToolSchemaTokens);
  });

  it('maps optional rules into the prepared stack when non-zero', () => {
    const point = transformTurnsToChartData([
      makeTurn({
        preparedRulesTokensEstimated: 50,
        historyTokensEstimated: 650,
      }),
    ])[0];

    expect(point.rulesTokens).toBe(50);
    expect(
      point.systemTokens +
        point.virtualToolSchemaTokens +
        point.clientToolSchemaTokens +
        point.rulesTokens +
        point.historyTokens +
        point.workingMemoryTokens,
    ).toBe(point.preparedPromptTokens);
  });

  it('leaves the working memory segment empty before the first version exists', () => {
    const turn = makeTurn({
      workingMemoryVersionUsed: null,
      workingMemoryTokensEstimated: 0,
      historyTokensEstimated: 1400,
    });

    const point = transformTurnsToChartData([turn])[0];

    expect(point.workingMemoryVersion).toBeNull();
    expect(point.workingMemoryTokens).toBe(0);
    expect(
      point.systemTokens +
        point.virtualToolSchemaTokens +
        point.clientToolSchemaTokens +
        point.rulesTokens +
        point.historyTokens,
    ).toBe(point.preparedPromptTokens);
  });

  it('holds the system segment constant across turns', () => {
    const points = transformTurnsToChartData([
      makeTurn({ turnIndex: 1, historyTokensEstimated: 700 }),
      makeTurn({
        turnIndex: 2,
        compressedInputTokensEstimated: 9000,
        historyTokensEstimated: 7700,
        actualPromptTokens: 8500,
      }),
    ]);

    expect(points.map((p) => p.systemTokens)).toEqual([300, 300]);
  });

  it('handles multiple turns', () => {
    const turns = [makeTurn({ turnIndex: 1 }), makeTurn({ turnIndex: 2 })];
    const result = transformTurnsToChartData(turns);

    expect(result).toHaveLength(2);
    expect(result[0].turnIndex).toBe(1);
    expect(result[1].turnIndex).toBe(2);
  });

  it('maps all fields correctly', () => {
    const turn = makeTurn({
      turnIndex: 5,
      model: 'claude-3',
      workingMemoryVersionUsed: 3,
      softBudgetExceeded: true,
      hardBudgetExceeded: true,
    });
    const result = transformTurnsToChartData([turn]);

    const point = result[0] as ChartDataPoint;
    expect(point.turnIndex).toBe(5);
    expect(point.model).toBe('claude-3');
    expect(point.workingMemoryVersion).toBe(3);
    expect(point.softBudgetExceeded).toBe(true);
    expect(point.hardBudgetExceeded).toBe(true);
  });

  it('handles null workingMemoryVersionUsed', () => {
    const turn = makeTurn({ workingMemoryVersionUsed: null });
    const result = transformTurnsToChartData([turn]);

    const point = result[0] as ChartDataPoint;
    expect(point.workingMemoryVersion).toBeNull();
  });

  it('preserves netTokenSavingsRatio from the turn', () => {
    const turn = makeTurn({ netTokenSavingsRatio: 0.35 });
    const result = transformTurnsToChartData([turn]);

    const point = result[0] as ChartDataPoint;
    expect(point.savingsRatio).toBe(0.35);
  });

  it('uses IrFull for SoftBudget ghost baseline when present', () => {
    const point = transformTurnsToChartData([
      makeTurn({
        rawInputTokensEstimated: 9000,
        irFullInputTokensEstimated: 7000,
        isLegacyMixedAxis: false,
      }),
    ])[0];

    expect(point.baselineTokens).toBe(7000);
  });

  it('falls back to NativeRaw when IrFull is null or legacy mixed-axis', () => {
    const nullIrFull = transformTurnsToChartData([
      makeTurn({
        rawInputTokensEstimated: 9000,
        irFullInputTokensEstimated: null,
        virtualToolsTokensSaved: null,
        isLegacyMixedAxis: true,
      }),
    ])[0];

    expect(nullIrFull.baselineTokens).toBe(9000);
    expect(nullIrFull.isLegacyMixedAxis).toBe(true);
    expect(nullIrFull.virtualToolsTokensSaved).toBeNull();
  });

  it('passes through Virtual Tools tokens when present', () => {
    const point = transformTurnsToChartData([
      makeTurn({ virtualToolsTokensSaved: -150 }),
    ])[0];

    expect(point.virtualToolsTokensSaved).toBe(-150);
  });
});

describe('softBudgetBaselineTokens()', () => {
  it('returns IrFull when present and not legacy', () => {
    expect(
      softBudgetBaselineTokens({
        rawInputTokensEstimated: 9000,
        irFullInputTokensEstimated: 7000,
        isLegacyMixedAxis: false,
      }),
    ).toBe(7000);
  });

  it('returns NativeRaw when IrFull is null', () => {
    expect(
      softBudgetBaselineTokens({
        rawInputTokensEstimated: 9000,
        irFullInputTokensEstimated: null,
        isLegacyMixedAxis: false,
      }),
    ).toBe(9000);
  });

  it('returns NativeRaw when legacy mixed-axis even if IrFull is set', () => {
    expect(
      softBudgetBaselineTokens({
        rawInputTokensEstimated: 9000,
        irFullInputTokensEstimated: 7000,
        isLegacyMixedAxis: true,
      }),
    ).toBe(9000);
  });
});

// ---------------------------------------------------------------------------
// getMaxWorkingMemoryVersion()
// ---------------------------------------------------------------------------

describe('getMaxWorkingMemoryVersion()', () => {
  it('returns null for empty or missing turns', () => {
    expect(getMaxWorkingMemoryVersion(undefined)).toBeNull();
    expect(getMaxWorkingMemoryVersion(null)).toBeNull();
    expect(getMaxWorkingMemoryVersion([])).toBeNull();
  });

  it('treats null WorkingMemoryVersionUsed as 0', () => {
    expect(
      getMaxWorkingMemoryVersion([makeTurn({ workingMemoryVersionUsed: null })]),
    ).toBe(0);
  });

  it('returns the max version across turns', () => {
    expect(
      getMaxWorkingMemoryVersion([
        makeTurn({ workingMemoryVersionUsed: null }),
        makeTurn({ workingMemoryVersionUsed: 1 }),
        makeTurn({ workingMemoryVersionUsed: 3 }),
        makeTurn({ workingMemoryVersionUsed: 2 }),
      ]),
    ).toBe(3);
  });
});

// ---------------------------------------------------------------------------
// getAverageCompressionRatio()
// ---------------------------------------------------------------------------

describe('getAverageCompressionRatio()', () => {
  it('returns null for empty or missing turns', () => {
    expect(getAverageCompressionRatio(undefined)).toBeNull();
    expect(getAverageCompressionRatio(null)).toBeNull();
    expect(getAverageCompressionRatio([])).toBeNull();
  });

  it('returns the simple mean of per-turn savings ratios', () => {
    expect(
      getAverageCompressionRatio([
        makeTurn({ netTokenSavingsRatio: 0.1 }),
        makeTurn({ netTokenSavingsRatio: 0.5 }),
        makeTurn({ netTokenSavingsRatio: 0.9 }),
      ]),
    ).toBeCloseTo(0.5);
  });
});

// ---------------------------------------------------------------------------
// getBestCompressionRatio()
// ---------------------------------------------------------------------------

describe('getBestCompressionRatio()', () => {
  it('returns null for empty or missing turns', () => {
    expect(getBestCompressionRatio(undefined)).toBeNull();
    expect(getBestCompressionRatio(null)).toBeNull();
    expect(getBestCompressionRatio([])).toBeNull();
  });

  it('returns the single turn ratio', () => {
    expect(
      getBestCompressionRatio([makeTurn({ netTokenSavingsRatio: 0.42 })]),
    ).toBe(0.42);
  });

  it('returns the max ratio across turns', () => {
    expect(
      getBestCompressionRatio([
        makeTurn({ netTokenSavingsRatio: 0.1 }),
        makeTurn({ netTokenSavingsRatio: 0.67 }),
        makeTurn({ netTokenSavingsRatio: 0.4 }),
      ]),
    ).toBe(0.67);
  });

  it('handles negative ratios (still picks the max)', () => {
    expect(
      getBestCompressionRatio([
        makeTurn({ netTokenSavingsRatio: -0.2 }),
        makeTurn({ netTokenSavingsRatio: -0.05 }),
      ]),
    ).toBe(-0.05);
  });
});
