/**
 * TypeScript types matching the C# API DTOs.
 *
 * The C# API serializes JSON using ASP.NET Core's default serializer.
 * Property names on the DTOs are PascalCase. The exact wire format
 * (camelCase vs PascalCase) depends on the API's JsonSerializerOptions.
 * These types use camelCase as the working assumption.
 */

// ---------------------------------------------------------------------------
// Conversation List Item DTO
// ---------------------------------------------------------------------------

/**
 * Corresponds to ConversationMetricsListItemDto.
 * Returned by GET /v1/comprexy/conversations
 */
export interface ConversationMetricsListItemDto {
  conversationId: string;
  totalTurns: number;
  totalRawInputTokensEstimated: number;
  totalActualTokensEstimated: number;
  totalNetTokensSaved: number;
  /** NativeRaw − IrFull channel rollup; not tools-only; may be negative. */
  totalVirtualToolsTokensSaved: number;
  averageTokenSavingsRatio: number;
  totalCompressionOverheadTokens: number;
  updatedAt: string;
}

// ---------------------------------------------------------------------------
// Conversation Metrics Summary DTO
// ---------------------------------------------------------------------------

/**
 * Corresponds to ConversationMetricsSummaryDto.
 * Returned by GET /v1/comprexy/conversations/{id}/metrics
 */
export interface ConversationMetricsSummaryDto {
  conversationId: string;
  totalTurns: number;
  totalRawInputTokensEstimated: number;
  totalCompressedPromptTokens: number;
  totalCompletionTokens: number;
  totalCompressionOverheadTokens: number;
  /** SoftBudget baseline (IR full + completion when IrFull present). */
  totalBaselineTokensEstimated: number;
  totalActualTokensEstimated: number;
  /** SoftBudget net (IR full − prepared when IrFull present). */
  totalNetTokensSaved: number;
  /** NativeRaw − IrFull channel rollup; not tools-only; may be negative. */
  totalVirtualToolsTokensSaved: number;
  averageTokenSavingsRatio: number;
  compressionEventCount: number;
  createdAt: string;
  updatedAt: string;
}

// ---------------------------------------------------------------------------
// Conversation Turn Metric DTO
// ---------------------------------------------------------------------------

/**
 * Corresponds to ConversationTurnMetricDto.
 * Returned by GET /v1/comprexy/conversations/{id}/metrics/turns
 */
export interface ConversationTurnMetricDto {
  id: string;
  turnIndex: number;
  requestStartedAt: string;
  model: string;
  /** Native wire + native tools (NativeRaw). */
  rawInputTokensEstimated: number;
  /**
   * IR tools + full IR transcript without WM fold (SoftBudget baseline input).
   * Null on pre-migration / legacy mixed-axis rows.
   */
  irFullInputTokensEstimated: number | null;
  compressedInputTokensEstimated: number;
  /**
   * Prepared-prompt split derived by the control API. The three sum to
   * `compressedInputTokensEstimated`. `systemPromptTokensEstimated` is constant across a
   * conversation; `workingMemoryTokensEstimated` is 0 before the first working memory exists.
   */
  systemPromptTokensEstimated: number;
  workingMemoryTokensEstimated: number;
  historyAndToolsTokensEstimated: number;
  actualPromptTokens: number | null;
  actualCompletionTokens: number;
  baselineTotalTokensEstimated: number;
  compressedTotalTokensEstimated: number;
  /** SoftBudget net (IR full − prepared when IrFull present). */
  netTokensSaved: number;
  netTokenSavingsRatio: number;
  /**
   * NativeRaw − IrFull when IrFull present; null on legacy rows.
   * Not tools-only; may be negative when IR history tax exceeds native-wire savings.
   */
  virtualToolsTokensSaved: number | null;
  /** True when IrFull was not captured (pre-migration mixed-axis SoftBudget). */
  isLegacyMixedAxis: boolean;
  softBudgetExceeded: boolean;
  hardBudgetExceeded: boolean;
  trimTriggered: boolean;
  workingMemoryVersionUsed: number | null;
  rawMessageCount: number;
  sentMessageCount: number;
  durationMs: number | null;
  upstreamDurationMs: number | null;
  prepareDurationMs: number | null;
  createdAt: string;
}

// ---------------------------------------------------------------------------
// Benchmark DTOs (control-api /v1/comprexy/benchmarks/*)
// ---------------------------------------------------------------------------

export type BenchmarkModelKind = 'local' | 'usd';

export interface BenchmarkCostRates {
  inputUsdPer1M: number;
  outputUsdPer1M: number;
  compressionInputUsdPer1M: number;
  compressionOutputUsdPer1M: number;
  developerUsdPerHour: number;
  machineUsdPerHour: number;
  modelKind: BenchmarkModelKind;
}

export interface ConversationTokenTotals {
  conversationId: string;
  turnCount: number;
  inputTokens: number;
  outputTokens: number;
  overheadTokens: number;
  totalSentTokens: number;
  wallClockMs: number | null;
  totalProxyDurationMs: number | null;
  totalUpstreamDurationMs: number | null;
  totalPrepareDurationMs: number | null;
}

export interface BenchmarkChannelDelta {
  baseline: number;
  compare: number;
  delta: number;
  deltaPercent: number | null;
}

export interface BenchmarkComparisonTotals {
  baseline: ConversationTokenTotals;
  compare: ConversationTokenTotals;
  input: BenchmarkChannelDelta;
  output: BenchmarkChannelDelta;
  overhead: BenchmarkChannelDelta;
  turnCount: BenchmarkChannelDelta;
  wallClockMs: BenchmarkChannelDelta | null;
  proxyDurationMs: BenchmarkChannelDelta | null;
  caveats: string[];
}

export interface BenchmarkCostBreakdown {
  modelKind: BenchmarkModelKind;
  baselineInputCostUsd: number | null;
  baselineOutputCostUsd: number | null;
  baselineOverheadCostUsd: number | null;
  compareInputCostUsd: number | null;
  compareOutputCostUsd: number | null;
  compareOverheadCostUsd: number | null;
  baselineTotalCostUsd: number | null;
  compareTotalCostUsd: number | null;
  costDeltaUsd: number | null;
  timeValueDeltaUsd: number | null;
  disclaimer: string;
}

export interface BenchmarkTelemetryPresentationResponse {
  totals: ConversationTokenTotals;
  cost: BenchmarkCostBreakdown | null;
}

export interface BenchmarkComparisonPresentationResponse {
  totals: BenchmarkComparisonTotals;
  cost: BenchmarkCostBreakdown | null;
  baselineConversationId: string | null;
  compareConversationId: string | null;
  runId: string | null;
  turnSeriesPaths: string[];
}

export interface BenchmarkScenarioDto {
  name: string;
  promptCount: number;
  description?: string | null;
  isSmoke?: boolean;
}

export interface BenchmarkStartRunRequest {
  conversations: string[];
  rates?: BenchmarkCostRates;
  modelKind?: BenchmarkModelKind;
  runLabel?: string;
}

export interface BenchmarkStartRunResponse {
  runId: string;
}

export interface BenchmarkRunSummaryDto {
  runId: string;
  phase: string;
  runPhase: string | null;
  startedAt: string | null;
  updatedAt: string | null;
  lastError: string | null;
  arm: string | null;
  conversationName: string | null;
  promptsCompleted: number | null;
  promptCount: number | null;
  conversationNames: string[];
  costRates: BenchmarkCostRates | null;
}

export interface BenchmarkConflictError {
  activeRunId: string;
  message: string;
}

// ---------------------------------------------------------------------------
// API Response Wrappers
// ---------------------------------------------------------------------------

/**
 * Optional wrapper for list endpoints. Some APIs return { data: [...] }.
 * If the API returns the array directly, the adapter layer strips this.
 */
export interface ApiResponseList<T> {
  data: T[];
  total?: number;
}

/**
 * Optional wrapper for single-object endpoints.
 */
export interface ApiResponseSingle<T> {
  data: T;
}

// ---------------------------------------------------------------------------
// Error Types
// ---------------------------------------------------------------------------

export interface ApiError {
  message: string;
  statusCode?: number;
}
