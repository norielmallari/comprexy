/**
 * TanStack Query hooks for the cost catalog.
 */

import { useQuery } from '@tanstack/react-query';

import { listCostModels } from '@/lib/api/cost-models';
import type { ApiError, CostModelDto } from '@/types/api';

export const costModelKeys = {
  all: ['cost-models'] as const,
  list: () => [...costModelKeys.all, 'list'] as const,
};

export function useCostModels() {
  return useQuery<CostModelDto[], ApiError>({
    queryKey: costModelKeys.list(),
    queryFn: () => listCostModels(),
    staleTime: 10 * 60 * 1000,
    retry: (failureCount, error) => {
      if (error.statusCode === 401 || error.statusCode === 403) {
        return false;
      }
      return failureCount < 2;
    },
  });
}
