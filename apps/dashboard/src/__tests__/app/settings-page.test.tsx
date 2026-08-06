import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import type { ReactElement } from 'react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import type { MockedFunction } from 'vitest';

import SettingsPage from '@/app/settings/page';
import { clearDashboardApiKey } from '@/lib/auth/dashboard-api-key';
import { getOperatorSettings } from '@/lib/api/settings';
import type { OperatorSettingsResponseDto } from '@/types/api';
import { OptimizationModeValues } from '@/types/api';

vi.mock('next/navigation', () => ({
  useRouter: vi.fn(() => ({ push: vi.fn(), replace: vi.fn(), prefetch: vi.fn() })),
  useSearchParams: vi.fn(() => ({ get: vi.fn(() => null), toString: vi.fn(() => '') })),
  usePathname: vi.fn(() => '/settings'),
}));

vi.mock('@/lib/queries/use-conversations', () => ({
  useConversations: vi.fn(() => ({ data: [], isLoading: false, isError: false })),
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

vi.mock('@/lib/api/settings', () => ({
  getOperatorSettings: vi.fn(),
  putOperatorSettingsWithEtag: vi.fn(),
}));

global.fetch = vi.fn().mockResolvedValue({ ok: true });

const mockGetSettings = getOperatorSettings as MockedFunction<typeof getOperatorSettings>;

function makeSettings(): OperatorSettingsResponseDto {
  return {
    revision: 1,
    updatedAt: '2026-01-15T12:00:00.000Z',
    settings: {
      proxy: {
        passThrough: true,
        optimizationMode: OptimizationModeValues.MonitorOnly,
        stripReasoningContent: false,
      },
      metrics: { enabled: true, promptTokenBasis: 1 },
    },
  };
}

function renderPage(ui: ReactElement = <SettingsPage />) {
  const client = new QueryClient({
    defaultOptions: { queries: { retry: false } },
  });
  return render(<QueryClientProvider client={client}>{ui}</QueryClientProvider>);
}

describe('SettingsPage post-login refetch', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    clearDashboardApiKey();
    sessionStorage.clear();
  });

  it('refetches settings after LoginGate Save without clicking Retry', async () => {
    let callCount = 0;
    mockGetSettings.mockImplementation(async () => {
      callCount += 1;
      if (callCount === 1) {
        throw { message: 'Unauthorized', statusCode: 401 };
      }
      return makeSettings();
    });

    renderPage();

    await waitFor(() => {
      expect(screen.getByRole('alert')).toHaveTextContent(/Unauthorized/i);
    });
    expect(screen.getByRole('button', { name: 'Retry' })).toBeInTheDocument();
    expect(screen.queryByTestId('settings-form')).not.toBeInTheDocument();

    fireEvent.click(screen.getByRole('button', { name: 'Enter dashboard API key' }));

    await waitFor(() => {
      expect(screen.getByRole('dialog', { name: 'Dashboard API key' })).toBeInTheDocument();
    });

    fireEvent.change(screen.getByLabelText('API key'), {
      target: { value: 'synthetic-dashboard-key' },
    });
    fireEvent.click(screen.getByRole('button', { name: 'Save key' }));

    await waitFor(() => {
      expect(screen.getByTestId('settings-form')).toBeInTheDocument();
    });
    expect(screen.queryByRole('button', { name: 'Retry' })).not.toBeInTheDocument();
    expect(mockGetSettings.mock.calls.length).toBeGreaterThanOrEqual(2);
  });
});
