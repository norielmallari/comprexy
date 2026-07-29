/**
 * API client for metrics endpoints.
 *
 * Handles GET /v1/comprexy/conversations/{id}/metrics
 */

import { apiGet } from './client';
import { ConversationMetricsSummaryDto } from '@/types/api';

/**
 * Get the metrics summary for a conversation.
 *
 * @param conversationId - Conversation ID
 * @returns Conversation metrics summary
 */
export async function getMetricsSummary(
  conversationId: string,
): Promise<ConversationMetricsSummaryDto> {
  return apiGet<ConversationMetricsSummaryDto>(
    `/v1/comprexy/conversations/${conversationId}/metrics`,
  );
}
