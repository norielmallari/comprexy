/**
 * Dashboard API key persistence and auth-required signaling.
 *
 * Key is stored in sessionStorage only (cleared when the tab closes).
 * Used by apiFetch for control-api `/v1/*` — never for `/health`.
 */

export const DASHBOARD_API_KEY_STORAGE_KEY = 'comprexy.dashboardApiKey';

type AuthRequiredListener = () => void;

const authRequiredListeners = new Set<AuthRequiredListener>();

function canUseSessionStorage(): boolean {
  return typeof window !== 'undefined' && typeof window.sessionStorage !== 'undefined';
}

/** Read the stored dashboard API key, or null when unset. */
export function getDashboardApiKey(): string | null {
  if (!canUseSessionStorage()) {
    return null;
  }
  const value = window.sessionStorage.getItem(DASHBOARD_API_KEY_STORAGE_KEY);
  if (value === null || value.trim() === '') {
    return null;
  }
  return value;
}

/** Persist the dashboard API key for this browser tab session. */
export function setDashboardApiKey(key: string): void {
  if (!canUseSessionStorage()) {
    return;
  }
  window.sessionStorage.setItem(DASHBOARD_API_KEY_STORAGE_KEY, key);
}

/** Clear the stored dashboard API key. */
export function clearDashboardApiKey(): void {
  if (!canUseSessionStorage()) {
    return;
  }
  window.sessionStorage.removeItem(DASHBOARD_API_KEY_STORAGE_KEY);
}

/** Subscribe to 401 / auth-required prompts. Returns unsubscribe. */
export function onAuthRequired(listener: AuthRequiredListener): () => void {
  authRequiredListeners.add(listener);
  return () => {
    authRequiredListeners.delete(listener);
  };
}

/** Notify UI that the operator must (re)enter the dashboard API key. */
export function notifyAuthRequired(): void {
  for (const listener of authRequiredListeners) {
    listener();
  }
}

/**
 * Apply Authorization Bearer and X-Api-Key when a key is stored.
 * No-op when the key is unset (open control-api deployments).
 */
export function applyDashboardApiKeyHeaders(headers: Headers): void {
  const key = getDashboardApiKey();
  if (!key) {
    return;
  }
  headers.set('Authorization', `Bearer ${key}`);
  headers.set('X-Api-Key', key);
}
