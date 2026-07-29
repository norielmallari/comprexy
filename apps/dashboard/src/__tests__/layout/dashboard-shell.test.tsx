import { render, screen } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { DashboardShell, DashboardSkeleton } from '@/components/layout/dashboard-shell';

// Mock next/navigation so useConversationUrl doesn't need app router
vi.mock('next/navigation', () => ({
  useRouter: vi.fn(() => ({ push: vi.fn(), replace: vi.fn(), prefetch: vi.fn() })),
  useSearchParams: vi.fn(() => ({ get: vi.fn(() => null), toString: vi.fn(() => '') })),
}));

// Mock react-query so TopBar's useConversations doesn't need a real QueryClient
vi.mock('@tanstack/react-query', () => ({
  QueryClientProvider: ({ children }: any) => children,
  useQuery: vi.fn(() => ({ data: [], isLoading: false })),
  useQueryClient: vi.fn(),
}));

// Mock fetch for TopBar health check
global.fetch = vi.fn().mockResolvedValue({ ok: true });

describe('DashboardShell', () => {
  it('renders children', () => {
    render(
      <DashboardShell>
        <div data-testid="child-content">Hello World</div>
      </DashboardShell>,
    );
    expect(screen.getByTestId('child-content')).toBeInTheDocument();
    expect(screen.getByText('Hello World')).toBeInTheDocument();
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
    expect(main?.classList.contains('p-4')).toBe(true);
  });

  it('wraps content in max-width container', () => {
    const { container } = render(<DashboardShell><div>Test</div></DashboardShell>);
    const wrapper = container.querySelector('.max-w-\\[1920px\\]');
    expect(wrapper).toBeInTheDocument();
  });

  it('renders without children (null children)', () => {
    render(<DashboardShell />);
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
    const heroSkeleton = container.querySelector('.h-32.w-full');
    expect(heroSkeleton).toBeInTheDocument();
  });

  it('renders metric cards skeleton grid', () => {
    const { container } = render(<DashboardSkeleton />);
    const grid = container.querySelector('.grid-cols-1.gap-4');
    expect(grid).toBeInTheDocument();
  });

  it('renders four metric card skeletons', () => {
    const { container } = render(<DashboardSkeleton />);
    const metricCards = container.querySelectorAll('.h-24');
    expect(metricCards.length).toBe(4);
  });

  it('renders charts skeleton grid', () => {
    const { container } = render(<DashboardSkeleton />);
    const chartsGrid = container.querySelector('.lg\\:grid-cols-2');
    expect(chartsGrid).toBeInTheDocument();
  });

  it('renders two chart skeletons', () => {
    const { container } = render(<DashboardSkeleton />);
    const chartSkeletons = container.querySelectorAll('.h-80');
    expect(chartSkeletons.length).toBe(2);
  });

  it('has space-y-6 layout spacing', () => {
    const { container } = render(<DashboardSkeleton />);
    const root = container.querySelector('.space-y-6');
    expect(root).toBeInTheDocument();
  });
});
