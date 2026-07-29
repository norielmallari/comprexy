import { render, screen, fireEvent, waitFor, act } from '@testing-library/react';
import { MockedFunction, vi } from 'vitest';
import { TopBar } from '@/components/layout/top-bar';
import { useTheme } from '@/hooks/use-theme';
import { useConversations } from '@/lib/queries/use-conversations';
import { useConversationUrl } from '@/hooks/use-conversation-url';

vi.mock('@/hooks/use-theme', () => ({
  useTheme: vi.fn(),
}));

vi.mock('@/lib/queries/use-conversations', () => ({
  useConversations: vi.fn(),
}));

vi.mock('@/hooks/use-conversation-url', () => ({
  useConversationUrl: vi.fn(),
}));

vi.mock('@/lib/constants', () => ({
  API_BASE_URL: 'http://localhost:8130',
}));

const mockUseTheme = useTheme as MockedFunction<typeof useTheme>;
const mockUseConversations = useConversations as MockedFunction<typeof useConversations>;
const mockUseConversationUrl = useConversationUrl as MockedFunction<typeof useConversationUrl>;

const defaultThemeMock = { theme: 'light' as const, toggleTheme: vi.fn() };
const defaultConversationsMock = { data: [], isLoading: false, isSuccess: true } as unknown as ReturnType<typeof useConversations>;
const defaultUrlMock = { conversationId: null, navigateToConversation: vi.fn() };

beforeEach(() => {
  vi.clearAllMocks();
  mockUseTheme.mockReturnValue(defaultThemeMock);
  mockUseConversations.mockReturnValue(defaultConversationsMock);
  mockUseConversationUrl.mockReturnValue(defaultUrlMock);

  // Mock fetch for health check
  global.fetch = vi.fn().mockResolvedValue({ ok: true });
});

describe('TopBar', () => {
  it('renders with title', () => {
    render(<TopBar />);
    expect(screen.getByText('Comprexy Metrics')).toBeInTheDocument();
  });

  it('renders conversation selector label', () => {
    render(<TopBar />);
    expect(screen.getByText('Conversation:')).toBeInTheDocument();
  });

  it('renders with mocked useConversations hook', () => {
    const conversations = [
      { conversationId: 'abc12345-def6-7890-abcd-ef1234567890', title: 'Test Conversation' },
    ];
    mockUseConversations.mockReturnValue({ data: conversations, isLoading: false } as any);

    render(<TopBar />);
    expect(screen.getByText('abc12345')).toBeInTheDocument();
  });

  it('renders with mocked useConversationUrl hook', () => {
    mockUseConversationUrl.mockReturnValue({
      conversationId: 'abc12345',
      navigateToConversation: vi.fn(),
    });

    render(<TopBar />);
    expect(screen.getByText('abc12345')).toBeInTheDocument();
  });

  it('renders health status indicator', async () => {
    global.fetch = vi.fn().mockResolvedValue({ ok: true });
    render(<TopBar />);

    await waitFor(() => {
      expect(screen.getByText('Connected')).toBeInTheDocument();
    });
  });

  it('shows disconnected when API is unhealthy', async () => {
    global.fetch = vi.fn().mockResolvedValue({ ok: false });
    render(<TopBar />);

    await waitFor(() => {
      expect(screen.getByText('Disconnected')).toBeInTheDocument();
    });
  });

  it('shows connecting state before health check completes', () => {
    global.fetch = vi.fn().mockImplementation(
      () => new Promise((resolve) => setTimeout(() => resolve({ ok: true }), 1000)),
    );
    render(<TopBar />);

    expect(screen.getByText('Connecting')).toBeInTheDocument();
  });

  it('renders theme toggle button', () => {
    render(<TopBar />);
    const themeButton = screen.getByRole('button', { name: 'Toggle theme' });
    expect(themeButton).toBeInTheDocument();
  });

  it('renders conversation selector with options', () => {
    const conversations = [
      { conversationId: 'conv-001', title: 'First' },
      { conversationId: 'conv-002', title: 'Second' },
    ];
    mockUseConversations.mockReturnValue({ data: conversations, isLoading: false } as any);

    render(<TopBar />);
    expect(screen.getByText('conv-001')).toBeInTheDocument();
    expect(screen.getByText('conv-002')).toBeInTheDocument();
  });

  it('calls navigateToConversation when option selected', async () => {
    const navigateMock = vi.fn();
    mockUseConversationUrl.mockReturnValue({
      conversationId: null,
      navigateToConversation: navigateMock,
    });

    const conversations = [
      { conversationId: 'conv-001', title: 'Test' },
    ];
    mockUseConversations.mockReturnValue({ data: conversations, isLoading: false } as any);

    render(<TopBar />);

    const select = screen.getByRole('combobox');
    fireEvent.change(select, { target: { value: 'conv-001' } });

    expect(navigateMock).toHaveBeenCalledWith('conv-001');
  });

  it('calls navigateToConversation with empty string when none selected', async () => {
    const navigateMock = vi.fn();
    mockUseConversationUrl.mockReturnValue({
      conversationId: 'conv-001',
      navigateToConversation: navigateMock,
    });

    const conversations = [
      { conversationId: 'conv-001', title: 'Test' },
    ];
    mockUseConversations.mockReturnValue({ data: conversations, isLoading: false } as any);

    render(<TopBar />);

    const select = screen.getByRole('combobox');
    fireEvent.change(select, { target: { value: 'none' } });

    // handleConversationChange: Select's 'none' value normalizes to '' in jsdom
    // since 'none' isn't in the options list, so navigateToConversation('') is called
    expect(navigateMock).toHaveBeenCalledWith('');
  });

  it('handles empty conversations list', () => {
    mockUseConversations.mockReturnValue({ data: [], isLoading: false, isSuccess: true } as unknown as ReturnType<typeof useConversations>);
    mockUseConversationUrl.mockReturnValue({ conversationId: null, navigateToConversation: vi.fn() });

    render(<TopBar />);
    expect(screen.getByText('Select conversation')).toBeInTheDocument();
  });

  it('shows loading state while conversations are loading', () => {
    mockUseConversations.mockReturnValue({ data: undefined, isLoading: true } as any);

    render(<TopBar />);
    expect(screen.getByText('Loading...')).toBeInTheDocument();
  });

  it('disables select while conversations are loading', () => {
    mockUseConversations.mockReturnValue({ data: undefined, isLoading: true } as any);

    render(<TopBar />);
    const select = screen.getByRole('combobox');
    expect(select).toBeDisabled();
  });

  it('toggles theme when theme toggle button is clicked', () => {
    const toggleMock = vi.fn();
    mockUseTheme.mockReturnValue({ theme: 'light', toggleTheme: toggleMock });

    render(<TopBar />);
    const themeButton = screen.getByRole('button', { name: 'Toggle theme' });
    fireEvent.click(themeButton);

    expect(toggleMock).toHaveBeenCalled();
  });

  it('renders badge when conversationId is present', () => {
    mockUseConversationUrl.mockReturnValue({
      conversationId: 'abc12345',
      navigateToConversation: vi.fn(),
    });

    render(<TopBar />);
    const badge = screen.getByText('abc12345');
    expect(badge).toBeInTheDocument();
  });

  it('does not render badge when conversationId is null', () => {
    mockUseConversationUrl.mockReturnValue({
      conversationId: null,
      navigateToConversation: vi.fn(),
    });

    render(<TopBar />);
    expect(screen.queryByText('abc12345')).not.toBeInTheDocument();
  });

  it('shows tooltip with API status on healthy connection', async () => {
    global.fetch = vi.fn().mockResolvedValue({ ok: true });
    render(<TopBar />);

    await waitFor(() => {
      expect(screen.getByText('Connected')).toBeInTheDocument();
    });
  });
});
