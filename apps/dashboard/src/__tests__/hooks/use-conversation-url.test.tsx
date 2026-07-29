import { renderHook, act } from '@testing-library/react';
import { useConversationUrl } from '@/hooks/use-conversation-url';

vi.mock('next/navigation', () => ({
  useRouter: vi.fn(),
  useSearchParams: vi.fn(),
}));

const mockUseRouter = vi.fn();
const mockUseSearchParams = vi.fn();

vi.doMock('next/navigation', () => ({
  useRouter: mockUseRouter,
  useSearchParams: mockUseSearchParams,
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
    mockRouter.push.mockClear();
  });

  it('returns null conversationId when no conv param', async () => {
    mockUseRouter.mockReturnValue(mockRouter);
    mockUseSearchParams.mockReturnValue(createSearchParams({}));

    // Re-import to pick up fresh mocks
    const { useConversationUrl: freshHook } = await import('@/hooks/use-conversation-url');
    const { result } = renderHook(() => freshHook());

    expect(result.current.conversationId).toBeNull();
  });

  it('returns decoded conversationId when conv param exists', async () => {
    const testId = 'abc12345-def6-7890-abcd-ef1234567890';
    mockUseRouter.mockReturnValue(mockRouter);
    mockUseSearchParams.mockReturnValue(createSearchParams({ conv: testId }));

    const { useConversationUrl: freshHook } = await import('@/hooks/use-conversation-url');
    const { result } = renderHook(() => freshHook());

    expect(result.current.conversationId).toBe(testId);
  });

  it('returns URL-encoded conversationId decoded', async () => {
    const testId = 'abc-123_456';
    const encoded = encodeURIComponent(testId);
    mockUseRouter.mockReturnValue(mockRouter);
    mockUseSearchParams.mockReturnValue(createSearchParams({ conv: encoded }));

    const { useConversationUrl: freshHook } = await import('@/hooks/use-conversation-url');
    const { result } = renderHook(() => freshHook());

    expect(result.current.conversationId).toBe(testId);
  });

  it('navigates to a conversation when navigateToConversation called', async () => {
    mockUseRouter.mockReturnValue(mockRouter);
    mockUseSearchParams.mockReturnValue(createSearchParams({}));

    const { useConversationUrl: freshHook } = await import('@/hooks/use-conversation-url');
    const { result } = renderHook(() => freshHook());

    await act(async () => {
      result.current.navigateToConversation('new-conv-id');
    });

    expect(mockRouter.push).toHaveBeenCalled();
    const callArg = mockRouter.push.mock.calls[0][0] as string;
    expect(callArg).toContain('conv=');
  });

  it('clears conversation when navigateToConversation called with null', async () => {
    mockUseRouter.mockReturnValue(mockRouter);
    mockUseSearchParams.mockReturnValue(createSearchParams({ conv: 'old-conv-id', other: 'value' }));

    const { useConversationUrl: freshHook } = await import('@/hooks/use-conversation-url');
    const { result } = renderHook(() => freshHook());

    await act(async () => {
      result.current.navigateToConversation(null);
    });

    expect(mockRouter.push).toHaveBeenCalled();
    const callArg = mockRouter.push.mock.calls[0][0] as string;
    expect(callArg).not.toContain('conv');
    expect(callArg).toContain('other=value');
  });

  it('clears conversation when navigateToConversation called with null (no other params)', async () => {
    mockUseRouter.mockReturnValue(mockRouter);
    mockUseSearchParams.mockReturnValue(createSearchParams({ conv: 'old-conv-id' }));

    const { useConversationUrl: freshHook } = await import('@/hooks/use-conversation-url');
    const { result } = renderHook(() => freshHook());

    await act(async () => {
      result.current.navigateToConversation(null);
    });

    expect(mockRouter.push).toHaveBeenCalled();
    const callArg = mockRouter.push.mock.calls[0][0] as string;
    expect(callArg).toBe('/');
  });

  it('encodes conversation ID when navigating', async () => {
    mockUseRouter.mockReturnValue(mockRouter);
    mockUseSearchParams.mockReturnValue(createSearchParams({}));

    const { useConversationUrl: freshHook } = await import('@/hooks/use-conversation-url');
    const { result } = renderHook(() => freshHook());

    const testId = 'id with spaces & symbols';
    await act(async () => {
      result.current.navigateToConversation(testId);
    });

    expect(mockRouter.push).toHaveBeenCalled();
    const callArg = mockRouter.push.mock.calls[0][0] as string;
    const params = new URLSearchParams(callArg.slice(1));
    expect(params.get('conv')).toBe(encodeURIComponent(testId));
  });

  it('preserves other query parameters when navigating to a conversation', async () => {
    mockUseRouter.mockReturnValue(mockRouter);
    mockUseSearchParams.mockReturnValue(createSearchParams({ page: '1', filter: 'active' }));

    const { useConversationUrl: freshHook } = await import('@/hooks/use-conversation-url');
    const { result } = renderHook(() => freshHook());

    await act(async () => {
      result.current.navigateToConversation('new-conv');
    });

    expect(mockRouter.push).toHaveBeenCalled();
    const callArg = mockRouter.push.mock.calls[0][0] as string;
    const params = new URLSearchParams(callArg.slice(1));
    expect(params.get('page')).toBe('1');
    expect(params.get('filter')).toBe('active');
    expect(params.get('conv')).toBe(encodeURIComponent('new-conv'));
  });

  it('returns navigateToConversation function', async () => {
    mockUseRouter.mockReturnValue(mockRouter);
    mockUseSearchParams.mockReturnValue(createSearchParams({}));

    const { useConversationUrl: freshHook } = await import('@/hooks/use-conversation-url');
    const { result } = renderHook(() => freshHook());

    expect(typeof result.current.navigateToConversation).toBe('function');
  });

  it('returns conversationId as string or null', async () => {
    mockUseRouter.mockReturnValue(mockRouter);
    mockUseSearchParams.mockReturnValue(createSearchParams({ conv: 'test-id' }));

    const { useConversationUrl: freshHook } = await import('@/hooks/use-conversation-url');
    const { result } = renderHook(() => freshHook());

    expect(typeof result.current.conversationId).toBe('string');
  });
});
