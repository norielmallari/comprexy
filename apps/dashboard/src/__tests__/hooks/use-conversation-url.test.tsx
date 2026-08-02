import { renderHook, act } from '@testing-library/react';
import { usePathname, useRouter, useSearchParams } from 'next/navigation';

import { useConversationUrl } from '@/hooks/use-conversation-url';

vi.mock('next/navigation', () => ({
  useRouter: vi.fn(),
  useSearchParams: vi.fn(),
  usePathname: vi.fn(() => '/'),
}));

describe('useConversationUrl', () => {
  const mockRouter = {
    push: vi.fn(),
    replace: vi.fn(),
    prefetch: vi.fn(),
  };

  const createSearchParams = (params: Record<string, string>) => {
    const searchParams = new URLSearchParams();
    Object.entries(params).forEach(([key, value]) => {
      searchParams.set(key, value);
    });
    return {
      get: vi.fn((key: string) => searchParams.get(key)),
      toString: vi.fn(() => searchParams.toString()),
      keys: vi.fn(() => searchParams.keys()),
      values: vi.fn(() => searchParams.values()),
      entries: vi.fn(() => searchParams.entries()),
      forEach: vi.fn((cb: (v: string, k: string) => void) => searchParams.forEach(cb)),
      has: vi.fn((key: string) => searchParams.has(key)),
      getAll: vi.fn((key: string) => searchParams.getAll(key)),
      size: searchParams.size,
      [Symbol.iterator]: () => searchParams[Symbol.iterator](),
    } as any;
  };

  beforeEach(() => {
    vi.clearAllMocks();
    sessionStorage.clear();
    mockRouter.push.mockClear();
    mockRouter.replace.mockClear();
    vi.mocked(useRouter).mockReturnValue(mockRouter as never);
    vi.mocked(usePathname).mockReturnValue('/');
  });

  function mockSearchParams(params: Record<string, string>) {
    vi.mocked(useSearchParams).mockReturnValue(createSearchParams(params));
  }

  it('returns null conversationId when no conv param', () => {
    mockSearchParams({});
    const { result } = renderHook(() => useConversationUrl());

    expect(result.current.conversationId).toBeNull();
  });

  it('returns decoded conversationId when conv param exists', () => {
    const testId = 'abc12345-def6-7890-abcd-ef1234567890';
    mockSearchParams({ conv: testId });
    const { result } = renderHook(() => useConversationUrl());

    expect(result.current.conversationId).toBe(testId);
  });

  it('returns URL-encoded conversationId decoded', () => {
    const testId = 'abc-123_456';
    const encoded = encodeURIComponent(testId);
    mockSearchParams({ conv: encoded });
    const { result } = renderHook(() => useConversationUrl());

    expect(result.current.conversationId).toBe(testId);
  });

  it('navigates to a conversation when navigateToConversation called', async () => {
    mockSearchParams({});
    const { result } = renderHook(() => useConversationUrl());

    await act(async () => {
      result.current.navigateToConversation('new-conv-id');
    });

    expect(mockRouter.push).toHaveBeenCalled();
    const callArg = mockRouter.push.mock.calls[0][0] as string;
    expect(callArg).toContain('conv=');
  });

  it('clears conversation when navigateToConversation called with null', async () => {
    mockSearchParams({ conv: 'old-conv-id', other: 'value' });
    const { result } = renderHook(() => useConversationUrl());

    await act(async () => {
      result.current.navigateToConversation(null);
    });

    expect(mockRouter.push).toHaveBeenCalled();
    const callArg = mockRouter.push.mock.calls[0][0] as string;
    expect(callArg).not.toContain('conv');
    expect(callArg).toContain('other=value');
  });

  it('clears conversation when navigateToConversation called with null (no other params)', async () => {
    mockSearchParams({ conv: 'old-conv-id' });
    const { result } = renderHook(() => useConversationUrl());

    await act(async () => {
      result.current.navigateToConversation(null);
    });

    expect(mockRouter.push).toHaveBeenCalled();
    const callArg = mockRouter.push.mock.calls[0][0] as string;
    expect(callArg).toBe('/');
  });

  it('encodes conversation ID when navigating', async () => {
    mockSearchParams({});
    const { result } = renderHook(() => useConversationUrl());

    const testId = 'id with spaces & symbols';
    await act(async () => {
      result.current.navigateToConversation(testId);
    });

    expect(mockRouter.push).toHaveBeenCalled();
    const callArg = mockRouter.push.mock.calls[0][0] as string;
    const params = new URLSearchParams(callArg.split('?')[1] ?? '');
    expect(params.get('conv')).toBe(encodeURIComponent(testId));
  });

  it('preserves other query parameters when navigating to a conversation', async () => {
    mockSearchParams({ page: '1', filter: 'active' });
    const { result } = renderHook(() => useConversationUrl());

    await act(async () => {
      result.current.navigateToConversation('new-conv');
    });

    expect(mockRouter.push).toHaveBeenCalled();
    const callArg = mockRouter.push.mock.calls[0][0] as string;
    const params = new URLSearchParams(callArg.split('?')[1] ?? '');
    expect(params.get('page')).toBe('1');
    expect(params.get('filter')).toBe('active');
    expect(params.get('conv')).toBe(encodeURIComponent('new-conv'));
  });

  it('restores last conversation on metrics when conv param is missing', () => {
    const savedId = 'stored-conv-id';
    sessionStorage.setItem('comprexy-dashboard:last-conversation-id', savedId);
    mockSearchParams({});

    renderHook(() => useConversationUrl());

    expect(mockRouter.replace).toHaveBeenCalledWith(
      `/?conv=${encodeURIComponent(savedId)}`,
    );
  });

  it('returns navigateToConversation function', () => {
    mockSearchParams({});
    const { result } = renderHook(() => useConversationUrl());

    expect(typeof result.current.navigateToConversation).toBe('function');
  });

  it('returns conversationId as string or null', () => {
    mockSearchParams({ conv: 'test-id' });
    const { result } = renderHook(() => useConversationUrl());

    expect(typeof result.current.conversationId).toBe('string');
  });
});
