/**
 * TanStack Query hooks for turn data.
 *
 * Provides typed query hooks with automatic caching and refetching.
 */

import { useQuery, useQueryClient } from '@tanstack/react-query';

import { getTurnMetrics } from '@/lib/api/turns';
import { ApiError, ConversationTurnMetricDto } from '@/types/api';

// Query keys
export const turnsKeys = {
  all: ['turns'] as const,
  list: (conversationId: string) =>
    [...turnsKeys.all, 'list', conversationId] as const,
};

// ---------------------------------------------------------------------------
// Turn Metrics Query
// ---------------------------------------------------------------------------

/**
 * Fetch all turn metrics for a conversation.
 *
 * @param conversationId - Conversation ID
 * @returns Promise resolving to the list of turn metrics
 */
async function fetchTurnMetrics(
  conversationId: string,
): Promise<ConversationTurnMetricDto[]> {
  return getTurnMetrics(conversationId);
}

/**
 * Hook to fetch all turn metrics for a conversation.
 *
 * @param conversationId - Conversation ID
 * @returns Query result with turn metrics data
 */
export function useTurnMetrics(conversationId: string | null) {
  return useQuery({
    queryKey: turnsKeys.list(conversationId ?? ''),
    queryFn: () =>
      conversationId ? fetchTurnMetrics(conversationId) : null,
    enabled: !!conversationId,
    staleTime: 5 * 60 * 1000, // 5 minutes
    retry: (failureCount, error) => {
      return (error as ApiError).statusCode !== 400 && failureCount < 3;
    },
  });
}

/**
 * Invalidate the turns cache.
 */
export function useInvalidateTurns() {
  const queryClient = useQueryClient();

  return (conversationId?: string) => {
    if (conversationId) {
      queryClient.invalidateQueries({
        queryKey: turnsKeys.list(conversationId),
      });
    } else {
      queryClient.invalidateQueries({ queryKey: turnsKeys.all });
    }
  };
}
