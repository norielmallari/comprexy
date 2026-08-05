/**
 * Constants for the dashboard.
 *
 * Includes working memory version colors, overhead color, ghost bar color,
 * and other reusable values.
 *
 * Chart fills target WCAG 2.1 AA non-text contrast (≥3:1) against card/page
 * backgrounds. Badge text uses {@link getContrastingForeground}.
 */

// ---------------------------------------------------------------------------
// Working Memory Version Colors
// ---------------------------------------------------------------------------

/**
 * Color palette for working memory versions (WCAG AA).
 *
 * Light mode (fills ≥3:1 vs white; v1+ support white label text ≥4.5:1):
 *   v0 = #64748b
 *   v1 = #2563eb
 *   v2 = #1d4ed8
 *   v3 = #1e3a8a
 *
 * Dark mode (fills ≥3:1 vs slate-800 card; labels via contrasting foreground):
 *   v0 = #94a3b8
 *   v1 = #60a5fa
 *   v2 = #93c5fd
 *   v3 = #bfdbfe
 */

export const WM_COLORS_LIGHT = {
  0: '#64748b',
  1: '#2563eb',
  2: '#1d4ed8',
  3: '#1e3a8a',
} as const;

export const WM_COLORS_DARK = {
  0: '#94a3b8',
  1: '#60a5fa',
  2: '#93c5fd',
  3: '#bfdbfe',
} as const;

// ---------------------------------------------------------------------------
// Prepared Prompt Segment Colors
// ---------------------------------------------------------------------------

/** Captured system prompt — constant across a conversation (≥3:1 vs white) */
export const SYSTEM_SEGMENT_COLOR = '#475569';

/** Still-unfolded raw turns plus the model-facing tool catalog (≥3:1 vs white) */
export const HISTORY_SEGMENT_COLOR = '#0f766e';

/** Stacked-bar separator stroke so adjacent segments stay distinguishable (1.4.11). */
export const CHART_SEGMENT_STROKE_LIGHT = '#ffffff';
export const CHART_SEGMENT_STROKE_DARK = '#0f172a';
export const CHART_SEGMENT_STROKE_WIDTH = 1;

// ---------------------------------------------------------------------------
// Ghost Bar (baseline reference drawn behind the stack)
// ---------------------------------------------------------------------------

/**
 * The ghost is an outlined backdrop rather than a solid bar so it stays readable behind the
 * stacked segments. It must not reuse a segment color.
 */
export const GHOST_BAR_FILL_LIGHT = '#78716c';
export const GHOST_BAR_FILL_DARK = '#a8a29e';
export const GHOST_BAR_STROKE_LIGHT = '#1e293b';
export const GHOST_BAR_STROKE_DARK = '#f8fafc';
export const GHOST_BAR_FILL_OPACITY = 0.18;

/** SoftBudget chart ghost baseline — IR full prompt estimate (no WM fold). */
export const SOFTBUDGET_GHOST_LABEL = 'SoftBudget (IR full)';

/** Virtual Tools / native-wire channel secondary metric title. */
export const VIRTUAL_TOOLS_CHANNEL_LABEL = 'Virtual Tools channel';

// ---------------------------------------------------------------------------
// Working Memory Version Labels
// ---------------------------------------------------------------------------

export const WM_LABELS = [
  'None (v0)',
  'Working Memory v1',
  'Working Memory v2',
  'Working Memory v3',
] as const;

// ---------------------------------------------------------------------------
// API Configuration
// ---------------------------------------------------------------------------

/** Base URL for the control API. Override via NEXT_PUBLIC_API_BASE_URL env var. */
export const API_BASE_URL =
  process.env.NEXT_PUBLIC_API_BASE_URL ?? 'http://localhost:8130';

/** Default number of conversations to fetch per page */
export const DEFAULT_PAGE_SIZE = 25;

// ---------------------------------------------------------------------------
// Chart Configuration
// ---------------------------------------------------------------------------

/** Minimum y-axis scale for the bar chart */
export const CHART_Y_AXIS_MIN = 0;

/** Maximum y-axis scale for the bar chart (auto-calculated, but capped here) */
export const CHART_Y_AXIS_MAX_DEFAULT = 1000000;

/** Fallback chart height in pixels when BarChart is not in fill mode */
export const CHART_HEIGHT = 220;

/** Chart width (desktop) */
export const CHART_WIDTH = 1200;

// ---------------------------------------------------------------------------
// Theme
// ---------------------------------------------------------------------------

export const THEME = {
  STORAGE_KEY: 'comprexy-theme',
  LIGHT: 'light',
  DARK: 'dark',
} as const;
