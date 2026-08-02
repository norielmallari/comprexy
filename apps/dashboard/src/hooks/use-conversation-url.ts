/**
 * Hook for managing conversation selection via URL query parameter.
 *
 * Persists the last selection in sessionStorage so Metrics ↔ Benchmark navigation
 * can restore the operator's conversation when the `conv` query param is dropped.
 */

import { usePathname, useRouter, useSearchParams } from 'next/navigation';
import { useEffect, useMemo, useState } from 'react';

import {
  decodeConversationId,
  encodeConversationId,
} from '@/lib/utils';

const STORAGE_KEY = 'comprexy-dashboard:last-conversation-id';

function readStoredConversationId(): string | null {
  if (typeof window === 'undefined') {
    return null;
  }

  return sessionStorage.getItem(STORAGE_KEY);
}

function writeStoredConversationId(id: string | null) {
  if (typeof window === 'undefined') {
    return;
  }

  if (id) {
    sessionStorage.setItem(STORAGE_KEY, id);
  } else {
    sessionStorage.removeItem(STORAGE_KEY);
  }
}

function normalizeConversationId(id: string | null | undefined): string | null {
  if (!id || id === 'none') {
    return null;
  }

  return id;
}

/**
 * Hook to manage conversation selection from URL query parameters.
 */
export function useConversationUrl() {
  const router = useRouter();
  const pathname = usePathname();
  const searchParams = useSearchParams();
  const rawConv = searchParams.get('conv');

  const conversationId = useMemo(
    () => (rawConv ? decodeConversationId(rawConv) : null),
    [rawConv],
  );

  const [storedConversationId, setStoredConversationId] = useState<string | null>(null);
  const [isRestoringConversation, setIsRestoringConversation] = useState(false);

  useEffect(() => {
    setStoredConversationId(readStoredConversationId());
  }, [conversationId]);

  useEffect(() => {
    if (conversationId) {
      writeStoredConversationId(conversationId);
      setIsRestoringConversation(false);
      return;
    }

    if (pathname !== '/') {
      setIsRestoringConversation(false);
      return;
    }

    const saved = readStoredConversationId();
    if (!saved) {
      setIsRestoringConversation(false);
      return;
    }

    setIsRestoringConversation(true);
    const params = new URLSearchParams(searchParams.toString());
    params.set('conv', encodeConversationId(saved));
    router.replace(`/?${params.toString()}`);
  }, [conversationId, pathname, router, searchParams]);

  const effectiveConversationId = conversationId ?? storedConversationId;

  const navigateToConversation = (id: string | null) => {
    const normalized = normalizeConversationId(id);
    writeStoredConversationId(normalized);
    setStoredConversationId(normalized);

    const basePath = pathname ?? '/';
    const params = new URLSearchParams(searchParams.toString());

    if (normalized === null) {
      params.delete('conv');
      const query = params.toString();
      router.push(query ? `${basePath}?${query}` : basePath);
      return;
    }

    params.set('conv', encodeConversationId(normalized));
    router.push(`${basePath}?${params.toString()}`);
  };

  return {
    conversationId,
    effectiveConversationId,
    isRestoringConversation,
    navigateToConversation,
  };
}
