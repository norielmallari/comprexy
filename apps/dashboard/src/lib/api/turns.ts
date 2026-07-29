/**
 * API client for turn endpoints.
 *
 * Handles GET /v1/comprexy/conversations/{id}/metrics/turns
 */

import { apiGet } from './client';
import { ConversationTurnMetricDto } from '@/types/api';

/**
 * Get all turn metrics for a conversation.
 *
 * @param conversationId - Conversation ID
 * @returns Array of turn metrics
 */
export async function getTurnMetrics(
  conversationId: string,
): Promise<ConversationTurnMetricDto[]> {
  return apiGet<ConversationTurnMetricDto[]>(
    `/v1/comprexy/conversations/${conversationId}/metrics/turns`,
  );
}
