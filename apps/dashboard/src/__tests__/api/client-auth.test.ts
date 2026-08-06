import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import { apiFetch } from '@/lib/api/client';
import {
  DASHBOARD_API_KEY_STORAGE_KEY,
  clearDashboardApiKey,
  setDashboardApiKey,
} from '@/lib/auth/dashboard-api-key';

vi.mock('@/lib/constants', () => ({
  API_BASE_URL: 'http://localhost:8130',
}));

describe('apiFetch auth headers', () => {
  beforeEach(() => {
    clearDashboardApiKey();
    sessionStorage.clear();
  });

  afterEach(() => {
    vi.unstubAllGlobals();
    clearDashboardApiKey();
    sessionStorage.clear();
  });

  it('includes Bearer and X-Api-Key from session storage on /v1 paths', async () => {
    setDashboardApiKey('synthetic-dashboard-key');

    const fetchMock = vi.fn().mockResolvedValue({
      ok: true,
      status: 200,
      text: async () => JSON.stringify({ ok: true }),
      json: async () => ({ ok: true }),
    });
    vi.stubGlobal('fetch', fetchMock);

    await apiFetch('/v1/comprexy/cost-models');

    expect(fetchMock).toHaveBeenCalledTimes(1);
    const [, init] = fetchMock.mock.calls[0] as [string, RequestInit];
    const headers = new Headers(init.headers);
    expect(headers.get('Authorization')).toBe('Bearer synthetic-dashboard-key');
    expect(headers.get('X-Api-Key')).toBe('synthetic-dashboard-key');
  });

  it('omits auth headers on /health', async () => {
    setDashboardApiKey('synthetic-dashboard-key');

    const fetchMock = vi.fn().mockResolvedValue({
      ok: true,
      status: 200,
      text: async () => JSON.stringify({ status: 'Healthy' }),
      json: async () => ({ status: 'Healthy' }),
    });
    vi.stubGlobal('fetch', fetchMock);

    await apiFetch('/health');

    expect(fetchMock).toHaveBeenCalledTimes(1);
    const [, init] = fetchMock.mock.calls[0] as [string, RequestInit];
    const headers = new Headers(init.headers);
    expect(headers.get('Authorization')).toBeNull();
    expect(headers.get('X-Api-Key')).toBeNull();
  });

  it('omits auth headers when no key is stored', async () => {
    expect(sessionStorage.getItem(DASHBOARD_API_KEY_STORAGE_KEY)).toBeNull();

    const fetchMock = vi.fn().mockResolvedValue({
      ok: true,
      status: 200,
      text: async () => JSON.stringify([]),
      json: async () => [],
    });
    vi.stubGlobal('fetch', fetchMock);

    await apiFetch('/v1/comprexy/cost-models');

    const [, init] = fetchMock.mock.calls[0] as [string, RequestInit];
    const headers = new Headers(init.headers);
    expect(headers.get('Authorization')).toBeNull();
    expect(headers.get('X-Api-Key')).toBeNull();
  });

  it('notifies auth-required listeners on 401', async () => {
    const { onAuthRequired, notifyAuthRequired: _n } = await import(
      '@/lib/auth/dashboard-api-key'
    );
    const listener = vi.fn();
    const unsubscribe = onAuthRequired(listener);

    const fetchMock = vi.fn().mockResolvedValue({
      ok: false,
      status: 401,
      statusText: 'Unauthorized',
      json: async () => ({ message: 'Unauthorized' }),
    });
    vi.stubGlobal('fetch', fetchMock);

    await expect(apiFetch('/v1/comprexy/conversations')).rejects.toMatchObject({
      statusCode: 401,
    });
    expect(listener).toHaveBeenCalledTimes(1);
    unsubscribe();
  });
});
