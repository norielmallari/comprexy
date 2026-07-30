import { describe, expect, it } from 'vitest';

import { getGhostBarProps } from '@/components/charts/ghost-bar';
import {
  GHOST_BAR_FILL_DARK,
  GHOST_BAR_FILL_LIGHT,
  GHOST_BAR_FILL_OPACITY,
  GHOST_BAR_STROKE_DARK,
  GHOST_BAR_STROKE_LIGHT,
  HISTORY_SEGMENT_COLOR,
  SYSTEM_SEGMENT_COLOR,
  WM_COLORS_DARK,
  WM_COLORS_LIGHT,
} from '@/lib/constants';

describe('getGhostBarProps', () => {
  it('binds the given dataKey', () => {
    expect(getGhostBarProps({ dataKey: 'baseline', xAxisId: 'ghost' })).toMatchObject({
      dataKey: 'baseline',
    });
  });

  it('targets a separate x-axis so it overlaps rather than sits beside the stack', () => {
    expect(getGhostBarProps({ dataKey: 'baseline', xAxisId: 'ghost' }).xAxisId).toBe('ghost');
  });

  it('never carries a stackId, which would fold it into the prepared-prompt stack', () => {
    expect(getGhostBarProps({ dataKey: 'baseline', xAxisId: 'ghost' })).not.toHaveProperty(
      'stackId',
    );
  });

  it('renders as a translucent dashed outline', () => {
    const props = getGhostBarProps({ dataKey: 'baseline', xAxisId: 'ghost' });

    expect(props.fillOpacity).toBe(GHOST_BAR_FILL_OPACITY);
    expect(props.strokeDasharray).toBe('3 2');
    expect(props.strokeWidth).toBe(1);
  });

  it('uses light theme colors by default', () => {
    const props = getGhostBarProps({ dataKey: 'baseline', xAxisId: 'ghost' });

    expect(props.fill).toBe(GHOST_BAR_FILL_LIGHT);
    expect(props.stroke).toBe(GHOST_BAR_STROKE_LIGHT);
  });

  it('uses dark theme colors when isDark is set', () => {
    const props = getGhostBarProps({ dataKey: 'baseline', xAxisId: 'ghost', isDark: true });

    expect(props.fill).toBe(GHOST_BAR_FILL_DARK);
    expect(props.stroke).toBe(GHOST_BAR_STROKE_DARK);
  });

  it('does not reuse a stacked segment color, so the ghost stays distinguishable', () => {
    const ghostColors = [
      GHOST_BAR_FILL_LIGHT,
      GHOST_BAR_FILL_DARK,
      GHOST_BAR_STROKE_LIGHT,
      GHOST_BAR_STROKE_DARK,
    ];

    expect(ghostColors).not.toContain(SYSTEM_SEGMENT_COLOR);
    expect(ghostColors).not.toContain(HISTORY_SEGMENT_COLOR);
    for (const wmColor of Object.values(WM_COLORS_LIGHT)) {
      expect(ghostColors).not.toContain(wmColor);
    }
    for (const wmColor of Object.values(WM_COLORS_DARK)) {
      expect(ghostColors).not.toContain(wmColor);
    }
  });

  it('applies correct border radius', () => {
    expect(getGhostBarProps({ dataKey: 'baseline', xAxisId: 'ghost' }).radius).toEqual([4, 4, 0, 0]);
  });
});
