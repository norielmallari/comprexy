/**
 * TanStack Query hooks for conversation data.
 *
 * Provides typed query hooks with automatic caching and refetching.
 */

import { useQuery, useQueryClient } from '@tanstack/react-query';

import { listConversations } from '@/lib/api/conversations';
import { ApiError, ConversationMetricsListItemDto } from '@/types/api';

// Query keys
export const conversationKeys = {
  all: ['conversations'] as const,
  lists: () => [...conversationKeys.all, 'list'] as const,
  list: (filters: unknown) => [...conversationKeys.lists(), filters] as const,
};

// ---------------------------------------------------------------------------
// Conversations List Query
// ---------------------------------------------------------------------------

/**
 * Fetch the list of conversations.
 *
 * @param params - Query parameters
 * @returns Promise resolving to the list of conversations
 */
async function fetchConversations(
  params: Parameters<typeof listConversations>[0] = {},
): Promise<ConversationMetricsListItemDto[]> {
  return listConversations(params);
}

/**
 * Hook to fetch the list of conversations.
 *
 * @param params - Query parameters
 * @returns Query result with conversations data
 */
export function useConversations(
  params: Parameters<typeof listConversations>[0] = {},
) {
  return useQuery({
    queryKey: conversationKeys.list(params),
    queryFn: () => fetchConversations(params),
    staleTime: 5 * 60 * 1000, // 5 minutes
    retry: (failureCount, error) => {
      // Don't retry on 4xx errors
      return (error as ApiError).statusCode !== 400 && failureCount < 3;
    },
  });
}

/**
 * Invalidate the conversations cache.
 */
export function useInvalidateConversations() {
  const queryClient = useQueryClient();

  return () => {
    queryClient.invalidateQueries({ queryKey: conversationKeys.all });
  };
}
