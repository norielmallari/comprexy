/**
 * TanStack Query hooks for metrics data.
 *
 * Provides typed query hooks with automatic caching and refetching.
 */

import { useQuery, useQueryClient } from '@tanstack/react-query';

import { getMetricsSummary } from '@/lib/api/metrics';
import { ApiError, ConversationMetricsSummaryDto } from '@/types/api';

// Query keys
export const metricsKeys = {
  all: ['metrics'] as const,
  summary: (conversationId: string) =>
    [...metricsKeys.all, 'summary', conversationId] as const,
};

// ---------------------------------------------------------------------------
// Metrics Summary Query
// ---------------------------------------------------------------------------

/**
 * Fetch the metrics summary for a conversation.
 *
 * @param conversationId - Conversation ID
 * @returns Promise resolving to the metrics summary
 */
async function fetchMetricsSummary(
  conversationId: string,
): Promise<ConversationMetricsSummaryDto> {
  return getMetricsSummary(conversationId);
}

/**
 * Hook to fetch the metrics summary for a conversation.
 *
 * @param conversationId - Conversation ID
 * @returns Query result with metrics summary data
 */
export function useMetricsSummary(conversationId: string | null) {
  return useQuery({
    queryKey: metricsKeys.summary(conversationId ?? ''),
    queryFn: () =>
      conversationId ? fetchMetricsSummary(conversationId) : null,
    enabled: !!conversationId,
    staleTime: 5 * 60 * 1000, // 5 minutes
    retry: (failureCount, error) => {
      return (error as ApiError).statusCode !== 400 && failureCount < 3;
    },
  });
}

/**
 * Invalidate the metrics cache.
 */
export function useInvalidateMetrics() {
  const queryClient = useQueryClient();

  return (conversationId?: string) => {
    if (conversationId) {
      queryClient.invalidateQueries({
        queryKey: metricsKeys.summary(conversationId),
      });
    } else {
      queryClient.invalidateQueries({ queryKey: metricsKeys.all });
    }
  };
}
