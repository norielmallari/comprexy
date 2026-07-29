/**
 * Constants for the dashboard.
 *
 * Includes working memory version colors, overhead color, ghost bar color,
 * and other reusable values.
 */

// ---------------------------------------------------------------------------
// Working Memory Version Colors
// ---------------------------------------------------------------------------

/**
 * Color palette for working memory versions.
 *
 * Light mode:
 *   v0 = #e0e7ef (light gray-blue)
 *   v1 = #a8c4e0
 *   v2 = #6ba3d6
 *   v3 = #2d6bc4 (deep blue)
 *
 * Dark mode (darker tones):
 *   v0 = #2a3a52
 *   v1 = #3d5a80
 *   v2 = #4a7ab5
 *   v3 = #5b8fd4
 */

export const WM_COLORS_LIGHT = {
  0: '#e0e7ef',
  1: '#a8c4e0',
  2: '#6ba3d6',
  3: '#2d6bc4',
} as const;

export const WM_COLORS_DARK = {
  0: '#2a3a52',
  1: '#3d5a80',
  2: '#4a7ab5',
  3: '#5b8fd4',
} as const;

// ---------------------------------------------------------------------------
// Overhead Color (amber)
// ---------------------------------------------------------------------------

export const OVERHEAD_COLOR = '#d69e2e';

// ---------------------------------------------------------------------------
// Ghost Bar Color (light gray)
// ---------------------------------------------------------------------------

export const GHOST_BAR_COLOR = '#cbd5e0';

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

/** Chart height in pixels */
export const CHART_HEIGHT = 400;

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
