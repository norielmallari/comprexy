/**
 * Hook for managing conversation selection via URL query parameter.
 *
 * Reads the `conv` query parameter and provides a way to navigate
 * to a different conversation by updating the URL.
 */

import { useRouter, useSearchParams } from 'next/navigation';

import {
  decodeConversationId,
  encodeConversationId,
} from '@/lib/utils';

/**
 * Hook to manage conversation selection from URL query parameters.
 *
 * @returns Object containing current conversation ID and navigation function
 */
export function useConversationUrl() {
  const router = useRouter();
  const searchParams = useSearchParams();

  /**
   * Get the currently selected conversation ID from the URL.
   */
  const conversationId = searchParams.get('conv');

  /**
   * Navigate to a different conversation by updating the URL.
   *
   * @param id - Conversation ID to navigate to (null to clear)
   */
  const navigateToConversation = (id: string | null) => {
    if (id === null) {
      // Remove the conv parameter
      const params = new URLSearchParams(searchParams.toString());
      params.delete('conv');
      const query = params.toString();
      router.push(query ? `?${query}` : window.location.pathname);
    } else {
      // Update the conv parameter
      const params = new URLSearchParams(searchParams.toString());
      params.set('conv', encodeConversationId(id));
      router.push(`?${params.toString()}`);
    }
  };

  return { conversationId, navigateToConversation };
}
