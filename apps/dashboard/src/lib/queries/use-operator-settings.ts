/**
 * TanStack Query hook for operator settings.
 *
 * Uses React Query so DashboardShell `invalidateQueries` after login
 * re-fetches `/settings` without a manual Retry.
 */

import { useQuery } from '@tanstack/react-query';

import { getOperatorSettings } from '@/lib/api/settings';
import type { ApiError, OperatorSettingsResponseDto } from '@/types/api';

export const operatorSettingsKeys = {
  all: ['operator-settings'] as const,
  detail: () => [...operatorSettingsKeys.all, 'detail'] as const,
};

export function useOperatorSettings() {
  return useQuery<OperatorSettingsResponseDto, ApiError>({
    queryKey: operatorSettingsKeys.detail(),
    queryFn: () => getOperatorSettings(),
    staleTime: 30 * 1000,
    retry: (failureCount, error) => {
      if (error.statusCode === 401 || error.statusCode === 403) {
        return false;
      }
      return failureCount < 2;
    },
  });
}
