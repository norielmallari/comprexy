/**
 * Constants for the dashboard.
 *
 * Prepared-bar fills use a limited slate + blue palette (light/dark pairs) targeting
 * WCAG 2.1 AA non-text contrast (≥3:1) against card/page backgrounds. Badge text uses
 * {@link getContrastingForeground}.
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

/**
 * Limited prepared-bar palette: slate + teal/emerald (tools) + blue (history/WM).
 * Fills meet WCAG 2.1 AA non-text contrast (≥3:1) vs card background; adjacent segments
 * use {@link CHART_SEGMENT_STROKE_*} separators for distinguishability (1.4.11).
 *
 * Light (≥3:1 vs white / slate-50):
 *   System = slate; Virtual = teal-700; Client = emerald-800; Rules/History/WM = blue
 *
 * Dark (≥3:1 vs slate-800 / slate-900):
 *   System = muted slate; Virtual = teal-500; Client = emerald-300; Rules/History/WM = blue
 */
export const PREPARED_SEGMENT_COLORS_LIGHT = {
  system: '#64748b',
  virtualToolSchema: '#0f766e',
  clientToolSchema: '#065f46',
  rules: '#1e40af',
  history: '#1d4ed8',
  workingMemory: '#1e3a8a',
} as const;

export const PREPARED_SEGMENT_COLORS_DARK = {
  system: '#7c8ea3',
  virtualToolSchema: '#14b8a6',
  clientToolSchema: '#6ee7b7',
  rules: '#7aa2e3',
  history: '#5b8bd6',
  workingMemory: '#93b4e8',
} as const;

export type PreparedSegmentColors =
  | typeof PREPARED_SEGMENT_COLORS_LIGHT
  | typeof PREPARED_SEGMENT_COLORS_DARK;

export function getPreparedSegmentColors(isDark: boolean): PreparedSegmentColors {
  return isDark ? PREPARED_SEGMENT_COLORS_DARK : PREPARED_SEGMENT_COLORS_LIGHT;
}

/** Light-mode System fill (tests / ghost contrast checks). */
export const SYSTEM_SEGMENT_COLOR = PREPARED_SEGMENT_COLORS_LIGHT.system;

/** Light-mode Virtual tools fill. */
export const VIRTUAL_TOOL_SCHEMA_SEGMENT_COLOR =
  PREPARED_SEGMENT_COLORS_LIGHT.virtualToolSchema;

/** Light-mode Client tools fill. */
export const CLIENT_TOOL_SCHEMA_SEGMENT_COLOR =
  PREPARED_SEGMENT_COLORS_LIGHT.clientToolSchema;

/** Light-mode Rules fill. */
export const RULES_SEGMENT_COLOR = PREPARED_SEGMENT_COLORS_LIGHT.rules;

/** Light-mode History fill. */
export const HISTORY_SEGMENT_COLOR = PREPARED_SEGMENT_COLORS_LIGHT.history;

/** Stacked-bar separator stroke so adjacent segments stay distinguishable (1.4.11). */
export const CHART_SEGMENT_STROKE_LIGHT = '#ffffff';
export const CHART_SEGMENT_STROKE_DARK = '#0f172a';
export const CHART_SEGMENT_STROKE_WIDTH = 1;

/** Prepared-stack catalog labels (never “VT / native-wire”). */
export const VIRTUAL_TOOLS_STACK_LABEL = 'Virtual tools';
export const CLIENT_TOOLS_STACK_LABEL = 'Client tools';
export const RULES_STACK_LABEL = 'Rules';
export const HISTORY_STACK_LABEL = 'History';

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

/** Chart ghost baseline — IR full prompt estimate (no WM fold); UI: Full History Est. */
export const FULL_HISTORY_EST_LABEL = 'Full History Est.';

/** SoftBudget net vs IrFull baseline — operator-facing chart/tooltip label. */
export const SAVED_VS_FULL_HISTORY_LABEL = 'Saved vs full history';

/** SoftBudget savings ratio vs IrFull baseline — operator-facing chart/tooltip label. */
export const SAVINGS_VS_FULL_HISTORY_RATIO_LABEL = 'Savings vs full history';

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

/**
 * Base URL for the control API.
 *
 * - Unset → `http://localhost:8130` (local `npm run dev`)
 * - Empty string → same-origin (combined nginx deploy; see {@link resolveApiBaseUrl})
 * - Non-empty → that absolute origin (no trailing slash)
 */
export const API_BASE_URL =
  process.env.NEXT_PUBLIC_API_BASE_URL === undefined
    ? 'http://localhost:8130'
    : process.env.NEXT_PUBLIC_API_BASE_URL.replace(/\/$/, '');

/**
 * Runtime control-api origin. When {@link API_BASE_URL} is empty (combined container),
 * uses `window.location.origin` in the browser.
 */
export function resolveApiBaseUrl(): string {
  if (API_BASE_URL.length > 0) {
    return API_BASE_URL;
  }

  if (typeof window !== 'undefined' && window.location?.origin) {
    return window.location.origin;
  }

  return 'http://127.0.0.1:8130';
}

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
