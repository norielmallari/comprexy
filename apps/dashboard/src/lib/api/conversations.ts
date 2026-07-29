/**
 * API client for conversation endpoints.
 *
 * Handles GET /v1/comprexy/conversations
 */

import { apiGet } from './client';
import {
  ApiResponseList,
  ConversationMetricsListItemDto,
} from '@/types/api';

/**
 * Query parameters for listing conversations.
 */
export interface ListConversationsParams {
  /** Page number (1-based) */
  page?: number;
  /** Page size */
  pageSize?: number;
  /** Sort field */
  sortBy?: string;
  /** Sort direction (asc | desc) */
  sortOrder?: 'asc' | 'desc';
}

/**
 * List all conversations with pagination.
 *
 * @param params - Query parameters
 * @returns List of conversation metrics
 */
export async function listConversations(
  params: ListConversationsParams = {},
): Promise<ConversationMetricsListItemDto[]> {
  const queryParams: Record<string, string> = {};

  if (params.page) queryParams.page = String(params.page);
  if (params.pageSize) queryParams.pageSize = String(params.pageSize);
  if (params.sortBy) queryParams.sortBy = params.sortBy;
  if (params.sortOrder) queryParams.sortOrder = params.sortOrder;

  const response = await apiGet<ApiResponseList<ConversationMetricsListItemDto>>(
    '/v1/comprexy/conversations',
    queryParams,
  );

  // Handle both wrapped and unwrapped responses
  return response.data ?? response;
}

/**
 * Get a single conversation by ID.
 *
 * @param id - Conversation ID
 * @returns Conversation metrics
 */
export async function getConversation(id: string): Promise<ConversationMetricsListItemDto> {
  return apiGet<ConversationMetricsListItemDto>(
    `/v1/comprexy/conversations/${id}`,
  );
}
