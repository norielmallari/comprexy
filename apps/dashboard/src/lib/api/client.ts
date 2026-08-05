/**
 * Base API client with fetch wrapper.
 *
 * Provides a centralized fetch function with error handling,
 * base URL configuration, and response type normalization.
 */

import { API_BASE_URL } from '@/lib/constants';
import { ApiError } from '@/types/api';

/**
 * Fetch configuration options.
 */
interface FetchOptions extends RequestInit {
  /** Query parameters to append to the URL */
  params?: Record<string, string>;
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
    const errorBody = await response.json().catch(() => ({}));
    const message =
      typeof errorBody.message === 'string'
        ? errorBody.message
        : `HTTP ${response.status} ${response.statusText}`;

    throw {
      message,
      statusCode: response.status,
      ...(typeof errorBody === 'object' && errorBody !== null ? errorBody : {}),
    } satisfies ApiError;
  }

  return response.json();
}

/**
 * Make an authenticated API request.
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
  headers.set('Content-Type', 'application/json');

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
