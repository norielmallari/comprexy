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
  /** Captured system prompt; constant across the conversation */
  systemTokens: number;
  /** Still-unfolded raw turns plus the model-facing tool catalog */
  historyTokens: number;
  /** Working memory injected on this turn; 0 before the first version exists */
  workingMemoryTokens: number;
  /** Prompt actually sent upstream — the sum of the three segments above */
  preparedPromptTokens: number;
  /** Uncompressed prompt estimate, drawn as the ghost bar behind the stack */
  baselineTokens: number;
  /** Working memory version used (null = none yet) */
  workingMemoryVersion: number | null;
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
  /** Draw the swatch as a dashed outline, matching the ghost bar rather than a solid segment */
  outlined?: boolean;
}
