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
  totalBaselineTokensEstimated: number;
  totalActualTokensEstimated: number;
  totalNetTokensSaved: number;
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
  rawInputTokensEstimated: number;
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
  netTokensSaved: number;
  netTokenSavingsRatio: number;
  softBudgetExceeded: boolean;
  hardBudgetExceeded: boolean;
  trimTriggered: boolean;
  workingMemoryVersionUsed: number | null;
  rawMessageCount: number;
  sentMessageCount: number;
  createdAt: string;
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
