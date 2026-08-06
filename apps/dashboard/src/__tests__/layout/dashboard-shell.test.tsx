import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';

import { DashboardShell, DashboardSkeleton } from '@/components/layout/dashboard-shell';
import {
  clearDashboardApiKey,
  setDashboardApiKey,
} from '@/lib/auth/dashboard-api-key';

const invalidateQueries = vi.fn();

// Mock next/navigation so useConversationUrl doesn't need app router
vi.mock('next/navigation', () => ({
  useRouter: vi.fn(() => ({ push: vi.fn(), replace: vi.fn(), prefetch: vi.fn() })),
  useSearchParams: vi.fn(() => ({ get: vi.fn(() => null), toString: vi.fn(() => '') })),
  usePathname: vi.fn(() => '/'),
}));

// Mock react-query so TopBar's useConversations doesn't need a real QueryClient
vi.mock('@tanstack/react-query', () => ({
  QueryClientProvider: ({ children }: { children: React.ReactNode }) => children,
  useQuery: vi.fn(() => ({ data: [], isLoading: false, isError: false })),
  useQueryClient: vi.fn(() => ({ invalidateQueries })),
}));

vi.mock('@/lib/queries/use-cost-models', () => ({
  useCostModels: vi.fn(() => ({
    data: [
      {
        modelKey: 'local',
        displayLabel: 'Local',
        currencyCode: 'USD',
        inputUsdPer1M: 0,
        outputUsdPer1M: 0,
        sortOrder: 0,
      },
    ],
    isLoading: false,
    isError: false,
  })),
}));

// Mock fetch for TopBar health check
global.fetch = vi.fn().mockResolvedValue({ ok: true });

describe('DashboardShell', () => {
  beforeEach(() => {
    invalidateQueries.mockClear();
    clearDashboardApiKey();
    sessionStorage.clear();
  });

  it('renders children', () => {
    render(
      <DashboardShell>
        <div data-testid="child-content">Hello World</div>
      </DashboardShell>,
    );
    expect(screen.getByTestId('child-content')).toBeInTheDocument();
    expect(screen.getByText('Hello World')).toBeInTheDocument();
  });

  it('calls invalidateQueries when LoginGate authenticates', async () => {
    render(
      <DashboardShell>
        <div>Content</div>
      </DashboardShell>,
    );

    fireEvent.click(screen.getByRole('button', { name: 'Enter dashboard API key' }));

    await waitFor(() => {
      expect(screen.getByRole('dialog', { name: 'Dashboard API key' })).toBeInTheDocument();
    });

    fireEvent.change(screen.getByLabelText('API key'), {
      target: { value: 'synthetic-dashboard-key' },
    });
    fireEvent.click(screen.getByRole('button', { name: 'Save key' }));

    expect(invalidateQueries).toHaveBeenCalled();
  });

  it('calls invalidateQueries when LoginGate clears the key', async () => {
    setDashboardApiKey('synthetic-dashboard-key');

    render(
      <DashboardShell>
        <div>Content</div>
      </DashboardShell>,
    );

    fireEvent.click(screen.getByRole('button', { name: 'Manage dashboard API key' }));

    await waitFor(() => {
      expect(screen.getByRole('dialog', { name: 'Dashboard API key' })).toBeInTheDocument();
    });

    fireEvent.click(screen.getByRole('button', { name: 'Clear key' }));

    expect(invalidateQueries).toHaveBeenCalled();
  });

  it('renders with TopBar (Comprexy Metrics title)', () => {
    render(<DashboardShell><div>Content</div></DashboardShell>);
    expect(screen.getByText('Comprexy Metrics')).toBeInTheDocument();
  });

  it('renders main content area with correct structure', () => {
    const { container } = render(<DashboardShell><div>Test</div></DashboardShell>);
    const main = container.querySelector('main');
    expect(main).toBeInTheDocument();
    expect(main?.classList.contains('overflow-auto')).toBe(true);
    expect(main?.classList.contains('p-3')).toBe(true);
  });

  it('wraps content in max-width container', () => {
    const { container } = render(<DashboardShell><div>Test</div></DashboardShell>);
    const wrapper = container.querySelector('.max-w-\\[1920px\\]');
    expect(wrapper).toBeInTheDocument();
  });

  it('renders without children (null children)', () => {
    render(<DashboardShell children={undefined} />);
    expect(screen.getByText('Comprexy Metrics')).toBeInTheDocument();
  });

  it('renders multiple children', () => {
    render(
      <DashboardShell>
        <span data-testid="first">First</span>
        <span data-testid="second">Second</span>
      </DashboardShell>,
    );
    expect(screen.getByTestId('first')).toBeInTheDocument();
    expect(screen.getByTestId('second')).toBeInTheDocument();
  });

  it('has correct outer container structure', () => {
    const { container } = render(<DashboardShell><div>Test</div></DashboardShell>);
    const outerDiv = container.querySelector('div.flex.h-screen.w-full.flex-col');
    expect(outerDiv).toBeInTheDocument();
  });
});

describe('DashboardSkeleton', () => {
  it('renders skeleton placeholder elements', () => {
    render(<DashboardSkeleton />);
    const skeletons = document.querySelectorAll('[class*="animate-pulse"]');
    expect(skeletons.length).toBeGreaterThan(0);
  });

  it('renders hero skeleton', () => {
    const { container } = render(<DashboardSkeleton />);
    const heroSkeleton = container.querySelector('.h-20.w-full');
    expect(heroSkeleton).toBeInTheDocument();
  });

  it('renders metric cards skeleton grid', () => {
    const { container } = render(<DashboardSkeleton />);
    const grid = container.querySelector('.grid-cols-1.gap-2');
    expect(grid).toBeInTheDocument();
  });

  it('renders four metric card skeletons', () => {
    const { container } = render(<DashboardSkeleton />);
    const metricCards = container.querySelectorAll('.h-16');
    expect(metricCards.length).toBe(4);
  });

  it('renders a fill chart skeleton', () => {
    const { container } = render(<DashboardSkeleton />);
    const chartSkeleton = container.querySelector('.flex-1');
    expect(chartSkeleton).toBeInTheDocument();
  });

  it('uses a flex column layout that can fill leftover height', () => {
    const { container } = render(<DashboardSkeleton />);
    const root = container.querySelector('.flex.flex-1.flex-col');
    expect(root).toBeInTheDocument();
  });
});
