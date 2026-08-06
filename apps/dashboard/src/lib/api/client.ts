/**
 * Base API client with fetch wrapper.
 *
 * Provides a centralized fetch function with error handling,
 * base URL configuration, dashboard API key injection, and response type normalization.
 */

import { API_BASE_URL } from '@/lib/constants';
import {
  applyDashboardApiKeyHeaders,
  notifyAuthRequired,
} from '@/lib/auth/dashboard-api-key';
import { ApiError } from '@/types/api';

/**
 * Fetch configuration options.
 */
interface FetchOptions extends RequestInit {
  /** Query parameters to append to the URL */
  params?: Record<string, string>;
}

/**
 * True when the request targets `/health` (never send dashboard API key).
 */
function isHealthPath(url: string): boolean {
  try {
    const parsed = new URL(url, API_BASE_URL);
    return parsed.pathname === '/health' || parsed.pathname.endsWith('/health');
  } catch {
    return url.includes('/health');
  }
}

/**
 * Parse a JSON response body with error handling.
 *
 * @param response - Fetch response object
 * @returns Parsed JSON data
 * @throws ApiError if the response is not OK
 */
async function parseJson<T>(response: Response): Promise<T> {
  if (!response.ok) {
    if (response.status === 401) {
      notifyAuthRequired();
    }

    const errorBody = await response.json().catch(() => ({}));
    const message =
      typeof errorBody === 'object' &&
      errorBody !== null &&
      'message' in errorBody &&
      typeof (errorBody as { message: unknown }).message === 'string'
        ? (errorBody as { message: string }).message
        : typeof errorBody === 'object' &&
            errorBody !== null &&
            'error' in errorBody &&
            typeof (errorBody as { error: unknown }).error === 'string'
          ? (errorBody as { error: string }).error
          : `HTTP ${response.status} ${response.statusText}`;

    const conflictRevision =
      typeof errorBody === 'object' &&
      errorBody !== null &&
      'currentRevision' in errorBody &&
      typeof (errorBody as { currentRevision: unknown }).currentRevision === 'number'
        ? (errorBody as { currentRevision: number }).currentRevision
        : undefined;

    throw {
      message,
      statusCode: response.status,
      ...(conflictRevision !== undefined ? { currentRevision: conflictRevision } : {}),
    } satisfies ApiError;
  }

  // Empty body (204)
  if (response.status === 204) {
    return undefined as T;
  }

  const text = await response.text();
  if (!text) {
    return undefined as T;
  }

  return JSON.parse(text) as T;
}

/**
 * Make an authenticated API request.
 *
 * Injects the dashboard API key (Bearer + X-Api-Key) except for `/health`.
 *
 * @param url - The URL to fetch
 * @param options - Fetch options including headers, method, body, etc.
 * @returns Promise resolving to the parsed JSON data
 * @throws ApiError if the response is not OK
 */
export async function apiFetch<T>(
  url: string,
  options: FetchOptions = {},
): Promise<T> {
  const { params, ...fetchOptions } = options;

  // Build URL with query parameters
  const urlObj = new URL(url, API_BASE_URL);
  if (params) {
    Object.entries(params).forEach(([key, value]) => {
      urlObj.searchParams.set(key, value);
    });
  }

  // Default headers
  const headers = new Headers(fetchOptions.headers);
  if (!headers.has('Content-Type') && fetchOptions.body !== undefined) {
    headers.set('Content-Type', 'application/json');
  } else if (!headers.has('Content-Type')) {
    headers.set('Content-Type', 'application/json');
  }

  if (!isHealthPath(urlObj.pathname)) {
    applyDashboardApiKeyHeaders(headers);
  }

  const response = await fetch(urlObj.toString(), {
    ...fetchOptions,
    headers,
  });

  return parseJson<T>(response);
}

/**
 * Make a simple GET request.
 *
 * @param url - The URL to fetch
 * @param params - Optional query parameters
 * @returns Promise resolving to the parsed JSON data
 */
export async function apiGet<T>(
  url: string,
  params?: Record<string, string>,
): Promise<T> {
  return apiFetch<T>(url, { method: 'GET', params });
}

/**
 * Make a PUT request with a JSON body.
 *
 * @param url - The URL to PUT to
 * @param body - JSON-serializable body
 * @returns Promise resolving to the parsed JSON data
 */
export async function apiPut<T>(
  url: string,
  body: unknown,
): Promise<T> {
  return apiFetch<T>(url, {
    method: 'PUT',
    body: JSON.stringify(body),
  });
}

/**
 * Make a POST request with a JSON body.
 *
 * @param url - The URL to POST to
 * @param body - JSON-serializable body
 * @returns Promise resolving to the parsed JSON data
 */
export async function apiPost<T>(
  url: string,
  body: unknown,
): Promise<T> {
  return apiFetch<T>(url, {
    method: 'POST',
    body: JSON.stringify(body),
  });
}
