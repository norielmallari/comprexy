/**
 * Chart data types for recharts.
 *
 * These types represent the transformed data structures used by chart
 * components, derived from API DTOs.
 */

/**
 * A single data point for the bar chart, representing one turn.
 */
export interface ChartDataPoint {
  /** 1-based turn index displayed on x-axis */
  turnIndex: number;
  /** Model name used in this turn */
  model: string;
  /** Prompt tokens (light gray) */
  promptTokens: number;
  /** System tokens (light gray) */
  systemTokens: number;
  /** Compressed working memory tokens */
  compressedTokens: number;
  /** Compression overhead tokens (amber) */
  overheadTokens: number;
  /** Baseline total for ghost bar comparison */
  baselineTokens: number;
  /** Working memory version used (null = not compressed) */
  workingMemoryVersion: number | null;
  /** Total compressed tokens (prompt + system + compressed + overhead) */
  totalCompressed: number;
  /** Net tokens saved in this turn */
  netTokensSaved: number;
  /** Token savings ratio */
  savingsRatio: number;
  /** Whether soft budget was exceeded */
  softBudgetExceeded: boolean;
  /** Whether hard budget was exceeded */
  hardBudgetExceeded: boolean;
}

/**
 * Legend item for the chart legend component.
 */
export interface ChartLegendItem {
  /** Display label */
  label: string;
  /** Color value */
  color: string;
}
